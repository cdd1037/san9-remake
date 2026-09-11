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
    private MapBuilding[] _runtimeBuildings = [];
    public San9.Application.GameSession Session { get; set; } = null!;
    public long StateRevision { get; private set; } = -1;
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
    public TextureService Textures { get; set; } = null!;
    private TextureScope _objects = null!;
    private TextureScope? _requestedObjects, _prefetchedObjects;
    private PreparedMap? _prepared;
    private long _preparedRevision;
    private TextureScope? _preparedObjects;
    private ResourceBudget<Vector2I, ImageTexture>? _preparedChunks;
    private readonly Dictionary<Vector2I, ChunkJob> _preparedJobs = [];
    private readonly List<IDisposable> _chunkPins = [];
    private readonly List<Sprite2D> _objectSprites = [];
    private int _objectCursor;
    private bool _objectsDirty;
    private Node2D _selectionLayer = null!;
    private ShaderMaterial _hoverMaterial = null!;
    private Dictionary<int, MapAnimatedSprite[]> _presentedAnimations = [];
    private ResourceBudget<Vector2I, ImageTexture> _chunks = null!;
    private readonly Dictionary<Vector2I, ChunkJob> _pending = [];
    private readonly SemaphoreSlim _renderSlots = new(2);
    private CancellationTokenSource _cancellation = new();
    private Task<PreparedMap>? _loading;
    private string _packageRoot = "", _season = "";
    private bool _dragging;
    private string? _error;
    private readonly System.Diagnostics.Stopwatch _loadWatch = new();
    private Vector2I? _selected;
    public int? HoveredBuildingId { get; private set; }
    public void HoverBuilding(int? id)
    {
        if (HoveredBuildingId == id) return;
        HoveredBuildingId = id; QueueObjects();
    }
    public int? BuildingAtViewport(Vector2 point) => HasCompleteView && !IsLoading ? TryBuildingAt(ViewportToWorld(point)) : null;
    public void DrawHoverFlag(Node2D target, int color, int glyph, Vector2 position)
    {
        // The card uses direction 5, frame 2, independently of the map's season and clock.
        void Draw(string id) => target.DrawTexture(_objects[_clips[id].SeasonFrames["autumn"][2]], position);
        Draw("flag-pole-back"); Draw($"flag-cloth-{color}");
        if (glyph >= 0)
        {
            int ink = new[] { 3,6,7,8,11,16,18,21,22,23,24,26,27,29,31,33,38,39,40,46,49,50 }.Contains(color) ? 1 : 0;
            Draw($"flag-glyph-{ink}-{glyph}");
        }
        Draw("flag-pole-front");
    }
    public event Action<int>? BuildingPicked;
    public event Action<int, int>? SquarePicked;
    public event Action? SelectionCleared;
    public void Highlight((int X, int Y)? square) { _selected = square is {} p ? new(p.X, p.Y) : null; QueueObjects(); }
    public long CacheBytes => _chunks?.Bytes ?? 0;
    public long CpuSourceBytes => _renderer?.SourceBytes ?? 0;
    public bool IsVisibleReady => HasCompleteView && !IsLoading && !_view.IsPending && _requestedObjects is null;
    public bool HasCompleteView => _view.HasPresented && _presentedChunks.All(_chunks.Contains);
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
    public bool IsLoading => _loading is not null || _prepared is not null;
    public string? LoadError => _error;
    public event Action? MapReady;
    public Func<Vector2, bool>? IsInputBlocked { get; set; }
    public Vector2 CameraPosition => _camera.Position;
    private const long CacheBudget = 256L * 1024 * 1024;
    private sealed record ChunkResult(PngImage Pixels, double Milliseconds);
    private sealed record ChunkJob(Task<ChunkResult> Task, CancellationTokenSource Cancellation);
    private sealed record PreparedMap(MapPackage Map, Dictionary<string, PngImage> Images, TerrainRenderer Renderer, string Season,
        MapViewport View, double AssetsMilliseconds, double TerrainMilliseconds);
    public float ZoomFactor => _camera.Zoom.X;

    public override void _Ready()
    {
        _chunks = NewTerrainCache();
        _hoverMaterial = new ShaderMaterial { Shader = new Shader { Code = "shader_type canvas_item; void fragment() { vec4 c = texture(TEXTURE, UV); COLOR = vec4(round(c.rgb * 255.0 * 0.6875 + vec3(240.0 * 0.3125)) / 255.0, round(c.a * 255.0 * 0.9375) / 255.0); }" } };
        _waterLayer = new Node2D { Name = "Water", Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        _objectLayer = new Node2D { Name = "Objects" };
        _waterLayer.Draw += DrawWater;
        AddChild(_waterLayer); AddChild(_objectLayer);
        _selectionLayer = new Node2D(); _selectionLayer.Draw += DrawSelection; _objectLayer.AddChild(_selectionLayer);
        _camera = GetNode<Camera2D>("Camera");
        var center = MapGeometry.Center(89, 64); _camera.Position = new(center.X, center.Y);
        _view.Request(new(center.X, center.Y, 1));
    }
    public void LoadMap(MapPackage map, string root, string? season = null, double? animationTime = null)
    {
        _map = map; _packageRoot = root; _fixedAnimationTime = animationTime;
        _clips = map.Animations.Clips.ToDictionary(c => c.Id); RefreshRuntimeBuildings();
        BeginLoad(season);
    }
    private void BeginLoad(string? season)
    {
        _cancellation.Cancel(); _cancellation.Dispose(); _cancellation = new();
        CancelJobs(_pending); CancelJobs(_preparedJobs);
        _prepared = null; _preparedObjects?.Dispose(); _preparedObjects = null; _preparedChunks?.Dispose(); _preparedChunks = null;
        _requestedObjects?.Dispose(); _requestedObjects = null; _prefetchedObjects?.Dispose(); _prefetchedObjects = null;
        var token = _cancellation.Token;
        var oldMap = _map; var oldImages = _images;
        _view.Request(ClampView(_view.Requested));
        var view = _view.HasPresented ? _view.Presented : _view.Requested; var root = _packageRoot;
        StatusText = "正在校验并准备地图…";
        _error = null; _loadWatch.Restart();
        _loading = Task.Run(() =>
        {
            var map = oldMap ?? throw new InvalidOperationException("Load a map before previewing a season.");
            var watch = Stopwatch.StartNew();
            var common = oldImages ?? ImageAssetFiles.Load(root, map.Assets.Where(a => map.Masks.Any(m => m.WeightAsset == a.Id || m.CoverageAsset == a.Id) || a.Id == "cloud-fill"), token);
            string selected = season ?? map.InitialSeason;
            var definition = map.Seasons.SingleOrDefault(s => s.Id == selected) ?? throw new InvalidDataException("Unknown season.");
            var land = ImageAssetFiles.Load(root, definition.Textures, token).ToDictionary(p => int.Parse(p.Key), p => p.Value);
            double assetsMs = watch.Elapsed.TotalMilliseconds; watch.Restart();
            var renderer = new TerrainRenderer(map, common, land);
            return new PreparedMap(map, common, renderer, selected, view, assetsMs, watch.Elapsed.TotalMilliseconds);
        }, token);
    }
    private static IEnumerable<Vector2I> ViewChunks(MapViewport view, MapPackage map, int margin) =>
        view.Chunks(map.CanvasWidth, map.CanvasHeight, ClientLayout.LogicalWidth, ClientLayout.LogicalHeight, margin).Select(p => new Vector2I(p.X, p.Y));
    public override void _Process(double delta)
    {
        long processStart = Stopwatch.GetTimestamp();
        if (!Input.IsMouseButtonPressed(MouseButton.Right)) _dragging = false;
        if (_loading is { IsCompleted: true })
        {
            var completed = _loading; _loading = null;
            try
            {
                _prepared = completed.GetAwaiter().GetResult();
                _preparedObjects = RequestObjects(_prepared.View, _prepared.Season); _preparedRevision = StateRevision;
                _preparedChunks = NewTerrainCache();
            }
            catch (Exception error) { _error = error.Message; GD.PushError(error.Message); }
        }
        if (_prepared is not null) PumpPrepared();
        if (_map is null || _renderer is null) return;
        if (StateRevision != Session.Snapshot.Revision) RefreshRuntimeBuildings();
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
        if (objectsChanged) QueueObjects();
        var uploadBudget = Stopwatch.StartNew();
        foreach (var pair in _pending.OrderBy(p => _visible.Contains(p.Key) ? 0 : 1).ToArray())
        {
            if (!Textures.CanUpload) break;
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
        if (_prefetchedObjects is { IsReady: true } || _prefetchedObjects?.Error is not null)
        { _prefetchedObjects.Dispose(); _prefetchedObjects = null; }
        if (!IsLoading && _error is null)
            foreach (var key in _priority)
            {
                if (_chunks.Contains(key) || _pending.ContainsKey(key)) continue;
                if (_pending.Count >= 2) break;
                var renderer = _renderer;
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token); var token = cancellation.Token;
                var task = RenderChunkAsync(renderer, key, token);
                _pending.Add(key, new(task, cancellation));
            }
        foreach (var pin in _chunkPins) pin.Dispose(); _chunkPins.Clear();
        foreach (var key in _visible.Concat(_presentedChunks).Distinct().Where(_chunks.Contains)) _chunkPins.Add(_chunks.Pin(key));
        _chunks.Trim();
        if (!IsLoading) StatusText = _error ?? $"190 年初始快照 · {_season} · 缩放 {ZoomFactor:0.00}×";
        if (_objectsDirty) { _objectsDirty = false; RefreshObjectSprites(); }
        LastProcessMilliseconds = Stopwatch.GetElapsedTime(processStart).TotalMilliseconds;
    }
    private void Cache(Vector2I key, PngImage image)
    {
        var watch = Stopwatch.StartNew();
        if (_chunks.Contains(key)) return;
        _chunks.Add(key, Textures.UploadGenerated(image), (long)image.Width * image.Height * image.Channels);
        TotalUploadMilliseconds += watch.Elapsed.TotalMilliseconds;
    }
    public override void _Draw()
    {
        TerrainDraws++;
        if (!HasCompleteView) return;
        var fill = _objects["cloud-fill"];
        var rect = VisibleRect();
        for (float y = 112 + MathF.Floor((rect.Position.Y - 112) / fill.GetHeight()) * fill.GetHeight(); y < rect.End.Y; y += fill.GetHeight())
        for (float x = 96 + MathF.Floor((rect.Position.X - 96) / fill.GetWidth()) * fill.GetWidth(); x < rect.End.X; x += fill.GetWidth())
            DrawTexture(fill, new(x, y));
        foreach (var key in _presentedChunks)
            if (_chunks.Contains(key)) DrawTexture(_chunks.Get(key), new(key.X * 512, key.Y * 512));
        _waterLayer.QueueRedraw();
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
        if (view.Intersects(new(position, texture.GetSize())))
        { if (layer == _objectLayer) EmitObject(texture, position, false); else layer.DrawTexture(texture, position); }
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
    public bool ShowBoundaries { get => _showBoundaries; set { if (_showBoundaries == value) return; _showBoundaries = value; QueueObjects(); } }
    private void QueueObjects() => _objectsDirty = true;
    private void RefreshObjectSprites()
    {
        ObjectDraws++;
        if (!HasCompleteView) return;
        _objectCursor = 0;
        var view = VisibleRect(); double time = AnimationTime;
        void Draw(MapSprite sprite, bool hovered = false)
        {
            var texture = _objects[sprite.AssetId]; var position = new Vector2(sprite.X, sprite.Y);
            if (view.Intersects(new(position, texture.GetSize()))) EmitObject(texture, position, hovered);
        }
        foreach (var sprite in _visibleBridges) Draw(sprite);
        if (ShowBoundaries) foreach (var sprite in _visibleBoundaries) Draw(sprite);
        foreach (var building in _visibleBuildings)
        {
            foreach (var sprite in building.Sprites) Draw(sprite, building.Id == HoveredBuildingId);
            if (_presentedAnimations.TryGetValue(building.Id, out var animations))
                foreach (var sprite in animations) DrawAnimated(_objectLayer, sprite, view, time);
        }
        foreach (var sprite in _visibleClouds) Draw(sprite);
        for (int i = _objectCursor; i < _objectSprites.Count; i++) { _objectSprites[i].Texture = null; _objectSprites[i].Visible = false; }
        _objectLayer.MoveChild(_selectionLayer, _objectLayer.GetChildCount() - 1); _selectionLayer.QueueRedraw();
    }
    private void EmitObject(Texture2D texture, Vector2 position, bool hovered)
    {
        if (_objectCursor == _objectSprites.Count)
        { var sprite = new Sprite2D { Centered = false }; _objectLayer.AddChild(sprite); _objectSprites.Add(sprite); }
        var item = _objectSprites[_objectCursor++]; item.Visible = true; item.Texture = texture; item.Position = position; item.Material = hovered ? _hoverMaterial : null;
    }
    private void DrawSelection()
    {
        if (_selected is Vector2I selected)
        {
            var c = MapGeometry.Center(selected.X, selected.Y); var p = new Vector2(c.X, c.Y);
            _selectionLayer.DrawPolyline([p + new Vector2(0, -16), p + new Vector2(32, 0), p + new Vector2(0, 16), p + new Vector2(-32, 0), p + new Vector2(0, -16)], new Color(1, 0.9f, 0.25f), 2 / ZoomFactor, true);
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
        foreach (var building in _visibleBuildings.Reverse())
            foreach (var sprite in building.Sprites.Reverse())
            {
                var image = Textures.MapAssets[sprite.AssetId]; int x = (int)Math.Floor(point.X - sprite.X), y = (int)Math.Floor(point.Y - sprite.Y);
                if (Textures.Hit(image, x, y)) return building.Id;
            }
        return null;
    }
    public void RefreshRuntimeBuildings()
    {
        if (_map is null || Session is null) return;
        var view = Session.View; var forces = view.Forces.ToDictionary(f => f.Id);
        var visuals = BuildingVisuals.Resolve(_map, view.Settlements.Select(s =>
        {
            var force = s.OwnerId is int owner ? forces[owner] : null;
            return new BuildingVisualState(s.Id,s.Durability,s.Appearance,Math.Max(s.Commerce,s.Cultivation),s.Soldiers,force?.ColorCode??50,force?.FlagGlyph??-1);
        }).ToArray());
        _runtimeBuildings = visuals.Buildings;
        _buildingAnimations = visuals.Animations.GroupBy(s => s.BuildingId).ToDictionary(g => g.Key,g => g.ToArray());
        StateRevision = view.World.Revision;
        if (_images is not null && _view.HasPresented && !IsLoading) RebuildRequests();
        QueueObjects();
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
        view.Clamp(_map.Squares);
    private void RequestView(MapViewport view)
    {
        view = ClampView(view);
        if (_view.Requested == view && _error is null) return;
        _error = null;
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
        if (!IsLoading && _renderer is not null)
        {
            _requestedObjects?.Dispose(); _requestedObjects = RequestObjects(_view.Requested, _season);
            _prefetchedObjects?.Dispose(); _prefetchedObjects = RequestObjects(_view.Requested, _season, 512, true);
        }
    }
    private void TryPresentView()
    {
        if (_renderer is null || IsLoading || _requestedObjects is null) return;
        if (_requestedObjects.Error is { } error)
        {
            _error = error.Message; _requestedObjects.Dispose(); _requestedObjects = null;
            _prefetchedObjects?.Dispose(); _prefetchedObjects = null; return;
        }
        if (!_requestedObjects.IsReady || !_visible.All(_chunks.Contains)) return;
        _objects?.Dispose(); _objects = _requestedObjects; _requestedObjects = null;
        _view.PresentPrepared(_view.Requested); ApplyPresentedView();
        LastNavigationMilliseconds = _navigationWatch.Elapsed.TotalMilliseconds;
        _navigationWatch.Stop();
    }
    private Vector2 AssetSize(string id) { var a = Textures.MapAssets[id]; return new(a.Width, a.Height); }
    private Rect2 SpriteBounds(MapSprite sprite) => new(new Vector2(sprite.X, sprite.Y), AssetSize(sprite.AssetId));
    private Rect2 AnimationBounds(MapAnimatedSprite sprite, string? season = null)
    {
        var clip = _clips[sprite.ClipId];
        var size = Vector2.Zero;
        foreach (var frame in clip.SeasonFrames.GetValueOrDefault(season ?? _season, clip.Frames)) size = size.Max(AssetSize(frame));
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
        _presentedAnimations = _buildingAnimations;
        _visibleWater = _presentedChunks.Where(_waterBuckets.ContainsKey).SelectMany(k => _waterBuckets[k]).Distinct().Order()
            .Select(i => _map!.Animations.Water[i]).Where(s => rect.Intersects(AnimationBounds(s))).ToArray();
        _visibleBridges = _map!.Bridges.Where(s => rect.Intersects(SpriteBounds(s))).ToArray();
        _visibleBoundaries = _map.Boundaries.Where(s => rect.Intersects(SpriteBounds(s))).ToArray();
        _visibleClouds = _map.Clouds.Where(s => rect.Intersects(SpriteBounds(s))).ToArray();
        _visibleBuildings = _runtimeBuildings.Where(b => b.Sprites.Any(s => rect.Intersects(SpriteBounds(s))) ||
            (_buildingAnimations.TryGetValue(b.Id, out var sprites) && sprites.Any(s => rect.Intersects(AnimationBounds(s))))).ToArray();
        _waterClips.Clear(); _objectClips.Clear();
        foreach (var sprite in _visibleWater) _waterClips.Add(sprite.ClipId);
        foreach (var building in _visibleBuildings)
            if (_buildingAnimations.TryGetValue(building.Id, out var sprites)) foreach (var sprite in sprites) _objectClips.Add(sprite.ClipId);
        QueueRedraw(); _waterLayer.QueueRedraw(); QueueObjects();
    }
    public Vector2 ViewportToWorld(Vector2 viewportPosition) => GetViewport().CanvasTransform.AffineInverse() * viewportPosition;
    private ResourceBudget<Vector2I, ImageTexture> NewTerrainCache() => new(CacheBudget, texture => Textures.Defer(texture.Dispose));
    private static void CancelJobs(Dictionary<Vector2I, ChunkJob> jobs)
    {
        foreach (var job in jobs.Values)
        {
            job.Cancellation.Cancel();
            _ = job.Task.ContinueWith(task => { _ = task.Exception; job.Cancellation.Dispose(); }, TaskScheduler.Default);
        }
        jobs.Clear();
    }
    private TextureScope RequestObjects(MapViewport view, string season, float margin = 0, bool prefetch = false)
    {
        var half = new Vector2(ClientLayout.LogicalWidth, ClientLayout.LogicalHeight) / (2 * (float)view.Zoom);
        var rect = new Rect2(new Vector2((float)view.X, (float)view.Y) - half, half * 2).Grow(margin);
        HashSet<string> ids = ["cloud-fill"];
        void Animation(MapAnimatedSprite sprite)
        {
            var clip = _clips[sprite.ClipId];
            foreach (var id in clip.SeasonFrames.GetValueOrDefault(season, clip.Frames)) ids.Add(id);
            // Hover cards use autumn frame 2 regardless of the map's current season.
            if (clip.Id.StartsWith("flag-", StringComparison.Ordinal) && clip.SeasonFrames.TryGetValue("autumn", out var frames) && frames.Length > 2) ids.Add(frames[2]);
        }
        foreach (var sprite in _map!.Bridges.Concat(_map.Boundaries).Concat(_map.Clouds).Where(s => rect.Intersects(SpriteBounds(s)))) ids.Add(sprite.AssetId);
        foreach (var water in _map.Animations.Water.Where(s => rect.Intersects(AnimationBounds(s, season)))) Animation(water);
        foreach (var building in _runtimeBuildings)
        {
            var animations = _buildingAnimations.GetValueOrDefault(building.Id, []);
            if (!building.Sprites.Any(s => rect.Intersects(SpriteBounds(s))) && !animations.Any(s => rect.Intersects(AnimationBounds(s, season)))) continue;
            foreach (var sprite in building.Sprites) ids.Add(sprite.AssetId);
            foreach (var sprite in animations) Animation(sprite);
        }
        var scope = Textures.CreateScope(Textures.MapAssets, prefetch, this); scope.Request(ids); return scope;
    }
    private void PumpPrepared()
    {
        var loaded = _prepared!;
        try
        {
            if (_preparedRevision != StateRevision)
            { _preparedObjects?.Dispose(); _preparedObjects = RequestObjects(loaded.View, loaded.Season); _preparedRevision = StateRevision; }
            if (_preparedObjects!.Error is { } error) throw error;
            var budget = Stopwatch.StartNew();
            foreach (var pair in _preparedJobs.Where(p => p.Value.Task.IsCompleted).ToArray())
            {
                if (!Textures.CanUpload) break;
                _preparedJobs.Remove(pair.Key);
                try
                {
                    var result = pair.Value.Task.GetAwaiter().GetResult(); var pixels = result.Pixels;
                    var watch = Stopwatch.StartNew();
                    _preparedChunks!.Add(pair.Key, Textures.UploadGenerated(pixels), (long)pixels.Width * pixels.Height * pixels.Channels);
                    TotalUploadMilliseconds += watch.Elapsed.TotalMilliseconds; TotalChunkRenderMilliseconds += result.Milliseconds;
                }
                finally { pair.Value.Cancellation.Dispose(); }
                if (budget.Elapsed.TotalMilliseconds >= 2) break;
            }
            var keys = ViewChunks(loaded.View, loaded.Map, 0).ToArray();
            if (keys.All(_preparedChunks!.Contains) && _preparedObjects.IsReady)
            {
                foreach (var pin in _chunkPins) pin.Dispose(); _chunkPins.Clear();
                _chunks.Dispose(); _chunks = _preparedChunks; _preparedChunks = null;
                _objects?.Dispose(); _objects = _preparedObjects; _preparedObjects = null;
                _images = loaded.Images; _renderer = loaded.Renderer; _season = loaded.Season;
                _prepared = null; BuildWaterIndex();
                if (!_animationWatch.IsRunning) _animationWatch.Start();
                _view.PresentPrepared(loaded.View); ApplyPresentedView(); RebuildRequests();
                GD.Print($"MAP_PREPARE assets_ms={loaded.AssetsMilliseconds:F2} terrain_ms={loaded.TerrainMilliseconds:F2} cpu_sources_mib={CpuSourceBytes / 1048576.0:F1}");
                MapReady?.Invoke(); QueueRedraw();
                GD.Print($"MAP_READY season={_season} seconds={_loadWatch.Elapsed.TotalSeconds:F3}");
                return;
            }
            foreach (var key in keys)
            {
                if (_preparedChunks.Contains(key) || _preparedJobs.ContainsKey(key)) continue;
                if (_preparedJobs.Count >= 2) break;
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
                _preparedJobs.Add(key, new(RenderChunkAsync(loaded.Renderer, key, cancellation.Token), cancellation));
            }
        }
        catch (Exception error)
        {
            _error = error.Message; StatusText = _error; GD.PushError(_error);
            CancelJobs(_preparedJobs); _preparedObjects?.Dispose(); _preparedObjects = null;
            _preparedChunks?.Dispose(); _preparedChunks = null; _prepared = null;
        }
    }
    public override void _ExitTree()
    {
        _cancellation.Cancel(); _cancellation.Dispose(); CancelJobs(_pending); CancelJobs(_preparedJobs);
        foreach (var sprite in _objectSprites) sprite.Texture = null;
        _objects?.Dispose(); _requestedObjects?.Dispose(); _prefetchedObjects?.Dispose(); _preparedObjects?.Dispose();
        foreach (var pin in _chunkPins) pin.Dispose(); _chunkPins.Clear();
        _chunks.Dispose(); _preparedChunks?.Dispose();
        _hoverMaterial.Shader.Dispose(); _hoverMaterial.Dispose();
    }
}
