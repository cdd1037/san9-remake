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
    public DeploymentTargets? Targets { get; set; }
    internal Frame? Presented => _transition.Presented;
    private MapFrameTransition<Frame> _transition = null!;
    private TextureScope? _prefetchAssets;
    private TerrainChunkCache<ImageTexture>.Lease? _prefetchTerrain;
    private TextureScope _ui = null!;
    private Node2D _water = null!, _objects = null!, _annotations = null!;
    private ShaderMaterial _hoverMaterial = null!;
    private ShaderMaterial _outlineMaterial = null!;
    private ShaderMaterial _candidateMaterial = null!;
    private readonly ShaderMaterial[] _meshMaterials = new ShaderMaterial[3];
    private readonly ImageTexture[] _meshTextures = new ImageTexture[16];
    private DeploymentTargets? _drawnTargets;
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
    private readonly List<Sprite2D> _sprites = [];
    private readonly Dictionary<string, int> _frames = [];
    private MapObjectTarget? _drawnHover;
    private int _drawnTerrainCount;
    private long _drawnAssetRevision;
    private double _drawnDayTime = -1;
    private int _annotationFrame = -1;
    private long _annotationUiRevision = -1;
    private bool _hasAnimatedBars;
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
        _ui.Request(InterfaceSkin.RequiredUiAssets.Where(role => role.StartsWith("bar-", StringComparison.Ordinal)));
        _outlineMaterial = new ShaderMaterial { Shader = new Shader { Code = """
            shader_type canvas_item;
            void fragment() {
                vec2 p = TEXTURE_PIXEL_SIZE;
                float a = texture(TEXTURE, UV).a;
                float l = UV.x > p.x ? texture(TEXTURE, UV-vec2(p.x,0)).a : 0.0;
                float u = UV.y > p.y ? texture(TEXTURE, UV-vec2(0,p.y)).a : 0.0;
                float edge = max(abs(step(0.01,a)-step(0.01,l)), abs(step(0.01,a)-step(0.01,u)));
                COLOR = vec4(0.973,0.973,0.627,edge * 0.65);
            }
            """ } };
        _hoverMaterial = new ShaderMaterial { Shader = new Shader { Code = "shader_type canvas_item; void fragment() { vec4 c = texture(TEXTURE, UV); COLOR = vec4(round(c.rgb * 255.0 * 0.6875 + vec3(240.0 * 0.3125)) / 255.0, round(c.a * 255.0 * 0.9375) / 255.0); }" } };
        _routeMaterial = new ShaderMaterial { Shader = new Shader { Code = "shader_type canvas_item; render_mode blend_add; void fragment() { vec4 c = texture(TEXTURE, UV); COLOR = vec4(c.rgb * 0.75, c.a); }" } };
        _candidateMaterial = new ShaderMaterial { Shader = new Shader { Code = "shader_type canvas_item; void fragment() { vec4 c = texture(TEXTURE, UV); COLOR = vec4(round(c.rgb * 255.0 * 0.8125 + vec3(248.0 * 0.1875)) / 255.0, round(c.a * 255.0 * 0.9375) / 255.0); }" } };
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
        _water = new Node2D { Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        _water.Draw += DrawWater; AddChild(_water);
        _additiveMaterial = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        _objects = new Node2D(); AddChild(_objects);
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
        bool hoverChanged = _drawnHover != Hovered;
        bool resourcesChanged = _drawnAssetRevision != frame.Assets.Revision;
        bool targetsChanged = _drawnTargets != Targets || _routeRevision != (Targets?.Revision ?? -1);
        bool markerChanged = Targets is not null && _markerFrame != (int)(AnimationTime() / 200) % 4;
        markerChanged |= Targets is not null && _routeFrame != (int)(AnimationTime() / 200) % 8;
        markerChanged |= Targets?.Waypoint is not null && _waypointFrame != (int)(AnimationTime() / 142) % 4;
        bool motion = frame.Region.Scene.Day is {} day && _drawnDayTime != day.VisualElapsed;
        bool water = resourcesChanged, objects = motion || hoverChanged || resourcesChanged || targetsChanged || markerChanged || _drawnUiRevision != _ui.Revision;
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
        bool barsChanged = _hasAnimatedBars && _annotationFrame != annotationFrame;
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
    private readonly List<(Rect2 Bounds, int UnitId)> _hitLabels = [];
    private Frame? _labelFrame;
    public MapObjectTarget? ObjectAt(Vector2 point)
    {
        // Labels are painted over map objects; overlapping labels follow their draw order.
        if (ReferenceEquals(_labelFrame, Presented))
            for (int i = _hitLabels.Count - 1; i >= 0; i--)
                if (_hitLabels[i].Bounds.HasPoint(point)) return new(_hitLabels[i].UnitId, true);
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
        ObjectDraws++; int cursor = 0; _drawnHover = Hovered; _drawnAssetRevision = frame.Assets.Revision;
        if (_drawnTargets != Targets)
        {
            _drawnTargets = Targets;
            _targetMeshes = Targets is { } targets ? TargetOverlayGeometry.Meshes(frame.Region.Scene.Map, targets.ClearCells, targets.InfluenceCells).ToArray() : [];
            _meshRegion = null;
            if (Targets is not null) _ui.Request(TargetSelectionSkin.RequiredRoles.Where(r => r.StartsWith("target-marker-", StringComparison.Ordinal) || r.StartsWith("target-route-", StringComparison.Ordinal) || r.StartsWith("target-waypoint-", StringComparison.Ordinal)));
        }
        _drawnUiRevision = _ui.Revision;
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
            if (cursor == _sprites.Count) { var sprite = new Sprite2D { Centered = false }; _objects.AddChild(sprite); _sprites.Add(sprite); }
            var item = _sprites[cursor++]; item.Texture = texture; item.Position = position; item.Visible = true;
            item.Material = material; item.Modulate = Colors.White;
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
            bool candidate = building is { IsUnit: false } targetBuilding && Targets?.Candidates.ContainsKey(new(targetBuilding.Id, false)) == true;
            Add(texture, position, blend == MapBlendMode.Additive ? _additiveMaterial :
                highlightable && candidate ? _candidateMaterial : highlightable && building is not null && building == Hovered ? _hoverMaterial : null);
        }, () =>
        {
            foreach (var mesh in _visibleTargetMeshes)
                Add(_meshTextures[mesh.ElevationMask], new(mesh.X, mesh.Y), _meshMaterials[mesh.Opacity == TargetOverlayGeometry.DimOpacity ? 0 :
                        mesh.Opacity == TargetOverlayGeometry.InfluenceOpacity ? 1 : 2]);
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
            void Marker(DeploymentTarget target, Vector2 position)
            {
                if (!selection.Candidates.TryGetValue(target, out var kind)) return;
                string role = $"target-marker-{(kind == DeploymentMarker.Attack ? "attack" : "enter")}-{_markerFrame}";
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
        for (int i = cursor; i < _sprites.Count; i++) { _sprites[i].Texture = null; _sprites[i].Visible = false; }
    }
    public void DrawAnnotations(Node2D target, Font font, TextCatalog texts, Func<Vector2, Vector2> project, float zoom)
    {
        if (Presented is not { } frame) return;
        _hitLabels.Clear(); _labelFrame = frame;
        _hasAnimatedBars = false;
        double barTime = AnimationTime();
        foreach (var b in frame.Region.Buildings)
        {
            var s = frame.Region.Scene.Settlements[b.Id];
            var p = frame.Region.Scene.Anchor(b);
            if (!frame.Region.Bounds.Intersects(new Rect2(p, new(116, 55)))) continue;
            target.DrawSetTransform(project(p), 0, Vector2.One * zoom);
            var style = GameTextStyles.Building(Hovered == new MapObjectTarget(b.Id, false));
            GameTextPresenter.Draw(target, font, s.Soldiers.ToString(System.Globalization.CultureInfo.InvariantCulture), new(0, 16), style, HorizontalAlignment.Right, 72);
            Bar("durability", s.Durability, s.MaxDurability, new(0, 16), b.Id);
            Bar("morale", s.Morale, 100, new(0, 29), b.Id);
            GameTextPresenter.Draw(target, font, s.ResolveName(texts), new(0, 53), style);
            foreach (string icon in s.Icons) target.DrawTexture(_ui["icon-" + icon], icon == "emperor" ? new(96, 17) : new(75, 17));
        }
        foreach (var unit in frame.Region.Units.Where(u => frame.Region.Scene.IsUnitVisible(u, frame.ViewportBounds)).Select(u => frame.Region.Scene.Sample(u)))
        {
            string unitName = unit.CommanderNameKey is { } nameKey ? texts.Resolve(nameKey) + "队" : "部队";
            int fontSize = GameTextStyles.Building(false).FontSize;
            float width = Math.Max(76, font.GetStringSize(unitName, fontSize: fontSize).X);
            var bounds = new Rect2(unit.LabelX, unit.LabelY, width, 47 + font.GetDescent(fontSize));
            if (!frame.Region.Bounds.Intersects(bounds)) continue;
            _hitLabels.Add((bounds, unit.Id));
            target.DrawSetTransform(project(new(unit.LabelX, unit.LabelY)), 0, Vector2.One * zoom);
            GameTextPresenter.Draw(target, font, unit.Soldiers.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new(0, 20), GameTextStyles.Building(Hovered == new MapObjectTarget(unit.Id, true)), HorizontalAlignment.Right, 72);
            Bar("morale", unit.Morale, 100, new(0, 20), unit.Id);
            GameTextPresenter.Draw(target, font, unitName,
                new(0, 47), GameTextStyles.Building(Hovered == new MapObjectTarget(unit.Id, true)));
        }
        target.DrawSetTransform(Vector2.Zero);
        void Bar(string kind, int current, int maximum, Vector2 point, int objectId)
        {
            target.DrawTexture(_ui[$"bar-{kind}-frame"], point);
            int width = BuildingInfoGeometry.FillWidth(current, maximum);
            if (width == 0) return;
            _hasAnimatedBars |= BuildingInfoGeometry.FillState(current, maximum) != "normal";
            var blend = MapInfoBarAnimation.Sample(current, maximum, barTime, objectId);
            Layer("normal", blend.Normal);
            Layer("warning", blend.Warning);
            Layer("critical", blend.Critical);
            void Layer(string state, float opacity)
            {
                if (opacity <= 0) return;
                var fill = _ui[$"bar-{kind}-{state}"];
                target.DrawTextureRectRegion(fill, new(point + new Vector2(4, 4), new Vector2(width, fill.GetHeight())),
                    new(0, 0, width, fill.GetHeight()), new Color(1, 1, 1, opacity));
            }
        }
    }
    public override void _ExitTree()
    {
        foreach (var sprite in _sprites) sprite.Texture = null;
        _transition.Dispose(); _prefetchAssets?.Dispose(); _prefetchTerrain?.Dispose(); _ui.Dispose();
        var waterMaterial = _water.Material;
        Textures.Defer(() => {
            waterMaterial.Dispose(); _additiveMaterial.Dispose();
            _routeMaterial.Shader.Dispose(); _routeMaterial.Dispose(); _hoverMaterial.Shader.Dispose(); _hoverMaterial.Dispose();
            _outlineMaterial.Shader.Dispose(); _outlineMaterial.Dispose();
            _candidateMaterial.Shader.Dispose(); _candidateMaterial.Dispose();
            foreach (var texture in _meshTextures) texture.Dispose();
            _meshMaterials[0].Shader.Dispose(); foreach (var material in _meshMaterials) material.Dispose();
        });
    }
}
