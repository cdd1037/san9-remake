using Godot;
using San9.Application.Authoring;
using San9.Content.Images;
using San9.Content.Maps;
using San9.Content.Packages;
using San9.Core.Domain;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>Versioned editor preview. Terrain pixels, object layers and picking commit together.</summary>
public partial class MapEditorCanvas : Control
{
    private readonly record struct RegionLabel(int RegionId, Vector2 Anchor);
    private sealed record Prepared(long Revision, string Season, MapPackage Map, MapBuildingPlacement[] Sites, TerrainRenderer Renderer, MapSurface Surface, MapSprite[] Objects, RegionLabel[] RegionLabels,
        Dictionary<string, PngImage> Common, Dictionary<int, PngImage> Land);
    private sealed record Frame(Prepared Source, Rect2 Coverage, bool Buildings, bool Boundaries,
        TerrainChunkCache<ImageTexture>.Lease Terrain, TextureScope Objects, MapSprite[] Sprites, MapSprite[] Borders) : IDisposable
    {
        public Vector2 Origin { get; set; }
        public float Zoom { get; set; }
        public Vector2 Size { get; set; }
        public void Dispose() { Terrain.Dispose(); Objects.Dispose(); }
    }
    public MapEditorWorkspace Workspace { get; init; } = null!;
    public TexturePackage Resources { get; init; } = null!;
    public Action<MapCell, bool>? Pressed;
    public Action<MapCell>? Dragged;
    public Action? Released;
    public Action<MapCell?>? Hovered;
    public Action<string>? Failed;
    public Action<float>? ZoomChanged;
    public string Season { get; set; } = "spring";
    public bool ShowGrid { get => _grid; set { _grid = value; QueueRedraw(); } }
    public bool ShowRegions { get => _regions; set { _regions = value; QueueRedraw(); } }
    public bool ShowBoundaries { get; set; } = true;
    public bool ShowBuildings { get; set; } = true;
    public ConstructionSurface ConstructionOverlay { get => _constructionOverlay; set { _constructionOverlay = value; QueueRedraw(); } }
    public int BrushRadius { get => _brushRadius; set { _brushRadius = value; QueueRedraw(); } }
    public int? SelectedBuilding { get => _selectedBuilding; set { _selectedBuilding = value; QueueRedraw(); } }
    public bool EditingEnabled { get; set; } = true;
    public MapCell? PlacementTarget { get => _placementTarget; set { _placementTarget = value; QueueRedraw(); } }
    private readonly CancellationTokenSource _lifetime = new();
    private TerrainChunkCache<ImageTexture> _terrain = null!;
    private TextureService _textures = null!;
    private Task<Prepared>? _loading;
    private Prepared? _prepared;
    private Frame? _pending, _presented;
    private TerrainChunkCache<ImageTexture>.Lease? _prefetchTerrain;
    private Vector2 _origin, _viewportSize;
    private MouseButton _panButton;
    private float _zoom = 0.8f;
    private bool _panning, _painting, _framed, _grid, _regions;
    private ConstructionSurface _constructionOverlay;
    private int _brushRadius;
    private int? _selectedBuilding;
    private MapCell? _hover, _placementTarget;
    private long _failedRevision = -1;
    private string? _failedSeason;

    public override void _Ready()
    {
        ClipContents = true; MouseFilter = MouseFilterEnum.Stop; FocusMode = FocusModeEnum.All; TextureFilter = TextureFilterEnum.Nearest;
        _textures = new TextureService(); AddChild(_textures); _textures.Initialize(Resources, Workspace.TextureRoot);
        _terrain = new(_textures.UploadGenerated, texture => texture.Dispose());
        MouseDefaultCursorShape = CursorShape.Cross;
        ZoomChanged?.Invoke(_zoom);
        MouseExited += () => { _hover = null; Hovered?.Invoke(null); QueueRedraw(); };
    }

    public void Focus(MapCell cell)
    {
        EndPaint();
        var center = MapGeometry.Center(cell.X, cell.Y, Workspace.Map.Height);
        _origin = Size / 2 - new Vector2(center.X, center.Y - 10 * (Workspace.Map.Cells[cell.Y * Workspace.Map.Width + cell.X].Elevation - 1)) * _zoom;
        QueueRedraw();
    }

