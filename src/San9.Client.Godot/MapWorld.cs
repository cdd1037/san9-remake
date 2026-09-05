using San9.Content.Images;
using Godot;
using San9.Content.Maps;
using San9.Rendering;
using System.Diagnostics;

namespace San9.Client.Godot;

public partial class MapWorld : Node2D
{
    private Camera2D _camera = null!;
    private Node2D _waterLayer = null!, _objectLayer = null!;
    private Dictionary<string, MapAnimationClip> _clips = [];
    private Dictionary<int, MapAnimatedSprite[]> _buildingAnimations = [];
    private readonly System.Diagnostics.Stopwatch _animationWatch = new();
    private double? _fixedAnimationTime;
    private readonly Dictionary<string, int> _lastFrames = [];
    private MapAnimatedSprite[] _visibleWater = [];
    private MapBuilding[] _visibleBuildings = [];
    private MapSprite[] _visibleBridges = [], _visibleBoundaries = [], _visibleClouds = [];
    private readonly Dictionary<Vector2I, List<int>> _waterBuckets = [];
    private readonly HashSet<string> _waterClips = [], _objectClips = [];
    private readonly MapViewportTransition _view = new(new(0, 0, 1));
    private HashSet<Vector2I> _wanted = [], _visible = [], _presentedChunks = [];
    private Vector2I[] _priority = [];
    private readonly Stopwatch _navigationWatch = new();
    private bool _showBoundaries = true;
    private double AnimationTime => _fixedAnimationTime ?? _animationWatch.Elapsed.TotalMilliseconds;
    private MapPackage? _map;
    private TerrainRenderer? _renderer;
    private Dictionary<string, PngImage>? _images;
    private readonly Dictionary<string, ImageTexture> _objects = [];
    private readonly Dictionary<Vector2I, CachedChunk> _chunks = [];
    private readonly Dictionary<Vector2I, ChunkJob> _pending = [];
    private readonly SemaphoreSlim _renderSlots = new(2);
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
    public void Highlight((int X, int Y)? square) { _selected = square is {} p ? new(p.X, p.Y) : null; _objectLayer.QueueRedraw(); }
    public long CacheBytes => _chunks.Values.Sum(c => c.Bytes);
    public bool IsVisibleReady => HasCompleteView && _loading is null && !_view.IsPending;
    public bool HasCompleteView => _view.HasPresented && _presentedChunks.All(_chunks.ContainsKey);
    public long ViewRevision => _view.Revision;
    public Vector2 RequestedPosition => new((float)_view.Requested.X, (float)_view.Requested.Y);
    public float RequestedZoom => (float)_view.Requested.Zoom;
    public double LastNavigationMilliseconds { get; private set; }
    public long TerrainDraws { get; private set; }
    public long WaterDraws { get; private set; }
    public long ObjectDraws { get; private set; }
    public double LastProcessMilliseconds { get; private set; }
    public double TotalChunkRenderMilliseconds { get; private set; }
    public double TotalUploadMilliseconds { get; private set; }
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
    private sealed record ChunkResult(PngImage Pixels, double Milliseconds);
    private sealed record ChunkJob(Task<ChunkResult> Task, CancellationTokenSource Cancellation);
    private sealed record PreparedMap(MapPackage Map, Dictionary<string, PngImage> Images, TerrainRenderer Renderer, string Season,
        MapViewport View, Dictionary<Vector2I, PngImage> Chunks, double AssetsMilliseconds, double TerrainMilliseconds);
    public float ZoomFactor => _camera.Zoom.X;

