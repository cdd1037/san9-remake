using Godot;
using San9.Content.Text;
using San9.Content.Maps;
using San9.Rendering;
using San9.Application.Presentation;
using San9.Content.Interface;

namespace San9.Client.Godot;

// An independent camera's complete frame and leases. No input or simulation ownership.
public partial class MapSceneView : Node2D
{
    internal sealed class Frame : IDisposable
    {
        private sealed record Target(MapViewport Camera, Vector2 Size, string[] Assets, TerrainChunkKey[] Terrain);
        public MapSceneRegion Region { get; }
        public MapViewport Camera => _target.Camera;
        public Vector2 Size => _target.Size;
        public Rect2 ViewportBounds => new(new Vector2((float)Camera.X, (float)Camera.Y) - Size / (2 * (float)Camera.Zoom), Size / (float)Camera.Zoom);
        public bool Boundaries { get; }
        public TextureScope Assets { get; }
        public TerrainChunkCache<ImageTexture>.Lease Terrain { get; }
        public HashSet<string> WaterClips { get; }
        public HashSet<string> ObjectClips { get; }
        private Target _target;
        private Target? _candidate;
        public Frame(MapSceneRegion region, MapViewport camera, Vector2 size, TextureScope assets,
            TerrainChunkCache<ImageTexture>.Lease terrain, bool boundaries)
        {
            Region = region; Assets = assets; Terrain = terrain; Boundaries = boundaries;
            WaterClips = region.Water.Select(s => s.ClipId).ToHashSet();
            ObjectClips = region.ObjectAnimations.Select(s => s.ClipId).ToHashSet();
            _target = Select(camera, size); Assets.Request(_target.Assets);
        }
        private Target Select(MapViewport camera, Vector2 size)
        {
            if (_candidate is { } existing && existing.Camera == camera && existing.Size == size) return existing;
            Vector2 half = size / (2 * (float)camera.Zoom);
            var bounds = new Rect2(new Vector2((float)camera.X, (float)camera.Y) - half, half * 2);
            return _candidate = new(camera, size, Region.Select(bounds).Assets.ToArray(),
                camera.Chunks(Region.Scene.Map.CanvasWidth, Region.Scene.Map.CanvasHeight, (int)size.X, (int)size.Y)
                    .Select(k => new TerrainChunkKey(Region.Scene.Terrain, k.X, k.Y)).ToArray());
        }
        public void Move(MapViewport camera, Vector2 size)
        { _target = Select(camera, size); Assets.Request(_target.Assets); }
        private bool IsReady(Target target) => Assets.AreReady(target.Assets) && target.Terrain.All(Terrain.IsAvailable);
        public bool Ready => IsReady(_target);
        public bool CanPresent(MapViewport camera, Vector2 size)
        {
            var target = Select(camera, size);
            // A probe must not start demand loads for a navigation that has not been committed.
            Assets.RequestCached(target.Assets);
            return IsReady(target);
        }
        public Exception? Error => Assets.ErrorFor(_target.Assets) ?? Terrain.Error;
        public void Dispose() { Assets.Dispose(); Terrain.Dispose(); }
    }
    public TextureService Textures { get; init; } = null!;
    public TerrainChunkCache<ImageTexture> Terrain { get; init; } = null!;
    public Func<double> AnimationTime { get; init; } = null!;
    public TextCatalog? Texts { get; set; }
    public Font? Font { get; set; }
    public bool ShowAnnotations { get; set; }
    public MapObjectTarget? Hovered { get; set; }
    public MilitaryTargets? Targets { get; set; }
    public PersonnelDraft? PersonnelTargets { get; set; }
    public CommandTargetSelection? CommandTargets { get; set; }
    public int? PersonnelHoverPlace { get; set; }
    private int? _drawnPersonnelHoverPlace;
    private readonly Dictionary<int, TargetMesh[]> _personnelRegionMeshes = [];
    public Vector2? ConstructionPointer { get; set; }
    private Vector2? _drawnConstructionPointer;
    private TextureScope? _previewAssets;
    private MapSprite[] _previewSprites = [];
    private long _previewRevision;
    internal Frame? Presented => _transition.Presented;
    private MapFrameTransition<Frame> _transition = null!;
    private TextureScope? _prefetchAssets;
    private TerrainChunkCache<ImageTexture>.Lease? _prefetchTerrain;
    private TextureScope _ui = null!;
    private Node2D _water = null!, _annotations = null!;
    private MapSpriteLayer _objects = null!;
    private ShaderMaterial _hoverMaterial = null!;
    private ShaderMaterial _outlineMaterial = null!;
    private ShaderMaterial _candidateMaterial = null!;
    private ShaderMaterial _unavailableMaterial = null!;
    private ShaderMaterial _regionHoverMaterial = null!;
    private readonly ShaderMaterial[] _meshMaterials = new ShaderMaterial[3];
    private readonly ImageTexture[] _meshTextures = new ImageTexture[16];
    private MilitaryTargets? _drawnTargets;
    private PersonnelDraft? _drawnPersonnelTargets;
    private CommandTargetSelection? _drawnCommandTargets;
    private TargetMesh[] _targetMeshes = [];
    private TargetMesh[] _visibleTargetMeshes = [];
    private MapSceneRegion? _meshRegion;
    private int _markerFrame = -1;
    private int _routeFrame = -1;
    private long _routeRevision = -1;
    private ShaderMaterial _routeMaterial = null!;
    private int _waypointFrame = -1;
    private long _drawnUiRevision;
    private CanvasItemMaterial _additiveMaterial = null!;
    private readonly Dictionary<string, int> _frames = [];
    private MapObjectTarget? _drawnHover;
    private int _drawnTerrainCount;
    private long _drawnAssetRevision;
    private double _drawnDayTime = -1;
    private int _annotationFrame = -1;
    private long _annotationUiRevision = -1;
    private (MapScene Scene, MapViewport Camera, Vector2 Size, bool Boundaries)? _lastRequest;
    public long Revision { get; private set; }
    public long AnnotationRevision { get; private set; }
    public long TerrainDraws { get; private set; }
    public long WaterDraws { get; private set; }
    public long ObjectDraws { get; private set; }
    public string? Error { get; private set; }
    public bool IsReady => Presented is not null && _transition.Pending is null && Error is null;
    public event Action? Committed;
    public override void _Ready()
    {
        _transition = new(f => f.Ready, f => f.Error, f => Textures.Defer(f.Dispose));
        _ui = Textures.CreateScope(owner: this);
        _ui.RequestPrefixes("bar-", "icon-");
        _outlineMaterial = new ShaderMaterial { Shader = new Shader { Code = """
            shader_type canvas_item;
            float solid(sampler2D source, vec2 uv) {
                if (any(lessThan(uv, vec2(0.0))) || any(greaterThanEqual(uv, vec2(1.0)))) return 0.0;
                return step(0.01, texture(source, uv).a);
            }
            void fragment() {
                vec2 p = TEXTURE_PIXEL_SIZE;
                float neighbors = max(max(solid(TEXTURE, UV-vec2(p.x,0)), solid(TEXTURE, UV+vec2(p.x,0))),
                    max(solid(TEXTURE, UV-vec2(0,p.y)), solid(TEXTURE, UV+vec2(0,p.y))));
                float edge = (1.0-solid(TEXTURE, UV)) * neighbors;
                // The original's opaque RGB555 outline resolves to pale cyan after channel quantization.
                COLOR = vec4(vec3(152.0,240.0,240.0)/255.0, edge);
            }
            """ } };
        _hoverMaterial = new ShaderMaterial { Shader = new Shader { Code = "shader_type canvas_item; void fragment() { vec4 c = texture(TEXTURE, UV); COLOR = vec4(round(c.rgb * 255.0 * 0.6875 + vec3(240.0 * 0.3125)) / 255.0, round(c.a * 255.0 * 0.9375) / 255.0); }" } };
        _routeMaterial = new ShaderMaterial { Shader = new Shader { Code = "shader_type canvas_item; render_mode blend_add; void fragment() { vec4 c = texture(TEXTURE, UV); COLOR = vec4(c.rgb * 0.75, c.a); }" } };
        _candidateMaterial = new ShaderMaterial { Shader = new Shader { Code = "shader_type canvas_item; void fragment() { vec4 c = texture(TEXTURE, UV); COLOR = vec4(round(c.rgb * 255.0 * 0.8125 + vec3(248.0 * 0.1875)) / 255.0, round(c.a * 255.0 * 0.9375) / 255.0); }" } };
        _unavailableMaterial = new ShaderMaterial { Shader = new Shader { Code = "shader_type canvas_item; void fragment() { vec4 c = texture(TEXTURE, UV); COLOR = vec4(round(c.rgb * 255.0 * 0.5625 + vec3(8.0 * 0.4375)) / 255.0, round(c.a * 255.0 * 0.9375) / 255.0); }" } };
        for (int mask = 0; mask < _meshTextures.Length; mask++)
        {
            var coverage = TargetOverlayGeometry.Coverage(mask);
            var rgba = new byte[64 * 44 * 4];
            for (int pixel = 0; pixel < coverage.Length; pixel++) rgba[pixel * 4 + 3] = coverage[pixel];
            using var image = Image.CreateFromData(64, 44, false, Image.Format.Rgba8, rgba);
            _meshTextures[mask] = ImageTexture.CreateFromImage(image);
        }
        var meshShader = new Shader { Code = """
            shader_type canvas_item;
            uniform sampler2D ground : hint_screen_texture, repeat_disable, filter_nearest;
            uniform vec3 overlay_rgb;
            uniform float weight;
            void fragment() {
                float coverage = texture(TEXTURE, UV).a;
                vec3 src = floor(round(overlay_rgb) / 8.0) * 8.0;
                vec3 dst = floor(round(texture(ground, SCREEN_UV).rgb * 255.0) / 8.0) * 8.0;
                dst = floor((src * weight + dst * (255.0 - weight)) / 2048.0) * 8.0;
                COLOR = vec4(dst / 255.0, coverage);
            }
            """ };
        for (int i = 0; i < _meshMaterials.Length; i++)
        {
            _meshMaterials[i] = new ShaderMaterial { Shader = meshShader };
            // Byte-valued RGB keeps overlay color independent of the texture's palette.
            _meshMaterials[i].SetShaderParameter("overlay_rgb", i == 0 ? Vector3.Zero : new Vector3(232, 16, 8));
            float opacity = i == 0 ? TargetOverlayGeometry.DimOpacity :
                i == 1 ? TargetOverlayGeometry.InfluenceOpacity : TargetOverlayGeometry.StrongInfluenceOpacity;
            _meshMaterials[i].SetShaderParameter("weight", opacity * 256);
        }
        _regionHoverMaterial = new ShaderMaterial { Shader = meshShader };
        _regionHoverMaterial.SetShaderParameter("overlay_rgb", new Vector3(208, 208, 208));
        _regionHoverMaterial.SetShaderParameter("weight", 64f);
        _water = new Node2D { Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        _water.Draw += DrawWater; AddChild(_water);
        _additiveMaterial = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        _objects = new MapSpriteLayer(); AddChild(_objects);
        _annotations = new Node2D();
        _annotations.Draw += () => { if (ShowAnnotations && Font is not null && Texts is not null) DrawAnnotations(_annotations, Font, Texts, p => p, 1); };
        AddChild(_annotations);
    }
    internal void Request(MapScene scene, MapViewport camera, Vector2 size, bool boundaries, bool prefetch = false)
    {
        var request = (scene, camera, size, boundaries);
        if (_lastRequest == request) return;
        _lastRequest = request; Error = null;
        Vector2 half = size / (2 * (float)camera.Zoom);
        var rect = new Rect2(new Vector2((float)camera.X, (float)camera.Y) - half, half * 2);
        // Camera motion inside a prepared region does not change resource ownership.
        bool Covers(Frame frame) => ReferenceEquals(frame.Region.Scene, scene) && frame.Boundaries == boundaries &&
            frame.Region.Bounds.Encloses(rect);
        if (Presented is { } presented && Covers(presented) && presented.CanPresent(camera, size))
        {
            _transition.CancelPending();
            presented.Move(camera, size);
            RebuildObjects(); _annotations.QueueRedraw();
            Revision++; Committed?.Invoke();
            return;
        }
        if (_transition.Pending is { } pending && Covers(pending))
        {
            pending.Move(camera, size); TryCommit(); return;
        }
        // Main-map coverage has slack; stationary previews retain their exact clipping rectangle.
        var coverage = prefetch ? rect.Grow(256) : rect;
        var region = scene.Select(coverage, boundaries);
        var assets = Textures.CreateScope(Textures.MapAssets, owner: this);
        var visible = camera.Chunks(scene.Map.CanvasWidth, scene.Map.CanvasHeight, (int)size.X, (int)size.Y).ToHashSet();
        var terrain = Terrain.Acquire(Keys(0).OrderBy(k => visible.Contains((k.X, k.Y)) ? 0 : 1));
        var next = new Frame(region, camera, size, assets, terrain, boundaries);
        _transition.Request(next);
        var oldAssets = _prefetchAssets; var oldTerrain = _prefetchTerrain;
        _prefetchAssets = null; _prefetchTerrain = null;
        if (prefetch)
        {
            _prefetchAssets = Textures.CreateScope(Textures.MapAssets, true, this);
            _prefetchAssets.Request(scene.Select(coverage.Grow(512), boundaries).Assets);
            _prefetchTerrain = Terrain.Acquire(Keys(1).Except(terrain.Keys), true);
        }
        // Transfer shared requests before releasing the old leases, including in-flight prefetch.
        oldAssets?.Dispose(); oldTerrain?.Dispose();
        TryCommit();
        IEnumerable<TerrainChunkKey> Keys(int margin) =>
            new MapViewport(coverage.GetCenter().X, coverage.GetCenter().Y, 1)
                .Chunks(scene.Map.CanvasWidth, scene.Map.CanvasHeight,
                    (int)Math.Ceiling(coverage.Size.X), (int)Math.Ceiling(coverage.Size.Y), margin)
                .Select(k => new TerrainChunkKey(scene.Terrain, k.X, k.Y));
    }
    private void TryCommit()
    {
        if (!_transition.TryCommit())
        {
            if (_transition.Error is { } error && Error != error.Message)
            {
                Error = error.Message; GD.PushError(Error);
                _prefetchAssets?.Dispose(); _prefetchAssets = null;
                _prefetchTerrain?.Dispose(); _prefetchTerrain = null;
            }
            return;
        }
        Revision++; _frames.Clear();
        foreach (string id in Presented!.WaterClips.Concat(Presented.ObjectClips).Distinct())
            _frames[id] = MapAnimationClock.Frame(Presented.Region.Scene.Clips[id], AnimationTime());
        RebuildObjects();
        QueueRedraw(); _water.QueueRedraw(); _annotations.QueueRedraw(); Committed?.Invoke();
    }
    internal void RetryFailedRequest() { if (Error is not null) _lastRequest = null; }
    private long _drawnUnitRevision = -1;
    public override void _Process(double delta)
    {
        TryCommit();
        if (_prefetchAssets is { IsReady: true } || _prefetchAssets?.Error is not null)
        { _prefetchAssets.Dispose(); _prefetchAssets = null; }
        if (_prefetchTerrain is { IsReady: true } || _prefetchTerrain?.Error is not null)
        { _prefetchTerrain.Dispose(); _prefetchTerrain = null; }
        if (Presented is not { } frame) return;
        int readyTerrain = frame.Terrain.Keys.Count(frame.Terrain.IsAvailable);
        if (readyTerrain != _drawnTerrainCount) QueueRedraw();
        bool hoverChanged = _drawnHover != Hovered || _drawnPersonnelHoverPlace != PersonnelHoverPlace;
        bool resourcesChanged = _drawnAssetRevision != frame.Assets.Revision;
        bool targetsChanged = _drawnTargets != Targets || _drawnPersonnelTargets != PersonnelTargets || _drawnCommandTargets != CommandTargets || _routeRevision != (Targets?.Revision ?? -1) ||
            _drawnConstructionPointer != ConstructionPointer || _previewRevision != (_previewAssets?.Revision ?? 0);
        bool markerChanged = (Targets is not null || PersonnelTargets?.Kind == "move" || CommandTargets is not null) && _markerFrame != (int)(AnimationTime() / 200) % 4;
        markerChanged |= Targets is not null && _routeFrame != (int)(AnimationTime() / 200) % 8;
        markerChanged |= Targets?.Waypoint is not null && _waypointFrame != (int)(AnimationTime() / 142) % 4;
        bool motion = frame.Region.Scene.Day is {} day && _drawnDayTime != day.VisualElapsed;
        long unitRevision = frame.Region.Scene.UnitAnimationRevision;
        bool membersChanged = _drawnUnitRevision != unitRevision;
        _drawnUnitRevision = unitRevision;
        bool water = resourcesChanged, objects = membersChanged || motion || hoverChanged || resourcesChanged || targetsChanged || markerChanged || _drawnUiRevision != _ui.Revision;
        foreach (string id in frame.WaterClips.Concat(frame.ObjectClips).Distinct())
        {
            int current = MapAnimationClock.Frame(frame.Region.Scene.Clips[id], AnimationTime());
            if (_frames.TryGetValue(id, out int previous) && previous == current) continue;
            _frames[id] = current;
            water |= frame.WaterClips.Contains(id); objects |= frame.ObjectClips.Contains(id);
        }
        if (water) _water.QueueRedraw();
        if (objects) RebuildObjects();
        if (motion) { _drawnDayTime = frame.Region.Scene.Day!.VisualElapsed; Revision++; }
        int annotationFrame = MapInfoBarAnimation.Frame(AnimationTime());
        bool barsChanged = _annotationLayout.HasAnimatedBars && _annotationFrame != annotationFrame;
        bool annotationAssetsChanged = _annotationUiRevision != _ui.Revision;
        _annotationFrame = annotationFrame; _annotationUiRevision = _ui.Revision;
        if (hoverChanged || motion || barsChanged || annotationAssetsChanged)
        { AnnotationRevision++; _annotations.QueueRedraw(); }
    }
    public override void _Draw()
    {
        TerrainDraws++;
        if (Presented is not { } frame) return;
        var rect = frame.Region.Bounds; var fill = frame.Assets["cloud-fill"];
        for (float y = 112 + MathF.Floor((rect.Position.Y - 112) / fill.GetHeight()) * fill.GetHeight(); y < rect.End.Y; y += fill.GetHeight())
        for (float x = 96 + MathF.Floor((rect.Position.X - 96) / fill.GetWidth()) * fill.GetWidth(); x < rect.End.X; x += fill.GetWidth())
            DrawTexture(fill, new(x, y));
        _drawnTerrainCount = 0;
        foreach (var key in frame.Terrain.Keys)
        {
            if (!frame.Terrain.IsAvailable(key)) continue;
            DrawTexture(frame.Terrain.Get(key), new(key.X * 512, key.Y * 512)); _drawnTerrainCount++;
        }
    }
    private void DrawWater()
    {
        WaterDraws++;
        if (Presented is not { } frame) return;
        foreach (var s in frame.Region.Water)
        {
            string id = MapAnimationClock.Asset(frame.Region.Scene.Clips[s.ClipId], frame.Region.Scene.Season, AnimationTime(), s.Phase);
            if (frame.Assets.TryGetReady(id, out var texture)) _water.DrawTexture(texture, new(s.X, s.Y));
        }
    }
    private readonly List<(string Asset, Vector2 Position, MapObjectTarget Target)> _hitSprites = [];
    public MapObjectTarget? ObjectAt(Vector2 point)
    {
        if (_annotationLayout.Hit(point) is { } label) return label;
        for (int i = _hitSprites.Count - 1; i >= 0; i--)
        {
            var sprite = _hitSprites[i];
            if (Textures.Hit(Textures.MapAssets[sprite.Asset], (int)Math.Floor(point.X - sprite.Position.X),
                (int)Math.Floor(point.Y - sprite.Position.Y))) return sprite.Target;
        }
        return null;
    }
    private void RebuildObjects()
    {
        if (Presented is not { } frame) return;
        _hitSprites.Clear();
        ObjectDraws++; _objects.Begin(); _drawnHover = Hovered; _drawnPersonnelHoverPlace = PersonnelHoverPlace; _drawnAssetRevision = frame.Assets.Revision;
        if (_drawnTargets != Targets || _drawnPersonnelTargets != PersonnelTargets || _drawnCommandTargets != CommandTargets)
        {
            _previewAssets?.Dispose(); _previewAssets = null; _previewSprites = [];
            if (Targets is SpecialDeploymentTargets { PreviewFacilityId: int type })
            {
                _previewSprites = BuildingVisuals.ConstructionPreview(frame.Region.Scene.Map,type);
                _previewAssets = Textures.CreateScope(Textures.MapAssets,owner:this);
                _previewAssets.Request(_previewSprites.Select(s=>s.AssetId));
            }
            _drawnTargets = Targets;
            _drawnPersonnelTargets = PersonnelTargets;
            _drawnCommandTargets = CommandTargets;
            _targetMeshes = Targets is { } targets ? TargetOverlayGeometry.Meshes(frame.Region.Scene.Map, targets.ClearCells, targets.InfluenceCells).ToArray() :
                PersonnelTargets is {} personnel ? TargetOverlayGeometry.Meshes(frame.Region.Scene.Map, personnel.TargetCells(), System.Collections.Immutable.ImmutableHashSet<San9.Core.Domain.MapCell>.Empty).ToArray() :
                CommandTargets is {} command ? TargetOverlayGeometry.Meshes(frame.Region.Scene.Map, command.ClearCells, System.Collections.Immutable.ImmutableHashSet<San9.Core.Domain.MapCell>.Empty).ToArray() : [];
            _personnelRegionMeshes.Clear();
            if (PersonnelTargets is { Kind: "search" } search)
            {
                var map = frame.Region.Scene.Map;
                var empty = System.Collections.Immutable.ImmutableHashSet<San9.Core.Domain.MapCell>.Empty;
                var meshes = TargetOverlayGeometry.Meshes(map, empty, empty).ToArray();
                foreach (var group in meshes.Select((mesh, index) => (Mesh: mesh, Place: search.PlaceAt(new(index % map.Width, index / map.Width))))
                    .Where(item => item.Place is not null).GroupBy(item => item.Place!.Value))
                    _personnelRegionMeshes.Add(group.Key, group.Select(item => item.Mesh).ToArray());
            }
            _meshRegion = null;
            if (Targets is not null || PersonnelTargets?.Kind == "move" || CommandTargets is not null) _ui.Request(TargetSelectionSkin.RequiredRoles.Where(r => r.StartsWith("target-marker-", StringComparison.Ordinal) || r.StartsWith("target-route-", StringComparison.Ordinal) || r.StartsWith("target-waypoint-", StringComparison.Ordinal)));
        }
        _drawnUiRevision = _ui.Revision;
        _drawnConstructionPointer = ConstructionPointer;
        _previewRevision = _previewAssets?.Revision ?? 0;
        if (_meshRegion != frame.Region)
        {
            _meshRegion = frame.Region;
            _visibleTargetMeshes = _targetMeshes.Where(m => frame.Region.Bounds.Intersects(new Rect2(m.X, m.Y, 64, 44))).ToArray();
        }
        _markerFrame = (int)(AnimationTime() / 200) % 4;
        _routeFrame = (int)(AnimationTime() / 200) % 8;
        _routeRevision = Targets?.Revision ?? -1;
        _waypointFrame = (int)(AnimationTime() / 142) % 4;
        void Add(Texture2D texture, Vector2 position, Material? material = null)
        {
            if (!frame.ViewportBounds.Intersects(new Rect2(position, texture.GetSize()))) return;
            _objects.Add(texture, position, material);
        }
        (double Depth, Action Draw)? waypointOverlay = null;
        if (Targets?.Waypoint is { } via)
        {
            string season = frame.Region.Scene.Season is "spring" or "summer" ? "spring-summer" : "autumn-winter";
            if (_ui.TryGetReady($"target-waypoint-{season}-{_waypointFrame}", out var flag))
            {
                var p = RoutePreviewGeometry.Waypoint(frame.Region.Scene.Map, via, flag.GetWidth(), flag.GetHeight());
                waypointOverlay = (via.X + via.Y, () => Add(flag, new(p.X, p.Y)));
            }
        }
        frame.Region.Objects(AnimationTime(), (id, position, building, blend, highlightable) =>
        {
            if (!frame.Assets.TryGetReady(id, out var texture)) return;
            if (!frame.ViewportBounds.Intersects(new Rect2(position, texture.GetSize()))) return;
            if (building is {} target && blend == MapBlendMode.Alpha) _hitSprites.Add((id, position, target));
            bool candidate = building is {} mapObject && (CommandTargets?.Objects.Contains(new(mapObject.Id,mapObject.IsUnit)) == true || mapObject is { IsUnit: false } targetBuilding &&
                (Targets?.Candidates.ContainsKey(new(targetBuilding.Id, false)) == true ||
                    PersonnelTargets is { Kind: "move" } movementCandidates && movementCandidates.Targets.Contains(targetBuilding.Id)));
            Add(texture, position, blend == MapBlendMode.Additive ? _additiveMaterial :
                highlightable && candidate ? _candidateMaterial :
                highlightable && (CommandTargets is not null && building is not null || building is { IsUnit: false } && PersonnelTargets?.Kind == "move") ? _unavailableMaterial :
                highlightable && building is not null && building == Hovered ? _hoverMaterial : null);
        }, () =>
        {
            foreach (var mesh in _visibleTargetMeshes)
                Add(_meshTextures[mesh.ElevationMask], new(mesh.X, mesh.Y), _meshMaterials[mesh.Opacity == TargetOverlayGeometry.DimOpacity ? 0 :
                        mesh.Opacity == TargetOverlayGeometry.InfluenceOpacity ? 1 : 2]);
            if (PersonnelHoverPlace is int place && _personnelRegionMeshes.TryGetValue(place, out var regionMeshes))
                foreach (var mesh in regionMeshes)
                    Add(_meshTextures[mesh.ElevationMask], new(mesh.X, mesh.Y), _regionHoverMaterial);
            if (Targets is { } route)
            {
                for (int i = 0; i < route.Route.Length; i++)
                {
                    var p = RoutePreviewGeometry.Glow(frame.Region.Scene.Map, route.Route[i]);
                    string role = $"target-route-{RoutePreviewGeometry.Frame(AnimationTime(), i)}";
                    if (_ui.TryGetReady(role, out var glow)) Add(glow, new(p.X, p.Y), _routeMaterial);
                }

            }
        }, waypointOverlay, (id, position) =>
        {
            if (frame.Assets.TryGetReady(id, out var texture)) Add(texture, position, _outlineMaterial);
        }, frame.ViewportBounds);
        if (Targets is { } selection)
        {
            void Marker(MilitaryTarget target, Vector2 position)
            {
                if (!selection.Candidates.TryGetValue(target, out var kind)) return;
                string role = $"target-marker-{(kind == MilitaryMarker.Attack ? "attack" : "enter")}-{_markerFrame}";
                if (_ui.TryGetReady(role, out var texture)) Add(texture, position);
            }
            foreach (var building in frame.Region.Buildings)
            {
                var offset = Textures.BuildingTargetOffsets[building.FixedType];
                var point = TargetOverlayGeometry.BuildingMarker(frame.Region.Scene.Map, building, offset.X, offset.Y);
                Marker(new(building.Id, false), new(point.X, point.Y));
            }
            foreach (var unit in frame.Region.Units) Marker(new(unit.Id, true), new(unit.AnchorX - 16, unit.AnchorY - 48));
        }
        if (PersonnelTargets is { Kind: "move" } movement && _ui.TryGetReady($"target-marker-enter-{_markerFrame}", out var arrow))
            foreach (var building in frame.Region.Buildings.Where(b => movement.Targets.Contains(b.Id)))
            {
                var offset = Textures.BuildingTargetOffsets[building.FixedType];
                var point = TargetOverlayGeometry.BuildingMarker(frame.Region.Scene.Map, building, offset.X, offset.Y);
                Add(arrow, new(point.X, point.Y));
            }
        if (CommandTargets is {} commandSelection)
        {
            string role = commandSelection.Marker switch
            {
                CommandTargetMarker.Enter => "enter", CommandTargetMarker.Attack => "attack",
                CommandTargetMarker.Hostile => "hostile", CommandTargetMarker.Friendly => "friendly",
                _ => throw new InvalidOperationException("Unknown command target marker.")
            };
            if (_ui.TryGetReady($"target-marker-{role}-{_markerFrame}", out var commandArrow))
            {
                bool directional = commandSelection.Marker is CommandTargetMarker.Hostile or CommandTargetMarker.Friendly;
                foreach (var building in frame.Region.Buildings.Where(b => commandSelection.Objects.Contains(new(b.Id,false))))
                {
                    var offset = (directional ? Textures.BuildingArrowOffsets : Textures.BuildingTargetOffsets)[building.FixedType];
                    var point = TargetOverlayGeometry.BuildingMarker(frame.Region.Scene.Map,building,offset.X,offset.Y);
                    Add(commandArrow,new(point.X,point.Y));
                }
                foreach (var unit in frame.Region.Units.Where(u => commandSelection.Objects.Contains(new(u.Id,true))))
                    Add(commandArrow,directional ? new(unit.AnchorX-28,unit.AnchorY-36) : new(unit.AnchorX-16,unit.AnchorY-48));
            }
        }
        if (Targets is SpecialDeploymentTargets { IsConstruction:true } construction)
        {
            if (construction.Selected?.Position is { } selected)
            {
                var cell = San9.Core.Domain.MapCoordinates.Cell(selected);
                var center = MapGeometry.Center(cell.X,cell.Y);
                Preview(new(center.X,center.Y-10*(frame.Region.Scene.Map.Squares[cell.Y*frame.Region.Scene.Map.Width+cell.X].Elevation-1)),96f/256);
            }
            if (!construction.EditingPolicies && ConstructionPointer is { } cursor)
            {
                bool valid = MapGeometry.TrySquare(cursor.X,cursor.Y,out int x,out int y) && construction.At(new(x,y)) is not null;
                Preview(cursor,(valid?192f:96f)/256);
            }

            void Preview(Vector2 position, float opacity)
            {
                foreach (var sprite in _previewSprites)
                    if (_previewAssets!.TryGetReady(sprite.AssetId,out var texture))
                        _objects.Add(texture,position+new Vector2(sprite.X,sprite.Y),null,new Color(1,1,1,opacity));
            }
        }
        _objects.End();
        PrepareAnnotations();
    }
    private readonly MapAnnotationLayer _annotationLayout = new();
    private void PrepareAnnotations()
    {
        if (Presented is { } frame && Font is not null && Texts is not null)
            _annotationLayout.Prepare(frame, Font, Texts);
    }
    public void ConfigureAnnotations(Font font, TextCatalog texts)
    { Font = font; Texts = texts; PrepareAnnotations(); AnnotationRevision++; }
    public void DrawAnnotations(Node2D target, Font font, TextCatalog texts, Func<Vector2, Vector2> project, float zoom) =>
        _annotationLayout.Draw(target, font, _ui, Hovered, project, zoom, AnimationTime());
    public override void _ExitTree()
    {
        _objects.Clear();
        _transition.Dispose(); _prefetchAssets?.Dispose(); _prefetchTerrain?.Dispose(); _previewAssets?.Dispose(); _ui.Dispose();
        var waterMaterial = _water.Material;
        Textures.Defer(() => {
            waterMaterial.Dispose(); _additiveMaterial.Dispose();
            _routeMaterial.Shader.Dispose(); _routeMaterial.Dispose(); _hoverMaterial.Shader.Dispose(); _hoverMaterial.Dispose();
            _outlineMaterial.Shader.Dispose(); _outlineMaterial.Dispose();
            _candidateMaterial.Shader.Dispose(); _candidateMaterial.Dispose();
            _unavailableMaterial.Shader.Dispose(); _unavailableMaterial.Dispose();
            _regionHoverMaterial.Dispose();
            foreach (var texture in _meshTextures) texture.Dispose();
            _meshMaterials[0].Shader.Dispose(); foreach (var material in _meshMaterials) material.Dispose();
        });
    }
}
