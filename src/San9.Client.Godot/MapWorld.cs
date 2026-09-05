using San9.Content.Images;
using Godot;
using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class MapWorld : Node2D
{
    private Camera2D _camera = null!;
    private Node2D _waterLayer = null!, _objectLayer = null!;
    private Dictionary<string, MapAnimationClip> _clips = [];
    private Dictionary<int, MapAnimatedSprite[]> _buildingAnimations = [];
    private readonly System.Diagnostics.Stopwatch _animationWatch = new();
    private double? _fixedAnimationTime;
    private long _lastAnimationTick = -1;
    private double AnimationTime => _fixedAnimationTime ?? _animationWatch.Elapsed.TotalMilliseconds;
    private MapPackage? _map;
    private TerrainRenderer? _renderer;
    private Dictionary<string, PngImage>? _images;
    private readonly Dictionary<string, ImageTexture> _objects = [];
    private readonly Dictionary<Vector2I, CachedChunk> _chunks = [];
    private readonly Dictionary<Vector2I, Task<PngImage>> _pending = [];
    private CancellationTokenSource _cancellation = new();
    private Task<PreparedMap>? _loading;
    private string _packageRoot = "", _season = "";
    private bool _dragging;
    private string? _error;
    private readonly System.Diagnostics.Stopwatch _loadWatch = new();
    private long _tick;
    private Vector2I? _selected;
    public event Action<int>? BuildingPicked;
    public event Action<int, int>? SquarePicked;
    public event Action? SelectionCleared;
    public void Highlight((int X, int Y)? square) { _selected = square is {} p ? new(p.X, p.Y) : null; QueueRedraw(); }
    public long CacheBytes => _chunks.Values.Sum(c => c.Bytes);
    public bool IsVisibleReady => _renderer is not null && _loading is null && VisibleChunks(_camera.Position, ZoomFactor, _map!, 0).All(_chunks.ContainsKey);
    public MapPackage? Package => _map;
    public string Season => _season;
    public string StatusText { get; private set; } = "正在准备地图…";
    public bool IsLoading => _loading is not null;
    public string? LoadError => _error;
    public event Action? MapReady;
    public Func<Vector2, bool>? IsInputBlocked { get; set; }
    public PngImage AssetPixels(string id) => _images![id];
    public Vector2 CameraPosition => _camera.Position;
    private const long CacheBudget = 256L * 1024 * 1024;
    private sealed record CachedChunk(ImageTexture Texture, long Bytes, long LastUse);
    private sealed record PreparedMap(MapPackage Map, Dictionary<string, PngImage> Images, TerrainRenderer Renderer, string Season, Dictionary<Vector2I, PngImage> Chunks);
    public float ZoomFactor => _camera.Zoom.X;

    public override void _Ready()
    {
        _waterLayer = new Node2D { Name = "Water", Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        _objectLayer = new Node2D { Name = "Objects" };
        _waterLayer.Draw += DrawWater; _objectLayer.Draw += DrawObjects;
        AddChild(_waterLayer); AddChild(_objectLayer);
        _camera = GetNode<Camera2D>("Camera");
        var center = MapGeometry.Center(89, 64); _camera.Position = new(center.X, center.Y);
    }
    public void LoadMap(MapPackage map, string root, string? season = null, double? animationTime = null)
    {
        _map = map; _packageRoot = root; _fixedAnimationTime = animationTime;
        BeginLoad(season);
    }
    private void BeginLoad(string? season)
    {
        _cancellation.Cancel(); _cancellation.Dispose(); _cancellation = new();
        _pending.Clear(); var token = _cancellation.Token;
        var oldMap = _map; var oldImages = _images;
        Vector2 position = _camera.Position; float zoom = ZoomFactor;
        StatusText = "正在校验并准备地图…";
        _error = null; _loadWatch.Restart();
        _loading = Task.Run(() =>
        {
            var map = oldMap ?? throw new InvalidOperationException("Load a map before previewing a season.");
            var common = oldImages ?? ImageAssetFiles.Load(_packageRoot, map.Assets, token);
            string selected = season ?? map.InitialSeason;
            var definition = map.Seasons.SingleOrDefault(s => s.Id == selected) ?? throw new InvalidDataException("Unknown season.");
            var land = ImageAssetFiles.Load(_packageRoot, definition.Textures, token).ToDictionary(p => int.Parse(p.Key), p => p.Value);
            var renderer = new TerrainRenderer(map, common, land);
            Dictionary<Vector2I, PngImage> prepared = [];
            foreach (var key in VisibleChunks(position, zoom, map, 0)) prepared.Add(key, renderer.RenderChunk(key.X, key.Y, token));
            return new PreparedMap(map, common, renderer, selected, prepared);
        }, token);
    }
    private static IEnumerable<Vector2I> VisibleChunks(Vector2 center, float zoom, MapPackage map, int margin)
    {
        Vector2 half = new Vector2(ClientLayout.LogicalWidth, ClientLayout.LogicalHeight) / (2 * zoom);
        int x0 = Math.Max(0, (int)Math.Floor((center.X - half.X) / 512) - margin), y0 = Math.Max(0, (int)Math.Floor((center.Y - half.Y) / 512) - margin);
        int x1 = Math.Min((map.CanvasWidth - 1) / 512, (int)Math.Floor((center.X + half.X) / 512) + margin), y1 = Math.Min((map.CanvasHeight - 1) / 512, (int)Math.Floor((center.Y + half.Y) / 512) + margin);
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) yield return new(x, y);
    }
    public override void _Process(double delta)
    {
        _tick++;
        if (!Input.IsMouseButtonPressed(MouseButton.Right)) _dragging = false;
        if (_loading is { IsCompleted: true })
        {
            var completed = _loading; _loading = null;
            try
            {
                var loaded = completed.GetAwaiter().GetResult();
                foreach (var c in _chunks.Values) c.Texture.Dispose(); _chunks.Clear();
                _map = loaded.Map; _images = loaded.Images; _renderer = loaded.Renderer; _season = loaded.Season;
                _clips = _map.Animations.Clips.ToDictionary(c => c.Id);
                _buildingAnimations = _map.Animations.Buildings.GroupBy(s => s.BuildingId).ToDictionary(g => g.Key, g => g.ToArray());
                if (_objects.Count == 0) foreach (var sprite in _map.Boundaries.Concat(_map.Bridges).Concat(_map.Buildings.SelectMany(b => b.Sprites)).Concat(_map.Clouds))
                    if (!_objects.ContainsKey(sprite.AssetId)) _objects.Add(sprite.AssetId, Texture(_images[sprite.AssetId]));
                foreach (string frame in _clips.Values.SelectMany(c => c.Frames.Concat(c.SeasonFrames.Values.SelectMany(f => f))).Distinct())
                    if (!_objects.ContainsKey(frame)) _objects.Add(frame, Texture(_images[frame]));
                if (!_animationWatch.IsRunning) _animationWatch.Start();
                foreach (var pair in loaded.Chunks) Cache(pair.Key, pair.Value);
                MapReady?.Invoke(); QueueRedraw();
                GD.Print($"MAP_READY season={_season} seconds={_loadWatch.Elapsed.TotalSeconds:F3} working_set_mib={System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576}");
            }
            catch (Exception e)
            {
                _error = $"地图加载失败：{e.Message}\n请运行 San9.Tools import-map 生成本地地图包。"; StatusText = _error;
                GD.PushError(e.Message);
            }
        }
        if (_map is null || _renderer is null) return;
        long animationTick = (long)(AnimationTime / 16);
        if (_lastAnimationTick != animationTick)
        { _lastAnimationTick = animationTick; _waterLayer.QueueRedraw(); _objectLayer.QueueRedraw(); }
        foreach (var pair in _pending.Where(p => p.Value.IsCompleted).Take(1).ToArray())
        {
            _pending.Remove(pair.Key);
            try { Cache(pair.Key, pair.Value.GetAwaiter().GetResult()); QueueRedraw(); }
            catch (OperationCanceledException) { }
            catch (Exception e) { _error = e.Message; StatusText = _error; GD.PushError(e.Message); }
        }
        var wanted = VisibleChunks(_camera.Position, ZoomFactor, _map, 1).ToHashSet();
        var visible = VisibleChunks(_camera.Position, ZoomFactor, _map, 0).ToHashSet();
        foreach (var key in wanted)
            if (_chunks.TryGetValue(key, out var chunk)) _chunks[key] = chunk with { LastUse = _tick };
        if (_loading is null)
            foreach (var key in wanted.OrderBy(k => visible.Contains(k) ? 0 : 1).ThenBy(k => (new Vector2(k.X * 512 + 256, k.Y * 512 + 256) - _camera.Position).LengthSquared()))
            {
                if (_pending.Count >= 2) break;
                if (_chunks.ContainsKey(key) || _pending.ContainsKey(key)) continue;
                var renderer = _renderer; var token = _cancellation.Token;
                _pending.Add(key, Task.Run(() => renderer.RenderChunk(key.X, key.Y, token), token));
            }
        long total = _chunks.Values.Sum(c => c.Bytes);
        foreach (var pair in _chunks.Where(p => !wanted.Contains(p.Key)).OrderBy(p => p.Value.LastUse).ToArray())
        {
            if (total <= CacheBudget) break;
            total -= pair.Value.Bytes; pair.Value.Texture.Dispose(); _chunks.Remove(pair.Key);
        }
        if (_loading is null) StatusText = _error ?? $"190 年初始快照 · {_season} · 缩放 {ZoomFactor:0.00}×";
    }
    private static ImageTexture Texture(PngImage pixels)
    {
        using var image = Image.CreateFromData(pixels.Width, pixels.Height, false, pixels.Channels == 4 ? Image.Format.Rgba8 : Image.Format.Rgb8, pixels.Pixels);
        return ImageTexture.CreateFromImage(image);
    }
    private void Cache(Vector2I key, PngImage image)
    {
        if (_chunks.Remove(key, out var old)) old.Texture.Dispose();
        _chunks.Add(key, new(Texture(image), (long)image.Width * image.Height * 7, _tick));
    }
    public override void _Draw()
    {
        if (_map is null) return;
        foreach (var key in VisibleChunks(_camera.Position, ZoomFactor, _map, 0))
            if (_chunks.TryGetValue(key, out var c)) DrawTexture(c.Texture, new(key.X * 512, key.Y * 512));
        _waterLayer.QueueRedraw(); _objectLayer.QueueRedraw();
    }
    private Rect2 VisibleRect()
    {
        var half = new Vector2(ClientLayout.LogicalWidth, ClientLayout.LogicalHeight) / (2 * ZoomFactor);
        return new(_camera.Position - half, half * 2);
    }
    private void DrawAnimated(Node2D layer, MapAnimatedSprite sprite, Rect2 view, double time)
    {
        var clip = _clips[sprite.ClipId];
        var texture = _objects[MapAnimationClock.Asset(clip, _season, time, sprite.Phase)];
        var position = new Vector2(sprite.X, sprite.Y);
        if (view.Intersects(new(position, texture.GetSize()))) layer.DrawTexture(texture, position);
    }
    private void DrawWater()
    {
        if (_map is null) return;
        var view = VisibleRect(); double time = AnimationTime;
        foreach (var sprite in _map.Animations.Water) DrawAnimated(_waterLayer, sprite, view, time);
    }
    public bool ShowBoundaries { get; set; } = true;
    private void DrawObjects()
    {
        if (_map is null) return;
        var view = VisibleRect(); double time = AnimationTime;
        void Draw(MapSprite sprite)
        {
            var texture = _objects[sprite.AssetId]; var position = new Vector2(sprite.X, sprite.Y);
            if (view.Intersects(new(position, texture.GetSize()))) _objectLayer.DrawTexture(texture, position);
        }
        foreach (var sprite in _map.Bridges) Draw(sprite);
        if (ShowBoundaries) foreach (var sprite in _map.Boundaries) Draw(sprite);
        foreach (var building in _map.Buildings)
        {
            foreach (var sprite in building.Sprites) Draw(sprite);
            if (_buildingAnimations.TryGetValue(building.Id, out var animations))
                foreach (var sprite in animations) DrawAnimated(_objectLayer, sprite, view, time);
        }
        foreach (var sprite in _map.Clouds) Draw(sprite);
        if (_selected is Vector2I selected)
        {
            var c = MapGeometry.Center(selected.X, selected.Y); var p = new Vector2(c.X, c.Y);
            _objectLayer.DrawPolyline([p + new Vector2(0, -16), p + new Vector2(32, 0), p + new Vector2(0, 16), p + new Vector2(-32, 0), p + new Vector2(0, -16)], new Color(1, 0.9f, 0.25f), 2 / ZoomFactor, true);
        }
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (_map is null) return;
        if (input is InputEventMouseButton button)
        {
            if (button.Position.X < 0 || button.Position.Y < 0 || button.Position.X >= ClientLayout.LogicalWidth || button.Position.Y >= ClientLayout.LogicalHeight) return;
            if (IsInputBlocked?.Invoke(button.Position) == true) { _dragging = false; return; }
            if (button.ButtonIndex == MouseButton.Right) { _dragging = button.Pressed; GetViewport().SetInputAsHandled(); }
            else if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                var before = ViewportToWorld(button.Position); SetZoom(ZoomFactor * (button.ButtonIndex == MouseButton.WheelUp ? 1.2f : 1 / 1.2f));
                _camera.ForceUpdateScroll(); _camera.Position += before - ViewportToWorld(button.Position); ClampCamera(); GetViewport().SetInputAsHandled();
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.Left)
            {
                var p = ViewportToWorld(button.Position);
                if (TryBuildingAt(p) is int id) BuildingPicked?.Invoke(id);
                else if (MapGeometry.TrySquare(p.X, p.Y, out int x, out int y)) SquarePicked?.Invoke(x, y);
                else SelectionCleared?.Invoke();
                QueueRedraw(); GetViewport().SetInputAsHandled();
            }
        }
        else if (input is InputEventMouseMotion motion && _dragging)
        {
            if (IsInputBlocked?.Invoke(motion.Position) == true) { _dragging = false; return; }
            _camera.Position -= motion.Relative / ZoomFactor; ClampCamera(); GetViewport().SetInputAsHandled();
        }
    }
    private int? TryBuildingAt(Vector2 point)
    {
        foreach (var building in _map!.Buildings.Reverse())
            foreach (var sprite in building.Sprites.Reverse())
            {
                var image = _images![sprite.AssetId]; int x = (int)Math.Floor(point.X - sprite.X), y = (int)Math.Floor(point.Y - sprite.Y);
                if (x >= 0 && y >= 0 && x < image.Width && y < image.Height &&
                    (image.Channels != 4 || image.Pixels[(y * image.Width + x) * 4 + 3] >= 128)) return building.Id;
            }
        return null;
    }
    public void PreviewSeason(string season) => BeginLoad(season);
    public void FocusWorld(Vector2 point) { _camera.Position = point; ClampCamera(); }
    public void FocusBuilding(int id)
    {
        var b = _map!.Buildings.Single(b => b.Id == id); var c = MapGeometry.Center(b.X, b.Y);
        FocusWorld(new(c.X, c.Y));
    }
    public Vector2 WorldToViewport(Vector2 world) => GetViewport().CanvasTransform * world;
    public void SetZoom(float zoomFactor)
    {
        if (!float.IsFinite(zoomFactor) || zoomFactor <= 0) throw new ArgumentOutOfRangeException(nameof(zoomFactor));
        _camera.Zoom = Vector2.One * Math.Clamp(zoomFactor, 0.25f, 2); ClampCamera();
    }
    private void ClampCamera()
    {
        if (_map is null) return;
        var half = new Vector2(ClientLayout.LogicalWidth, ClientLayout.LogicalHeight) / (2 * ZoomFactor);
        _camera.Position = new(Math.Clamp(_camera.Position.X, half.X, _map.CanvasWidth - half.X), Math.Clamp(_camera.Position.Y, half.Y, _map.CanvasHeight - half.Y));
        _camera.ForceUpdateScroll(); QueueRedraw();
    }
    public Vector2 ViewportToWorld(Vector2 viewportPosition) => GetViewport().CanvasTransform.AffineInverse() * viewportPosition;
    public override void _ExitTree()
    {
        _cancellation.Cancel(); _cancellation.Dispose();
        foreach (var c in _chunks.Values) c.Texture.Dispose(); foreach (var t in _objects.Values) t.Dispose();
    }
}