    public void Fit()
    {
        EndPaint();
        var size = MapDisplayMetrics.CanvasSize(Workspace.Map.Width, Workspace.Map.Height);
        _zoom = Math.Clamp(Math.Min(Size.X / size.Width, Size.Y / size.Height) * 0.96f, 0.04f, 4);
        _origin = (Size - new Vector2(size.Width, size.Height) * _zoom) / 2;
        ZoomChanged?.Invoke(_zoom);
    }

    public void ZoomBy(float factor) => SetZoom(_zoom * factor);
    public void SetZoom(float zoom) => ZoomAt(zoom, Size / 2);

    private void ZoomAt(float zoom, Vector2 anchor)
    {
        EndPaint();
        Vector2 world = (anchor - _origin) / _zoom;
        _zoom = Math.Clamp(zoom, 0.04f, 4);
        _origin = anchor - world * _zoom;
        ZoomChanged?.Invoke(_zoom);
    }

    public void Retry()
    {
        _failedRevision = -1; _failedSeason = null;
        _textures.RetryFailed(_textures.MapAssets.Values.Select(a => a.File).Distinct());
        _pending?.Dispose(); _pending = null;
        _prepared = null;
    }

    public override void _Process(double delta)
    {
        if (_framed && _viewportSize != Size) _origin += (Size - _viewportSize) / 2;
        _viewportSize = Size;
        if (!_framed && Size.X > 0 && Size.Y > 0)
        {
            _framed = true;
            var first = Workspace.Buildings.FirstOrDefault();
            if (first is not null) Focus(new(first.X, first.Y)); else Fit();
        }
        if (_painting && !Input.IsMouseButtonPressed(MouseButton.Left)) EndPaint();
        if (_panning && !Input.IsMouseButtonPressed(_panButton))
        {
            _panning = false;
            MouseDefaultCursorShape = CursorShape.Cross;
        }
        if (_loading is { IsCompleted: true } loading)
        {
            _loading = null;
            try
            {
                var result = loading.GetAwaiter().GetResult();
                // CPU sources can be reused even when this draft was overtaken by another stroke.
                _prepared = result;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception error)
            {
                _failedRevision = Workspace.Revision; _failedSeason = Season; Failed?.Invoke(error.Message);
            }
        }
        if (_loading is null && (_prepared?.Revision != Workspace.Revision || _prepared.Season != Season) &&
            (_failedRevision != Workspace.Revision || _failedSeason != Season))
        {
            var map = Workspace.Snapshot(); var buildings = Workspace.Buildings;
            long revision = Workspace.Revision; string season = Season;
            var prior = _prepared; var token = _lifetime.Token; var geography = Workspace.Geography;
            _loading = Task.Run(() =>
            {
                var display = MapLayoutCompiler.Compile(map, buildings.Where(b => b.FixedType >= 0).ToArray(), Resources.Map, null, geography.RegionCityIds);
                token.ThrowIfCancellationRequested();
                var common = prior?.Common ?? ClientImageFiles.Load(Workspace.TextureRoot,
                    display.Assets.Where(a => display.Masks.Any(m => m.WeightAsset == a.Id || m.CoverageAsset == a.Id) || a.Id == "cloud-fill"), token);
                var land = prior?.Season == season ? prior.Land : ClientImageFiles.Load(Workspace.TextureRoot,
                    display.Seasons.Single(s => s.Id == season).Textures, token).ToDictionary(p => int.Parse(p.Key), p => p.Value);
                var visuals = BuildingVisuals.Resolve(display, display.Buildings.Select(b =>
                    new BuildingVisualState(b.Id, display.BuildingTypes.Single(t => t.Id == b.FixedType).MaxDurability, 0, 0, 0, 0, -1)).ToArray());
                var surface = new MapSurface(display.Width, display.Height, display.Squares);
                return new Prepared(revision, season, display, buildings, new(display, common, land), surface,
                    visuals.Buildings.SelectMany(b => b.Sprites).Concat(display.Bridges).OrderBy(s => s.Depth).ToArray(),
                    BuildRegionLabels(display, surface), common, land);
            }, token);
        }
        if (_prepared is { } prepared && prepared.Revision == Workspace.Revision && prepared.Season == Season &&
            (_failedRevision != Workspace.Revision || _failedSeason != Season) &&
            NeedsFrame(_pending ?? _presented, prepared)) Request(prepared);
        _terrain.Pump(() => _textures.CanUpload);
        if (_pending is { } obsolete && (obsolete.Source.Revision != Workspace.Revision || obsolete.Source.Season != Season))
        { obsolete.Dispose(); _pending = null; }
        if (_pending is { } pending)
        {
            if (pending.Terrain.Error is not null || pending.Objects.Error is not null)
            {
                Failed?.Invoke(pending.Terrain.Error?.Message ?? pending.Objects.Error!.Message);
                pending.Dispose(); _pending = null; _failedRevision = Workspace.Revision; _failedSeason = Season;
            }
            else if (FrameReady(pending, new Rect2(-pending.Origin / pending.Zoom, pending.Size / pending.Zoom)))
            {
                _presented?.Dispose(); _presented = pending; _pending = null;
                _hover = Pick(GetLocalMousePosition()); Hovered?.Invoke(_hover); QueueRedraw();
            }
        }
    }

