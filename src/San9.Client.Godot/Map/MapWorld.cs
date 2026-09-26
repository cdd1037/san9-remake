using Godot;
using San9.Content.Images;
using San9.Content.Maps;
using San9.Rendering;
using System.Diagnostics;

namespace San9.Client.Godot;

// Main-map input and camera controller. MapSceneView owns all visual rendering.
public partial class MapWorld : Node2D
{
    private Camera2D _camera = null!;
    private MapPackage? _map;
    private MapViewport _requested = new(0, 0, 1);
    private MapSceneView? _display;
    private TerrainChunkCache<ImageTexture>? _terrain;
    private TextureScope? _flags;
    private Dictionary<string, PngImage>? _common;
    private CancellationTokenSource _cancellation = new();
    private Task<LoadedTerrain>? _loading;
    private string _packageRoot = "";
    private string? _error;
    private bool _dragging, _showBoundaries = true, _awaitingSeason;
    private readonly Stopwatch _animation = Stopwatch.StartNew(), _navigation = new(), _loadWatch = new();
    private double? _fixedAnimationTime;
    private TerrainRenderer? _announcedTerrain;
    private sealed record LoadedTerrain(Dictionary<string, PngImage> Common, TerrainRenderer Renderer, string Season);
    internal MapScene? Scene { get; private set; }
    internal double AnimationTime => _fixedAnimationTime ?? _animation.Elapsed.TotalMilliseconds;
    internal TerrainChunkCache<ImageTexture> Terrain => _terrain!;
    public MapSceneView? Display => _display;
    public San9.Application.GameSession Session { get; set; } = null!;
    public San9.Application.Presentation.StrategySnapshot? Presentation { get; set; }
    public San9.Application.Presentation.StrategySnapshot View => Presentation ?? Session.View;
    public TextureService Textures { get; set; } = null!;
    public long StateRevision => Scene?.State.World.Revision ?? -1;
    public MapPackage? Package => _map;
    public string Season => _display?.Presented?.Region.Scene.Season ?? "";
    public long CacheBytes => _terrain?.Bytes ?? 0;
    public long CpuSourceBytes => Scene?.Terrain.SourceBytes ?? 0;
    public bool HasCompleteView => _display?.Presented is not null;
    public bool IsVisibleReady => HasCompleteView && !IsLoading && _display!.IsReady && _display.Presented!.Camera == _requested;
    public bool IsLoading => _loading is not null || _awaitingSeason;
    public string? LoadError => _error ?? _display?.Error;
    public long ViewRevision => _display?.Revision ?? 0;
    public Vector2 RequestedPosition => new((float)_requested.X, (float)_requested.Y);
    public float RequestedZoom => (float)_requested.Zoom;
    public Vector2 CameraPosition => _camera.Position;
    public float ZoomFactor => _camera.Zoom.X;
    public long TerrainDraws => _display?.TerrainDraws ?? 0;
    public long WaterDraws => _display?.WaterDraws ?? 0;
    public long ObjectDraws => _display?.ObjectDraws ?? 0;
    public double TotalChunkRenderMilliseconds => _terrain?.RenderMilliseconds ?? 0;
    public double TotalUploadMilliseconds => _terrain?.UploadMilliseconds ?? 0;
    public double LastNavigationMilliseconds { get; private set; }
    public double LastProcessMilliseconds { get; private set; }
    public string StatusText => LoadError ?? (IsLoading ? "正在准备地图…" : $"{Season} · 缩放 {ZoomFactor:0.00}×");
    public IReadOnlyList<MapBuilding> RuntimeBuildings => Scene?.Buildings ?? [];
    public MapObjectTarget? HoveredObject => _display?.Hovered;
    public int? HoveredBuildingId => HoveredObject is { IsUnit: false } target ? target.Id : null;
    public Func<Vector2, bool>? IsInputBlocked { get; set; }
    public Func<bool>? CanIssueOrders { get; set; }
    public event Action<int, Vector2>? BuildingContextRequested;
    public event Action<int, Vector2>? UnitContextRequested;
    public event Action<MapObjectTarget, Vector2>? CommandMenuRequested;
    public event Action? MapReady;
    public bool ShowBoundaries
    {
        get => _showBoundaries;
        set { if (_showBoundaries == value) return; _showBoundaries = value; RequestDisplay(); }
    }
    public override void _Ready()
    {
        _camera = GetNode<Camera2D>("Camera");
        var center = MapGeometry.Center(89, 64);
        _requested = new(center.X, center.Y, 1); _camera.Position = new(center.X, center.Y);
    }
    public void LoadMap(MapPackage map, string root, string? season = null, double? animationTime = null)
    {
        _map = map; _packageRoot = root; _fixedAnimationTime = animationTime;
        _requested = _requested.Clamp(map.Squares);
        _terrain ??= new(Textures.UploadGenerated, texture => Textures.Defer(texture.Dispose));
        if (_display is null)
        {
            _display = new() { Textures = Textures, Terrain = _terrain, AnimationTime = () => AnimationTime };
            _display.Committed += OnCommitted; AddChild(_display);
        }
        _flags ??= Textures.CreateScope(Textures.MapAssets, owner: this);
        BeginLoad(season ?? map.InitialSeason);
    }
    private void BeginLoad(string season)
    {
        _cancellation.Cancel(); _cancellation.Dispose(); _cancellation = new();
        if (_loading is { } abandoned) _ = abandoned.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        var token = _cancellation.Token; var map = _map!; var common = _common; string root = _packageRoot;
        _error = null; _awaitingSeason = true; _loadWatch.Restart();
        _loading = Task.Run(() =>
        {
            var shared = common ?? ImageAssetFiles.Load(root, map.Assets.Where(a => map.Masks.Any(m => m.WeightAsset == a.Id || m.CoverageAsset == a.Id) || a.Id == "cloud-fill"), token);
            var definition = map.Seasons.SingleOrDefault(s => s.Id == season) ?? throw new InvalidDataException("Unknown season.");
            var land = ImageAssetFiles.Load(root, definition.Textures, token).ToDictionary(p => int.Parse(p.Key), p => p.Value);
            return new LoadedTerrain(shared, new(map, shared, land), season);
        }, token);
    }
    public override void _Process(double delta)
    {
        long start = Stopwatch.GetTimestamp();
        if (!Input.IsMouseButtonPressed(MouseButton.Middle)) _dragging = false;
        if (_loading is { IsCompleted: true } completed)
        {
            _loading = null;
            try
            {
                var loaded = completed.GetAwaiter().GetResult(); _common = loaded.Common;
                Scene = new(_map!, loaded.Renderer, loaded.Season, View, Textures);
                RequestDisplay();
            }
            catch (Exception error) { _error = error.Message; _awaitingSeason = false; GD.PushError(_error); }
        }
        if (Scene is not null && !ReferenceEquals(Scene.State, View)) RefreshRuntimeObjects();
        _terrain?.Pump(() => Textures.CanUpload);
        if (_display?.Error is not null) _awaitingSeason = false;
        LastProcessMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    private void OnCommitted()
    {
        var frame = _display!.Presented!;
        _camera.Position = new((float)frame.Camera.X, (float)frame.Camera.Y);
        _camera.Zoom = Vector2.One * (float)frame.Camera.Zoom; _camera.ForceUpdateScroll();
        LastNavigationMilliseconds = _navigation.Elapsed.TotalMilliseconds; _navigation.Stop();
        if (!ReferenceEquals(_announcedTerrain, frame.Region.Scene.Terrain))
        {
            _announcedTerrain = frame.Region.Scene.Terrain; _awaitingSeason = false;
            GD.Print($"MAP_READY season={Season} seconds={_loadWatch.Elapsed.TotalSeconds:F3}"); MapReady?.Invoke();
        }
    }
    private void RequestDisplay()
    {
        if (Scene is not null) _display!.Request(Scene, _requested, new(ClientLayout.LogicalWidth, ClientLayout.LogicalHeight), ShowBoundaries, true);
    }
    public void RefreshRuntimeObjects()
    {
        if (Scene is null) return;
        Scene = new(Scene.Map, Scene.Terrain, Scene.Season, View, Textures, Scene); RequestDisplay();
    }
    public void PreviewSeason(string season) => BeginLoad(season);
    public void HoverBuilding(int? id) => HoverObject(id is int value ? new(value, false) : null);
    public void HoverObject(MapObjectTarget? target)
    {
        if (_display is not null) _display.Hovered = target;
    }
    public int? BuildingAtViewport(Vector2 point) => ObjectAtViewport(point) is { IsUnit: false } target ? target.Id : null;
    public MapObjectTarget? ObjectAtViewport(Vector2 point) => HasCompleteView && !IsLoading ? ObjectAt(ViewportToWorld(point)) : null;
    private MapObjectTarget? ObjectAt(Vector2 point) => _display?.ObjectAt(point);
    public void DrawHoverFlag(Node2D target, int color, int glyph, Vector2 position)
    {
        if (Scene is null || _flags is null) return;
        void Draw(string id) => target.DrawTexture(_flags[Scene.Clips[id].SeasonFrames["autumn"][2]], position);
        Draw("flag-pole-back"); Draw($"flag-cloth-{color}");
        if (glyph >= 0)
        {
            int ink = new[] { 3,6,7,8,11,16,18,21,22,23,24,26,27,29,31,33,38,39,40,46,49,50 }.Contains(color) ? 1 : 0;
            Draw($"flag-glyph-{ink}-{glyph}");
        }
        Draw("flag-pole-front");
    }
    public void FocusWorld(Vector2 point) => RequestView(new(point.X, point.Y, RequestedZoom));
    public void FocusBuilding(int id)
    {
        var b = Session.View.Settlements.Single(b => b.Id == id); var c = MapGeometry.Center(b.X, b.Y); FocusWorld(new(c.X, c.Y));
    }
    public void FocusUnit(int id)
    {
        var unit = Session.View.World.Units.SingleOrDefault(u => u.Id == id);
        if (unit is null) return;
        var center = MapGeometry.Center(unit.Cell.X, unit.Cell.Y);
        FocusWorld(new(center.X, center.Y));
    }
    public Vector2 WorldToViewport(Vector2 world) => GetViewport().CanvasTransform * world;
    public Vector2 ViewportToWorld(Vector2 viewport) => GetViewport().CanvasTransform.AffineInverse() * viewport;
    public void SetZoom(float zoom)
    {
        if (!float.IsFinite(zoom) || zoom <= 0) throw new ArgumentOutOfRangeException(nameof(zoom));
        RequestView(_requested with { Zoom = Math.Clamp(zoom, .25f, 2) });
    }
    private void RequestView(MapViewport view)
    {
        view = _map is null ? view : view.Clamp(_map.Squares);
        if (_requested == view && LoadError is null) return;
        _requested = view; _error = null; _display?.RetryFailedRequest(); _navigation.Restart(); RequestDisplay();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (_map is null) return;
        if (input is InputEventMouseButton button)
        {
            if (button.Position.X < 0 || button.Position.Y < 0 || button.Position.X >= ClientLayout.LogicalWidth || button.Position.Y >= ClientLayout.LogicalHeight) return;
            if (IsInputBlocked?.Invoke(button.Position) == true) { _dragging = false; return; }
            if (button.ButtonIndex is MouseButton.Left or MouseButton.Right && CanIssueOrders?.Invoke() == false)
            { GetViewport().SetInputAsHandled(); return; }
            if (button.ButtonIndex == MouseButton.Middle) { _dragging = button.Pressed; GetViewport().SetInputAsHandled(); }
            else if (button.Pressed && button.ButtonIndex == MouseButton.Right)
            {
                _dragging = false;
                if (ObjectAtViewport(button.Position) is {} target)
                {
                    if (target.IsUnit) UnitContextRequested?.Invoke(target.Id, button.Position);
                    else BuildingContextRequested?.Invoke(target.Id, button.Position);
                }
                GetViewport().SetInputAsHandled();
            }
            else if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                var offset = button.Position - new Vector2(ClientLayout.LogicalWidth, ClientLayout.LogicalHeight) / 2;
                float zoom = Math.Clamp(RequestedZoom * (button.ButtonIndex == MouseButton.WheelUp ? 1.2f : 1 / 1.2f), .25f, 2);
                var center = RequestedPosition + offset / RequestedZoom - offset / zoom;
                RequestView(new(center.X, center.Y, zoom)); GetViewport().SetInputAsHandled();
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.Left)
            {
                if (ObjectAtViewport(button.Position) is {} target)
                    CommandMenuRequested?.Invoke(target, button.Position);
                GetViewport().SetInputAsHandled();
            }
        }
        else if (input is InputEventMouseMotion motion && _dragging)
        {
            if (IsInputBlocked?.Invoke(motion.Position) == true) { _dragging = false; return; }
            FocusWorld(RequestedPosition - motion.Relative / RequestedZoom); GetViewport().SetInputAsHandled();
        }
    }
    public override void _ExitTree()
    {
        _cancellation.Cancel(); _cancellation.Dispose();
        if (_loading is { } loading) _ = loading.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        _flags?.Dispose(); _terrain?.Dispose();
    }
}
