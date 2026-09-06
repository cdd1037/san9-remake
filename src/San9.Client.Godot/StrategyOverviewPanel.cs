using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class StrategyOverviewPanel : Control
{
    public StrategyOverview State { get; init; } = null!;
    public MapWorld World { get; init; } = null!;
    public GameSession Session { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public IReadOnlyDictionary<string, ImageTexture> Textures { get; init; } = null!;
    private static readonly Vector2 Origin = new(40, 71);
    private readonly Dictionary<OverviewLayer, StrategyButton> _buttons = [];
    private IReadOnlyList<ForceRankingColumn> _rankings = [];
    private long _revision = -1;
    public StrategyButton? HoverButton => _buttons.Values.FirstOrDefault(b => b.IsHovered());

    public bool TrySquare(Vector2 position, out int x, out int y)
    {
        x = y = 0;
        if (_buttons.Values.Any(b => b.GetRect().HasPoint(position) && b.TextureClickMask.GetBitv((Vector2I)(position - b.Position)))) return false;
        var local = position - Origin;
        if (!MinimapGeometry.TryGrid(local.X / 800, local.Y / 400, out double gx, out double gy)) return false;
        x = (int)Math.Floor(gx + .5); y = (int)Math.Floor(gy + .5);
        return true;
    }

    public override void _Ready()
    {
        Name = "StrategyOverview"; Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        foreach (var (layer, title, role, x, y, textRole) in new[]
        {
            (OverviewLayer.Forces, "势力", "forces", 564, 385, GameButtonTextRole.Territory),
            (OverviewLayer.Facilities, "设施", "facilities", 504, 415, GameButtonTextRole.Facilities),
            (OverviewLayer.Units, "部队", "units", 445, 445, GameButtonTextRole.Units)
        })
        {
            var button = new StrategyButton
            {
                Title = title, Position = new(x,y), Size = new(108,60), IgnoreTextureSize = true,
                ToggleMode = true, TextRole = textRole, HintText = Texts.Resolve(StrategyHintText.Key($"overview-{role}")),
                Frames = Enumerable.Range(0,4).Select(f => (Texture2D)Textures[$"button-overview-{role}" + (f == 0 ? "" : $"-{f}")]).ToArray()
            };
            // The four rectangular frames contain a diamond-shaped button.
            using var pixels = Textures[$"button-overview-{role}"].GetImage();
            var mask = new Bitmap(); mask.CreateFromImageAlpha(pixels); button.TextureClickMask = mask;
            button.Pressed += () => State.Toggle(layer);
            AddChild(button); _buttons.Add(layer, button);
        }
    }

    public override void _Process(double delta)
    {
        Visible = State.IsOpen;
        if (!Visible) return;
        if (_revision != Session.Snapshot.Revision)
        {
            _revision = Session.Snapshot.Revision;
            _rankings = StrategyOverview.Rankings(Session.View);
        }
        _buttons[OverviewLayer.Forces].SetPressedNoSignal(State.ShowForces);
        _buttons[OverviewLayer.Facilities].SetPressedNoSignal(State.ShowFacilities);
        _buttons[OverviewLayer.Units].SetPressedNoSignal(State.ShowUnits);
        QueueRedraw();
    }

    public bool Navigate(Vector2 position)
    {
        var local = position - Origin;
        if (!MinimapGeometry.TryGrid(local.X / 800, local.Y / 400, out double x, out double y)) return false;
        World.FocusWorld(new((float)(32 * (x + 200 - y) + 96), (float)(16 * (x + y + 1) + 112)));
        return true;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } button) Navigate(button.Position);
        if (@event is InputEventMouseButton { Pressed: true } wheel &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && TrySquare(wheel.Position, out _, out _))
            World.SetZoom(World.RequestedZoom * (wheel.ButtonIndex == MouseButton.WheelUp ? 1.2f : 1 / 1.2f));
        AcceptEvent();
    }

    public override void _Draw()
    {
        if (!State.IsOpen) return;
        DrawTexture(Textures["overview-background"], Vector2.Zero);
        DrawTexture(Textures["overview-terrain"], Origin);
        var view = Session.View;
        if (State.ShowForces)
        {
            foreach (var city in view.Settlements.Where(s => s.CityMarkerColor is not null))
                DrawTexture(Textures[$"overview-city-{city.Id}"], Origin, ForceColor(city.OwnerId));
            DrawTexture(Textures["overview-borders"], Origin);
        }
        if (State.ShowFacilities)
            foreach (var facility in view.Settlements.Where(s => s.IsFacilityListEntry))
            {
                var center = Origin + new Vector2(2 * (facility.X - facility.Y) + 401, facility.X + facility.Y);
                int color = facility.OwnerId is int owner ? view.Forces.Single(f => f.Id == owner).ColorCode : 50;
                var texture = Textures[$"overview-{(facility.CityMarkerColor is not null ? "marker" : "facility")}-{color}"];
                DrawTexture(texture, center - texture.GetSize()/2);
            }
        if (State.ShowUnits)
            foreach (var unit in view.World.Units)
            {
                var p = Origin + new Vector2(2 * (unit.X - unit.Y) + 401, unit.X + unit.Y);
                int color = view.Forces.Single(f => f.Id == unit.OwnerId).ColorCode;
                if (color < 50) DrawTexture(Textures[$"overview-unit-{color}"],p-new Vector2(4,4));
            }
        DrawViewport();
        DrawTexture(Textures["overview-ranking"], new(56,529));
        DrawText("势力排行榜", new(80, 540), 240, 24, new Color("f8f850"));
        string[] titles = ["信望", "都市", "武将", "士兵"];
        int[] starts = [88,264,432,604];
        // Cell boundaries in the converted ranking background anchor both
        // centered headings and the shared numeric right inset.
        int[] cellEdges = [73,250,413,584,792];
        const int valueRightInset = 14;
        for (int column = 0; column < _rankings.Count; column++)
        {
            DrawText(titles[column], new(cellEdges[column],574), cellEdges[column+1]-cellEdges[column],16,new Color("f8f8b0"),HorizontalAlignment.Center);
            var ranking = _rankings[column];
            int valueRight = cellEdges[column+1] - valueRightInset;
            for (int row = 0; row < ranking.Leaders.Count; row++) DrawEntry(ranking.Leaders[row], starts[column], 602 + row * 28, valueRight);
            if (ranking.Player is { } player) DrawEntry(player, starts[column],694,valueRight);
        }
    }

    private Color ForceColor(int? owner)
    {
        int code = owner is int id ? Session.View.Forces.Single(f => f.Id == id).ColorCode : 50;
        return new(Skin.Colors[$"overview-force-{code}"]);
    }
    private void DrawEntry(ForceRankingEntry entry, int x, int y, int valueRight)
    {
        DrawText(entry.Rank.ToString(),new(x,y),16,16,Colors.White,HorizontalAlignment.Right);
        DrawTexture(Textures[$"overview-marker-{entry.Force.ColorCode}"],new(x+20,y));
        DrawText(Texts.Resolve(entry.Force.NameKey),new(x+40,y),64,16,Colors.White);
        DrawText(entry.Value.ToString(),new(x+112,y),valueRight-(x+112),16,Colors.White,HorizontalAlignment.Right);
    }
    private void DrawText(string text, Vector2 p, float width, int size, Color color, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var font = GetThemeFont("font");
        DrawString(font, p + new Vector2(0,font.GetAscent(size)), text, alignment, width, size, color);
    }
    private void DrawViewport()
    {
        var half = new Vector2(1024,768) / (2 * World.ZoomFactor);
        var a = MinimapGeometry.FromWorld(World.CameraPosition.X-half.X, World.CameraPosition.Y-half.Y);
        var b = MinimapGeometry.FromWorld(World.CameraPosition.X+half.X, World.CameraPosition.Y+half.Y);
        var points = MinimapGeometry.ClipViewport(a.U,a.V,b.U,b.V).Select(p => Origin + new Vector2((float)p.U*800,(float)p.V*400)).ToArray();
        if (points.Length > 2) DrawPolyline([..points,points[0]],Colors.White,1);
    }
}