    private static RegionLabel[] BuildRegionLabels(MapPackage map, MapSurface surface)
    {
        var regions = map.Squares.Select((square, index) => (square, index))
            .Where(cell => cell.square.RegionId >= 0).GroupBy(cell => cell.square.RegionId)
            .OrderByDescending(region => region.Count()).ThenBy(region => region.Key);
        return regions.Select(region =>
        {
            var visible = region.Where(cell => !cell.square.HideRegionName).ToArray();
            var centers = (visible.Length > 0 ? visible.AsEnumerable() : region).Select(cell =>
            {
                var tile = surface[cell.index];
                return new Vector2((float)tile.CenterX, (float)((tile.TopY + tile.BottomY) / 2));
            }).ToArray();
            var center = new Vector2(centers.Average(point => point.X), centers.Average(point => point.Y));
            return new RegionLabel(region.Key, centers.MinBy(point => point.DistanceSquaredTo(center)));
        }).ToArray();
    }

    private bool NeedsFrame(Frame? frame, Prepared source) => frame is null ||
        !ReferenceEquals(frame.Source, source) || frame.Origin != _origin || frame.Zoom != _zoom || frame.Size != Size ||
        frame.Buildings != ShowBuildings || frame.Boundaries != ShowBoundaries;

    private bool MoveWithinFrame(Frame? frame, Prepared source, Rect2 bounds)
    {
        if (frame is null || !ReferenceEquals(frame.Source, source) || frame.Buildings != ShowBuildings ||
            frame.Boundaries != ShowBoundaries || !frame.Coverage.Encloses(bounds)) return false;
        if (ReferenceEquals(frame, _presented) && !FrameReady(frame, bounds)) return false;
        frame.Origin = _origin; frame.Zoom = _zoom; frame.Size = Size;
        return true;
    }

    private static bool ChunkIntersects(TerrainChunkKey key, Rect2 bounds) =>
        bounds.Intersects(new(key.X * TerrainRenderer.ChunkSize, key.Y * TerrainRenderer.ChunkSize,
            TerrainRenderer.ChunkSize, TerrainRenderer.ChunkSize));

    private bool FrameReady(Frame frame, Rect2 bounds) =>
        frame.Terrain.Keys.Where(key => ChunkIntersects(key, bounds)).All(frame.Terrain.IsAvailable) &&
        frame.Objects.AreReady(frame.Sprites.Concat(frame.Borders).Where(sprite =>
        {
            var asset = _textures.MapAssets[sprite.AssetId];
            return bounds.Intersects(new(sprite.X, sprite.Y, asset.Width, asset.Height));
        }).Select(sprite => sprite.AssetId));

    private void Request(Prepared source)
    {
        var bounds = new Rect2(-_origin / _zoom, Size / _zoom);
        if (MoveWithinFrame(_presented, source, bounds))
        {
            _pending?.Dispose(); _pending = null;
            _hover = Pick(GetLocalMousePosition()); Hovered?.Invoke(_hover); QueueRedraw();
            return;
        }
        if (MoveWithinFrame(_pending, source, bounds)) return;

        var coverage = bounds.Grow(256);
        var viewport = new MapViewport(coverage.GetCenter().X, coverage.GetCenter().Y, 1);
        IEnumerable<TerrainChunkKey> Keys(int margin) => viewport.Chunks(source.Map.CanvasWidth, source.Map.CanvasHeight,
            (int)Math.Ceiling(coverage.Size.X), (int)Math.Ceiling(coverage.Size.Y), margin).Select(k => new TerrainChunkKey(source.Renderer, k.X, k.Y));
        var keys = Keys(0).OrderBy(key => ChunkIntersects(key, bounds) ? 0 : 1).ToArray();
        bool Visible(MapSprite sprite)
        {
            var asset = _textures.MapAssets[sprite.AssetId];
            return coverage.Intersects(new(sprite.X, sprite.Y, asset.Width, asset.Height));
        }
        var sprites = (ShowBuildings ? source.Objects : source.Map.Bridges).Where(Visible).ToArray();
        var borders = ShowBoundaries ? source.Map.Boundaries.Where(Visible).ToArray() : [];
        var scope = _textures.CreateScope(_textures.MapAssets);
        scope.Request(sprites.Concat(borders).Select(s => s.AssetId).Distinct());
        var next = new Frame(source, coverage, ShowBuildings, ShowBoundaries, _terrain.Acquire(keys), scope, sprites, borders)
            { Origin = _origin, Zoom = _zoom, Size = Size };
        var prefetch = _terrain.Acquire(Keys(1).Except(keys), prefetch: true);
        // Acquire shared work before releasing the previous view's ownership.
        _pending?.Dispose(); _prefetchTerrain?.Dispose();
        _pending = next; _prefetchTerrain = prefetch;
    }

