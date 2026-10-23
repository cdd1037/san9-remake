using Godot;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class MapWorld
{
    public Control CreateInformationPreview(int regionId, TextCatalog texts, int? unitId = null,
        Func<StrategySnapshot>? readView = null) =>
        new InformationMapPreview { World = this, RegionId = regionId, UnitId = unitId, Texts = texts, ReadView = readView };
}

// Only preview framing and clipping live here; every map layer is drawn by MapSceneView.
public partial class InformationMapPreview : Control
{
    public MapWorld World { get; init; } = null!;
    public int RegionId { get; init; }
    public int? UnitId { get; init; }
    public TextCatalog Texts { get; init; } = null!;
    public Func<StrategySnapshot>? ReadView { get; init; }
    private MapSceneView _view = null!;
    private long _revision = -1;
    private MapScene? _requestedScene;
    private MapScene? _sourceScene;
    private MapScene? _previewScene;
    private Vector2 _requestedSize;
    private string? _error;
    public override void _Ready()
    {
        ClipContents = true; MouseFilter = MouseFilterEnum.Ignore;
        _view = new MapSceneView { Textures = World.Textures, Terrain = World.Terrain,
            AnimationTime = () => World.AnimationTime, ShowAnnotations = true, Texts = Texts, Font = GetThemeFont("font") };
        _view.Committed += () =>
        {
            var frame = _view.Presented!;
            _view.Position = frame.Size / 2 - new Vector2((float)frame.Camera.X, (float)frame.Camera.Y);
            QueueRedraw();
        };
        AddChild(_view);
    }
    public override void _Process(double delta)
    {
        if (World.Display?.Presented?.Region.Scene is not { } scene || Size.X <= 0 || Size.Y <= 0) return;
        if (ReadView is { } readView)
        {
            var state = readView();
            // Draft changes do not advance the world's revision; snapshot identity owns invalidation.
            if (!ReferenceEquals(_sourceScene, scene) || !ReferenceEquals(_previewScene?.State, state))
            {
                _sourceScene = scene;
                _previewScene = new(scene.Map, scene.Terrain, scene.Season, state, World.Textures.MapAssets,
                    () => World.UnitPositionTime, _previewScene);
            }
            scene = _previewScene!;
        }
        if (_error != _view.Error) { _error = _view.Error; QueueRedraw(); }
        if (ReferenceEquals(_requestedScene, scene) && _requestedSize == Size) return;
        _requestedScene = scene; _requestedSize = Size;
        var settlement = scene.State.Settlements.FirstOrDefault(s => s.Id == RegionId);
        Vector2 center;
        if (UnitId is int unitId && scene.State.World.Units.FirstOrDefault(u => u.Id == unitId) is {} unit)
        {
            double x = (unit.Position.X - San9.Core.Domain.MapCoordinates.UnitsPerCell / 2) / (double)San9.Core.Domain.MapCoordinates.UnitsPerCell;
            double y = (unit.Position.Y - San9.Core.Domain.MapCoordinates.UnitsPerCell / 2) / (double)San9.Core.Domain.MapCoordinates.UnitsPerCell;
            center = new((float)(32 * (x + scene.Map.Height - y) + 96), (float)(16 * (x + y + 1) + 112));
        }
        else if (settlement is not null)
        {
            var p = MapGeometry.Center(settlement.X, settlement.Y, scene.Map.Height);
            int elevation = scene.Map.Squares[settlement.Y * scene.Map.Width + settlement.X].Elevation;
            center = new(p.X, p.Y - 10 * (elevation - 1) - 20);
        }
        else
        {
            var cells = scene.Map.Squares.Select((s, i) => (s, i)).Where(p => p.s.RegionId == RegionId)
                .Select(p => new Vector2(p.i % scene.Map.Width, p.i / scene.Map.Width)).ToArray();
            if (cells.Length == 0) return;
            var average = cells.Aggregate(Vector2.Zero, (a, b) => a + b) / cells.Length;
            var cell = cells.MinBy(p => p.DistanceSquaredTo(average));
            var p = MapGeometry.Center((int)cell.X, (int)cell.Y, scene.Map.Height); center = new(p.X, p.Y);
        }
        _view.Request(scene, new(center.X, center.Y, 1), Size, false);
        if (_revision != _view.Revision) { _revision = _view.Revision; QueueRedraw(); }
    }
    public override void _Draw()
    {
        DrawRect(new(Vector2.Zero, Size), new Color("191b18"));
        if (_view.Presented is not null) return;
        string message = _view.Error is null ? "正在准备地图…" : "地图加载失败";
        GameTextPresenter.Draw(this, GetThemeFont("font"), message, new(0, Size.Y / 2),
            GameTextStyles.CatalogRow, HorizontalAlignment.Center, Size.X);
    }
}
