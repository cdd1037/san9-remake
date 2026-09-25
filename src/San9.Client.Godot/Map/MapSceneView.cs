using Godot;
using San9.Content.Text;
using San9.Rendering;

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
    public int? Hovered { get; set; }
    internal Frame? Presented => _transition.Presented;
    private MapFrameTransition<Frame> _transition = null!;
    private TextureScope? _prefetchAssets;
    private TerrainChunkCache<ImageTexture>.Lease? _prefetchTerrain;
    private TextureScope _ui = null!;
    private Node2D _water = null!, _objects = null!, _annotations = null!;
    private ShaderMaterial _hoverMaterial = null!;
    private readonly List<Sprite2D> _sprites = [];
    private readonly Dictionary<string, int> _frames = [];
    private int? _drawnHover;
    private int _drawnTerrainCount;
    private long _drawnAssetRevision;
    private (MapScene Scene, MapViewport Camera, Vector2 Size, bool Boundaries)? _lastRequest;
    public long Revision { get; private set; }
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
        _hoverMaterial = new ShaderMaterial { Shader = new Shader { Code = "shader_type canvas_item; void fragment() { vec4 c = texture(TEXTURE, UV); COLOR = vec4(round(c.rgb * 255.0 * 0.6875 + vec3(240.0 * 0.3125)) / 255.0, round(c.a * 255.0 * 0.9375) / 255.0); }" } };
        _water = new Node2D { Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        _water.Draw += DrawWater; AddChild(_water);
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
        bool water = resourcesChanged, objects = hoverChanged || resourcesChanged;
        foreach (string id in frame.WaterClips.Concat(frame.ObjectClips).Distinct())
        {
            int current = MapAnimationClock.Frame(frame.Region.Scene.Clips[id], AnimationTime());
            if (_frames.TryGetValue(id, out int previous) && previous == current) continue;
            _frames[id] = current;
            water |= frame.WaterClips.Contains(id); objects |= frame.ObjectClips.Contains(id);
        }
        if (water) _water.QueueRedraw();
        if (objects) RebuildObjects();
        if (hoverChanged) _annotations.QueueRedraw();
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
    private void RebuildObjects()
    {
        if (Presented is not { } frame) return;
        ObjectDraws++; int cursor = 0; _drawnHover = Hovered; _drawnAssetRevision = frame.Assets.Revision;
        frame.Region.Objects(AnimationTime(), (id, position, building) =>
        {
            if (!frame.Assets.TryGetReady(id, out var texture)) return;
            if (!frame.Region.Bounds.Intersects(new Rect2(position, texture.GetSize()))) return;
            if (cursor == _sprites.Count) { var sprite = new Sprite2D { Centered = false }; _objects.AddChild(sprite); _sprites.Add(sprite); }
            var item = _sprites[cursor++]; item.Texture = texture; item.Position = position; item.Visible = true;
            item.Material = building is not null && building == Hovered ? _hoverMaterial : null;
        });
        for (int i = cursor; i < _sprites.Count; i++) { _sprites[i].Texture = null; _sprites[i].Visible = false; }
    }
    public void DrawAnnotations(Node2D target, Font font, TextCatalog texts, Func<Vector2, Vector2> project, float zoom)
    {
        if (Presented is not { } frame) return;
        foreach (var b in frame.Region.Buildings)
        {
            var s = frame.Region.Scene.Settlements[b.Id];
            var p = frame.Region.Scene.Anchor(b);
            if (!frame.Region.Bounds.Intersects(new Rect2(p, new(116, 55)))) continue;
            target.DrawSetTransform(project(p), 0, Vector2.One * zoom);
            var style = GameTextStyles.Building(b.Id == Hovered);
            GameTextPresenter.Draw(target, font, s.Soldiers.ToString(System.Globalization.CultureInfo.InvariantCulture), new(0, 16), style, HorizontalAlignment.Right, 72);
            Bar("durability", s.Durability, s.MaxDurability, new(0, 16));
            Bar("morale", s.Morale, 100, new(0, 29));
            GameTextPresenter.Draw(target, font, s.ResolveName(texts), new(0, 53), style);
            foreach (string icon in s.Icons) target.DrawTexture(_ui["icon-" + icon], icon == "emperor" ? new(96, 17) : new(75, 17));
        }
        target.DrawSetTransform(Vector2.Zero);
        void Bar(string kind, int current, int maximum, Vector2 point)
        {
            target.DrawTexture(_ui[$"bar-{kind}-frame"], point);
            int width = BuildingInfoGeometry.FillWidth(current, maximum);
            if (width == 0) return;
            var fill = _ui[$"bar-{kind}-{BuildingInfoGeometry.FillState(current, maximum)}"];
            target.DrawTextureRectRegion(fill, new(point + new Vector2(4, 4), new Vector2(width, fill.GetHeight())), new(0, 0, width, fill.GetHeight()));
        }
    }
    public override void _ExitTree()
    {
        foreach (var sprite in _sprites) sprite.Texture = null;
        _transition.Dispose(); _prefetchAssets?.Dispose(); _prefetchTerrain?.Dispose(); _ui.Dispose();
        var waterMaterial = _water.Material;
        Textures.Defer(() => { waterMaterial.Dispose(); _hoverMaterial.Shader.Dispose(); _hoverMaterial.Dispose(); });
    }
}