    public MapCell? Pick(Vector2 local)
    {
        if (!new Rect2(Vector2.Zero, Size).HasPoint(local) || _presented is not { } frame) return null;
        var point = (local - frame.Origin) / frame.Zoom;
        int? index = frame.Source.Surface.PickCell(point.X, point.Y);
        return index is int i ? new(i % frame.Source.Map.Width, i / frame.Source.Map.Width) : null;
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton button)
        {
            if (button.Pressed) GrabFocus();
            if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && button.Pressed)
            {
                ZoomAt(_zoom * (button.ButtonIndex == MouseButton.WheelUp ? 1.2f : 1 / 1.2f), button.Position);
                AcceptEvent(); return;
            }
            if (_panning && button.ButtonIndex == _panButton && !button.Pressed)
            {
                _panning = false;
                MouseDefaultCursorShape = CursorShape.Cross;
                AcceptEvent(); return;
            }
            if (button.Pressed && (button.ButtonIndex is MouseButton.Middle or MouseButton.Right ||
                button.ButtonIndex == MouseButton.Left && Input.IsPhysicalKeyPressed(Key.Space)))
            {
                EndPaint();
                _panButton = button.ButtonIndex; _panning = true;
                MouseDefaultCursorShape = CursorShape.Drag;
                AcceptEvent(); return;
            }
            if (button.ButtonIndex == MouseButton.Left)
            {
                if (!button.Pressed) EndPaint();
                else if (EditingEnabled && Pick(button.Position) is { } cell)
                { _painting = !button.CtrlPressed; Pressed?.Invoke(cell, button.CtrlPressed); }
                AcceptEvent();
            }
        }
        else if (input is InputEventMouseMotion motion)
        {
            if (_panning) _origin += motion.Relative;
            _hover = Pick(motion.Position); Hovered?.Invoke(_hover); QueueRedraw();
            if (!_panning && _painting && EditingEnabled && _hover is { } cell) Dragged?.Invoke(cell);
            AcceptEvent();
        }
    }

    private void EndPaint() { if (!_painting) return; _painting = false; Released?.Invoke(); }

    private Vector2[] Diamond(MapCell cell, Frame frame)
    {
        var surface = frame.Source.Surface[cell.Y * frame.Source.Map.Width + cell.X];
        return [new((float)surface.CenterX, (float)surface.TopY), new((float)surface.RightX, (float)surface.RightY),
            new((float)surface.CenterX, (float)surface.BottomY), new((float)surface.LeftX, (float)surface.LeftY)];
    }

    public override void _Draw()
    {
        DrawRect(new(Vector2.Zero, Size), new Color("141b20"));
        if (_presented is not { } frame) return;
        DrawSetTransform(frame.Origin, 0, Vector2.One * frame.Zoom);
        foreach (var key in frame.Terrain.Keys)
            if (frame.Terrain.IsAvailable(key)) DrawTexture(frame.Terrain.Get(key), new(key.X * 512, key.Y * 512));
        if (ShowRegions || ShowGrid && frame.Zoom >= 0.35f || ConstructionOverlay != ConstructionSurface.None)
        {
            for (int i = 0; i < frame.Source.Map.Squares.Length; i++)
            {
                var surface = frame.Source.Surface[i];
                if (frame.Origin.X + surface.RightX * frame.Zoom < 0 || frame.Origin.X + surface.LeftX * frame.Zoom > Size.X ||
                    frame.Origin.Y + surface.BottomY * frame.Zoom < 0 || frame.Origin.Y + surface.TopY * frame.Zoom > Size.Y) continue;
                var points = Diamond(new(i % frame.Source.Map.Width, i / frame.Source.Map.Width), frame);
                int region = frame.Source.Map.Squares[i].RegionId;
                if (ShowRegions && region >= 0) DrawColoredPolygon(points, Color.FromHsv((region * 0.618034f) % 1, 0.65f, 0.9f, 0.25f));
                if (ConstructionOverlay != ConstructionSurface.None &&
                    (frame.Source.Map.Authoring!.Cells[i].Construction & ConstructionOverlay) == 0)
                    DrawColoredPolygon(points, new Color(0, 0, 0, TargetOverlayGeometry.DimOpacity));
                if (ShowGrid && frame.Zoom >= 0.35f) DrawPolyline([.. points, points[0]], new Color(1, 1, 1, 0.22f), 1 / frame.Zoom);
            }
        }
        foreach (var sprite in frame.Sprites.Concat(frame.Borders))
            if (frame.Objects.TryGetReady(sprite.AssetId, this, out var texture)) DrawTexture(texture, new(sprite.X, sprite.Y));
        if (ShowBuildings)
            foreach (var site in frame.Source.Sites.Where(s => s.FixedType < 0))
            {
                var points = Diamond(new(site.X, site.Y), frame);
                DrawCircle((points[0] + points[2]) / 2, 3 / frame.Zoom, new Color("c2ae82"));
            }
        if (SelectedBuilding is int selected && frame.Source.Sites.FirstOrDefault(b => b.Id == selected) is { } building)
        {
            var points = Diamond(new(building.X, building.Y), frame);
            DrawPolyline([.. points, points[0]], new Color("f6d488"), 3 / frame.Zoom);
        }
        if (PlacementTarget is { } target)
        {
            var points = Diamond(target, frame);
            DrawColoredPolygon(points, new Color(0.4f, 0.9f, 0.85f, 0.28f));
            DrawPolyline([.. points, points[0]], new Color("84e0cf"), 3 / frame.Zoom);
        }
        if (!_panning && PlacementTarget is null && _hover is { } hover)
        {
            int radius = BrushRadius;
            for (int y = Math.Max(0, hover.Y - radius); y <= Math.Min(frame.Source.Map.Height - 1, hover.Y + radius); y++)
            for (int x = Math.Max(0, hover.X - radius); x <= Math.Min(frame.Source.Map.Width - 1, hover.X + radius); x++)
            {
                if ((x - hover.X) * (x - hover.X) + (y - hover.Y) * (y - hover.Y) > radius * radius) continue;
                var points = Diamond(new(x, y), frame);
                DrawColoredPolygon(points, new Color(1, 1, 1, 0.18f));
                DrawPolyline([.. points, points[0]], Colors.White, 2 / frame.Zoom);
            }
        }
        DrawSetTransform(Vector2.Zero);
        if (ShowRegions) DrawRegionLabels(frame);
    }

    private void DrawRegionLabels(Frame frame)
    {
        const int fontSize = 16;
        var font = GetThemeDefaultFont();
        var viewport = new Rect2(Vector2.Zero, Size);
        List<Rect2> occupied = [];
        foreach (var label in frame.Source.RegionLabels)
        {
            var center = frame.Origin + label.Anchor * frame.Zoom;
            if (!viewport.HasPoint(center)) continue;
            string name = Workspace.RegionName(label.RegionId);
            var size = font.GetStringSize(name, fontSize: fontSize);
            var bounds = new Rect2(center - size / 2, size).Grow(3);
            if (occupied.Any(other => other.Intersects(bounds))) continue;
            occupied.Add(bounds);
            var baseline = center - size / 2 + new Vector2(0, font.GetAscent(fontSize));
            DrawStringOutline(font, baseline, name, fontSize: fontSize, size: 4, modulate: Colors.Black);
            DrawString(font, baseline, name, fontSize: fontSize, modulate: new Color("e6f2ff"));
        }
    }

    public override void _ExitTree()
    {
        EndPaint(); _lifetime.Cancel();
        if (_loading is { } loading) _ = loading.ContinueWith(task => { _ = task.Exception; }, TaskScheduler.Default);
        _pending?.Dispose(); _presented?.Dispose(); _prefetchTerrain?.Dispose(); _terrain.Dispose(); _lifetime.Dispose();
    }
}