    public override void _Ready()
    {
        _waterLayer = new Node2D { Name = "Water", Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        _objectLayer = new Node2D { Name = "Objects" };
        _waterLayer.Draw += DrawWater; _objectLayer.Draw += DrawObjects;
        AddChild(_waterLayer); AddChild(_objectLayer);
        _camera = GetNode<Camera2D>("Camera");
        var center = MapGeometry.Center(89, 64); _camera.Position = new(center.X, center.Y);
        _view.Request(new(center.X, center.Y, 1));
    }
    public void LoadMap(MapPackage map, string root, string? season = null, double? animationTime = null)
    {
        _map = map; _packageRoot = root; _fixedAnimationTime = animationTime;
        BeginLoad(season);
    }
    private void BeginLoad(string? season)
    {
        _cancellation.Cancel(); _cancellation.Dispose(); _cancellation = new();
        foreach (var job in _pending.Values) { job.Cancellation.Cancel(); job.Cancellation.Dispose(); }
        _pending.Clear(); var token = _cancellation.Token;
        var oldMap = _map; var oldImages = _images;
        _view.Request(ClampView(_view.Requested));
        var view = _view.HasPresented ? _view.Presented : _view.Requested; var root = _packageRoot;
        StatusText = "正在校验并准备地图…";
        _error = null; _loadWatch.Restart();
        _loading = Task.Run(() =>
        {
            var map = oldMap ?? throw new InvalidOperationException("Load a map before previewing a season.");
            var watch = Stopwatch.StartNew();
            var common = oldImages ?? ImageAssetFiles.Load(root, map.Assets, token);
            string selected = season ?? map.InitialSeason;
            var definition = map.Seasons.SingleOrDefault(s => s.Id == selected) ?? throw new InvalidDataException("Unknown season.");
            var land = ImageAssetFiles.Load(root, definition.Textures, token).ToDictionary(p => int.Parse(p.Key), p => p.Value);
            double assetsMs = watch.Elapsed.TotalMilliseconds; watch.Restart();
            var renderer = new TerrainRenderer(map, common, land);
            Dictionary<Vector2I, PngImage> prepared = [];
            foreach (var key in ViewChunks(view, map, 0)) prepared.Add(key, renderer.RenderChunk(key.X, key.Y, token));
            return new PreparedMap(map, common, renderer, selected, view, prepared, assetsMs, watch.Elapsed.TotalMilliseconds);
        }, token);
    }
    private static IEnumerable<Vector2I> ViewChunks(MapViewport view, MapPackage map, int margin) =>
        view.Chunks(map.CanvasWidth, map.CanvasHeight, ClientLayout.LogicalWidth, ClientLayout.LogicalHeight, margin).Select(p => new Vector2I(p.X, p.Y));
    public override void _Process(double delta)
    {
        long processStart = Stopwatch.GetTimestamp();
        _tick++;
        if (!Input.IsMouseButtonPressed(MouseButton.Right)) _dragging = false;
        if (_loading is { IsCompleted: true })
        {
            var completed = _loading; _loading = null;
            try
            {
                var loaded = completed.GetAwaiter().GetResult();
                var uploadWatch = Stopwatch.StartNew();
                var stagedObjects = new Dictionary<string, ImageTexture>();
                var stagedChunks = new Dictionary<Vector2I, CachedChunk>();
                try
                {
                    var sprites = loaded.Map.Boundaries.Concat(loaded.Map.Bridges).Concat(loaded.Map.Buildings.SelectMany(b => b.Sprites)).Concat(loaded.Map.Clouds);
                    var ids = sprites.Select(s => s.AssetId).Concat(loaded.Map.Animations.Clips.SelectMany(c => c.Frames.Concat(c.SeasonFrames.Values.SelectMany(f => f)))).Distinct();
                    foreach (var id in ids)
                        if (!_objects.ContainsKey(id)) stagedObjects.Add(id, Texture(loaded.Images[id]));
                    foreach (var pair in loaded.Chunks)
                        stagedChunks.Add(pair.Key, new(Texture(pair.Value), (long)pair.Value.Width * pair.Value.Height * 7, _tick));
                }
                catch
                {
                    foreach (var texture in stagedObjects.Values) texture.Dispose();
                    foreach (var chunk in stagedChunks.Values) chunk.Texture.Dispose();
                    throw;
                }
                double uploadMs = uploadWatch.Elapsed.TotalMilliseconds;
                TotalUploadMilliseconds += uploadMs;
                foreach (var c in _chunks.Values) c.Texture.Dispose(); _chunks.Clear();
                foreach (var pair in stagedChunks) _chunks.Add(pair.Key, pair.Value);
                foreach (var pair in stagedObjects) _objects.Add(pair.Key, pair.Value);
                _map = loaded.Map; _images = loaded.Images; _renderer = loaded.Renderer; _season = loaded.Season;
                _clips = _map.Animations.Clips.ToDictionary(c => c.Id);
                _buildingAnimations = _map.Animations.Buildings.GroupBy(s => s.BuildingId).ToDictionary(g => g.Key, g => g.ToArray());
                BuildWaterIndex();
                if (!_animationWatch.IsRunning) _animationWatch.Start();
                _view.PresentPrepared(loaded.View); ApplyPresentedView(); RebuildRequests();
                GD.Print($"MAP_PREPARE assets_ms={loaded.AssetsMilliseconds:F2} terrain_ms={loaded.TerrainMilliseconds:F2} upload_ms={uploadMs:F2} install_ms={uploadWatch.Elapsed.TotalMilliseconds - uploadMs:F2}");
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
        bool waterChanged = false, objectsChanged = false;
        foreach (var clip in _clips.Values)
        {
            int frame = MapAnimationClock.Frame(clip, AnimationTime);
            if (_lastFrames.TryGetValue(clip.Id, out int previous) && previous == frame) continue;
            _lastFrames[clip.Id] = frame;
            waterChanged |= _waterClips.Contains(clip.Id);
            objectsChanged |= _objectClips.Contains(clip.Id);
        }
        if (waterChanged) _waterLayer.QueueRedraw();
        if (objectsChanged) _objectLayer.QueueRedraw();
        var uploadBudget = Stopwatch.StartNew();
        foreach (var pair in _pending.OrderBy(p => _visible.Contains(p.Key) ? 0 : 1).ToArray())
        {
            if (!pair.Value.Task.IsCompleted) continue;
            _pending.Remove(pair.Key); pair.Value.Cancellation.Dispose();
            try
            {
                var result = pair.Value.Task.GetAwaiter().GetResult();
                TotalChunkRenderMilliseconds += result.Milliseconds;
                if (_wanted.Contains(pair.Key)) Cache(pair.Key, result.Pixels);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { _error = e.Message; StatusText = _error; GD.PushError(e.Message); }
            if (uploadBudget.Elapsed.TotalMilliseconds >= 2) break;
        }
        TryPresentView();
        if (_loading is null && _error is null)
            foreach (var key in _priority)
            {
                if (_chunks.ContainsKey(key) || _pending.ContainsKey(key)) continue;
                var renderer = _renderer;
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token); var token = cancellation.Token;
                var task = RenderChunkAsync(renderer, key, token);
                _pending.Add(key, new(task, cancellation));
            }
        long total = _chunks.Values.Sum(c => c.Bytes);
        if (total > CacheBudget) foreach (var pair in _chunks.Where(p => !_visible.Contains(p.Key) && !_presentedChunks.Contains(p.Key)).OrderBy(p => p.Value.LastUse).ToArray())
        {
            if (total <= CacheBudget) break;
            total -= pair.Value.Bytes; pair.Value.Texture.Dispose(); _chunks.Remove(pair.Key);
        }
        if (_loading is null) StatusText = _error ?? $"190 年初始快照 · {_season} · 缩放 {ZoomFactor:0.00}×";
        LastProcessMilliseconds = Stopwatch.GetElapsedTime(processStart).TotalMilliseconds;
    }
    private static ImageTexture Texture(PngImage pixels)
    {
        using var image = Image.CreateFromData(pixels.Width, pixels.Height, false, pixels.Channels == 4 ? Image.Format.Rgba8 : Image.Format.Rgb8, pixels.Pixels);
        return ImageTexture.CreateFromImage(image);
    }
    private void Cache(Vector2I key, PngImage image)
    {
        var watch = Stopwatch.StartNew();
        if (_chunks.Remove(key, out var old)) old.Texture.Dispose();
        _chunks.Add(key, new(Texture(image), (long)image.Width * image.Height * 7, _tick));
        TotalUploadMilliseconds += watch.Elapsed.TotalMilliseconds;
    }
    public override void _Draw()
    {
        TerrainDraws++;
        if (!HasCompleteView) return;
        foreach (var key in _presentedChunks)
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
        WaterDraws++;
        if (!HasCompleteView) return;
        var view = VisibleRect(); double time = AnimationTime;
        foreach (var sprite in _visibleWater) DrawAnimated(_waterLayer, sprite, view, time);
    }
    private async Task<ChunkResult> RenderChunkAsync(TerrainRenderer renderer, Vector2I key, CancellationToken token)
    {
        await _renderSlots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var watch = Stopwatch.StartNew();
                var pixels = renderer.RenderChunk(key.X, key.Y, token);
                return new ChunkResult(pixels, watch.Elapsed.TotalMilliseconds);
            }, token).ConfigureAwait(false);
        }
        finally { _renderSlots.Release(); }
    }
    public bool ShowBoundaries { get => _showBoundaries; set { if (_showBoundaries == value) return; _showBoundaries = value; _objectLayer.QueueRedraw(); } }
    private void DrawObjects()
    {
        ObjectDraws++;
        if (!HasCompleteView) return;
        var view = VisibleRect(); double time = AnimationTime;
        void Draw(MapSprite sprite)
        {
            var texture = _objects[sprite.AssetId]; var position = new Vector2(sprite.X, sprite.Y);
            if (view.Intersects(new(position, texture.GetSize()))) _objectLayer.DrawTexture(texture, position);
        }
        foreach (var sprite in _visibleBridges) Draw(sprite);
        if (ShowBoundaries) foreach (var sprite in _visibleBoundaries) Draw(sprite);
        foreach (var building in _visibleBuildings)
        {
            foreach (var sprite in building.Sprites) Draw(sprite);
            if (_buildingAnimations.TryGetValue(building.Id, out var animations))
                foreach (var sprite in animations) DrawAnimated(_objectLayer, sprite, view, time);
        }
        foreach (var sprite in _visibleClouds) Draw(sprite);
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
                var offset = button.Position - new Vector2(ClientLayout.LogicalWidth, ClientLayout.LogicalHeight) / 2;
                float zoom = Math.Clamp(RequestedZoom * (button.ButtonIndex == MouseButton.WheelUp ? 1.2f : 1 / 1.2f), .25f, 2);
                var center = RequestedPosition + offset / RequestedZoom - offset / zoom;
                RequestView(new(center.X, center.Y, zoom)); GetViewport().SetInputAsHandled();
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.Left)
            {
                if (!HasCompleteView) return;
                var p = ViewportToWorld(button.Position);
                if (TryBuildingAt(p) is int id) BuildingPicked?.Invoke(id);
                else if (MapGeometry.TrySquare(p.X, p.Y, out int x, out int y)) SquarePicked?.Invoke(x, y);
                else SelectionCleared?.Invoke();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (input is InputEventMouseMotion motion && _dragging)
        {
            if (IsInputBlocked?.Invoke(motion.Position) == true) { _dragging = false; return; }
            FocusWorld(RequestedPosition - motion.Relative / RequestedZoom); GetViewport().SetInputAsHandled();
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
    public void FocusWorld(Vector2 point) => RequestView(new(point.X, point.Y, RequestedZoom));
    public void FocusBuilding(int id)
    {
        var b = _map!.Buildings.Single(b => b.Id == id); var c = MapGeometry.Center(b.X, b.Y);
        FocusWorld(new(c.X, c.Y));
    }
    public Vector2 WorldToViewport(Vector2 world) => GetViewport().CanvasTransform * world;
    public void SetZoom(float zoomFactor)
    {
        if (!float.IsFinite(zoomFactor) || zoomFactor <= 0) throw new ArgumentOutOfRangeException(nameof(zoomFactor));
        RequestView(_view.Requested with { Zoom = Math.Clamp(zoomFactor, 0.25f, 2) });
    }
    private MapViewport ClampView(MapViewport view) => _map is null ? view :
        view.Clamp(_map.CanvasWidth, _map.CanvasHeight, ClientLayout.LogicalWidth, ClientLayout.LogicalHeight);
    private void RequestView(MapViewport view)
    {
        view = ClampView(view);
        if (_view.Requested == view) return;
        _view.Request(view); _navigationWatch.Restart();
        RebuildRequests(); TryPresentView();
    }
    private void RebuildRequests()
    {
        if (_map is null) return;
        _visible = ViewChunks(_view.Requested, _map, 0).ToHashSet();
        _wanted = ViewChunks(_view.Requested, _map, 1).ToHashSet();
        var position = RequestedPosition;
        _priority = _wanted.OrderBy(k => _visible.Contains(k) ? 0 : 1)
            .ThenBy(k => (new Vector2(k.X * 512 + 256, k.Y * 512 + 256) - position).LengthSquared()).ToArray();
        foreach (var pair in _pending)
            if (!_visible.Contains(pair.Key) && !pair.Value.Task.IsCompleted) pair.Value.Cancellation.Cancel();
        foreach (var key in _wanted)
            if (_chunks.TryGetValue(key, out var chunk)) _chunks[key] = chunk with { LastUse = _tick };
    }
    private void TryPresentView()
    {
        if (_renderer is null || _loading is not null) return;
        if (!_view.TryPresent(_ => _visible.All(_chunks.ContainsKey))) return;
        ApplyPresentedView();
        LastNavigationMilliseconds = _navigationWatch.Elapsed.TotalMilliseconds;
        _navigationWatch.Stop();
    }
    private Rect2 SpriteBounds(MapSprite sprite) => new(new Vector2(sprite.X, sprite.Y), _objects[sprite.AssetId].GetSize());
    private Rect2 AnimationBounds(MapAnimatedSprite sprite)
    {
        var clip = _clips[sprite.ClipId];
        var size = Vector2.Zero;
        foreach (var frame in clip.SeasonFrames.GetValueOrDefault(_season, clip.Frames)) size = size.Max(_objects[frame].GetSize());
        return new(new Vector2(sprite.X, sprite.Y), size);
    }
    private void BuildWaterIndex()
    {
        _waterBuckets.Clear();
        for (int i = 0; i < _map!.Animations.Water.Length; i++)
        {
            var rect = AnimationBounds(_map.Animations.Water[i]);
            for (int y = (int)Math.Floor(rect.Position.Y / 512); y < Math.Ceiling(rect.End.Y / 512); y++)
                for (int x = (int)Math.Floor(rect.Position.X / 512); x < Math.Ceiling(rect.End.X / 512); x++)
                {
                    var key = new Vector2I(x, y);
                    if (!_waterBuckets.TryGetValue(key, out var bucket)) _waterBuckets.Add(key, bucket = []);
                    bucket.Add(i);
                }
        }
    }
    private void ApplyPresentedView()
    {
        var view = _view.Presented;
        _camera.Position = new((float)view.X, (float)view.Y); _camera.Zoom = Vector2.One * (float)view.Zoom;
        _camera.ForceUpdateScroll();
        _presentedChunks = ViewChunks(view, _map!, 0).ToHashSet();
        var rect = VisibleRect();
        _visibleWater = _presentedChunks.Where(_waterBuckets.ContainsKey).SelectMany(k => _waterBuckets[k]).Distinct().Order()
            .Select(i => _map!.Animations.Water[i]).Where(s => rect.Intersects(AnimationBounds(s))).ToArray();
        _visibleBridges = _map!.Bridges.Where(s => rect.Intersects(SpriteBounds(s))).ToArray();
        _visibleBoundaries = _map.Boundaries.Where(s => rect.Intersects(SpriteBounds(s))).ToArray();
        _visibleClouds = _map.Clouds.Where(s => rect.Intersects(SpriteBounds(s))).ToArray();
        _visibleBuildings = _map.Buildings.Where(b => b.Sprites.Any(s => rect.Intersects(SpriteBounds(s))) ||
            (_buildingAnimations.TryGetValue(b.Id, out var sprites) && sprites.Any(s => rect.Intersects(AnimationBounds(s))))).ToArray();
        _waterClips.Clear(); _objectClips.Clear();
        foreach (var sprite in _visibleWater) _waterClips.Add(sprite.ClipId);
        foreach (var building in _visibleBuildings)
            if (_buildingAnimations.TryGetValue(building.Id, out var sprites)) foreach (var sprite in sprites) _objectClips.Add(sprite.ClipId);
        QueueRedraw(); _waterLayer.QueueRedraw(); _objectLayer.QueueRedraw();
    }
    public Vector2 ViewportToWorld(Vector2 viewportPosition) => GetViewport().CanvasTransform.AffineInverse() * viewportPosition;
    public override void _ExitTree()
    {
        _cancellation.Cancel(); _cancellation.Dispose();
        foreach (var job in _pending.Values) { job.Cancellation.Cancel(); job.Cancellation.Dispose(); }
        foreach (var c in _chunks.Values) c.Texture.Dispose(); foreach (var t in _objects.Values) t.Dispose();
    }
}
