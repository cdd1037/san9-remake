using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class StrategyOverviewPanel : FixedPage
{
    protected override Color PageBackground => Colors.Black;
    public StrategyOverview State { get; init; } = null!;
    public MapWorld World { get; init; } = null!;
    public GameSession Session { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    private TextureScope? _scope;
    private OverviewMapLayer? _map;
    private MinimapGeometry Geometry => Service.OverviewMap.Geometry;
    private TextureScope Textures => _scope!;
    private static readonly Vector2 Origin = new(40, 71);
    private readonly Node2D _viewport = new();
    private readonly Dictionary<OverviewLayer, StrategyButton> _buttons = [];
    private IReadOnlyList<ForceRankingColumn> _rankings = [];
    private long _revision = -1;
    private (long Revision, bool Forces, bool Facilities, bool Units)? _drawState;
    public StrategyButton? HoverButton => _buttons.Values.FirstOrDefault(b => b.IsHovered());

    public bool TrySquare(Vector2 position, out int x, out int y)
    {
        x = y = 0;
        if (_buttons.Values.Any(b => b.GetRect().HasPoint(position) && (b.TextureClickMask?.GetBitv((Vector2I)(position - b.Position)) ?? true))) return false;
        var local = position - Origin;
        if (!Geometry.TryGrid(local.X / 800, local.Y / 400, out double gx, out double gy)) return false;
        x = (int)Math.Floor(gx + .5); y = (int)Math.Floor(gy + .5);
        return true;
    }

    public override void _Ready()
    {
        Name = "StrategyOverview"; Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _viewport.Draw += DrawViewport;
        AddChild(_viewport);
        World.ViewportPresentationChanged += _viewport.QueueRedraw;
    }
    private void BuildButtons()
    {
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
                ToggleMode = true, TextRole = textRole, HintText = Texts.ResolveRich(StrategyHintText.Key($"overview-{role}")),
                Frames = Enumerable.Range(0,4).Select(f => (Texture2D)Textures.Get($"button-overview-{role}" + (f == 0 ? "" : $"-{f}"), this)).ToArray()
            };
            // The four rectangular frames contain a diamond-shaped button.
            button.Pressed += () => State.Toggle(layer);
            AddChild(button); _buttons.Add(layer, button);
        }
    }

    public override void _Process(double delta)
    {
        Visible = State.IsOpen;
        if (!Visible)
        {
            if (_scope is not null)
            {
                foreach (var button in _buttons.Values) { RemoveChild(button); button.QueueFree(); }
                _buttons.Clear(); _scope.Dispose(); _scope = null; _drawState = null; QueueRedraw();
            }
            return;
        }
        if (_scope is null)
        {
            _scope = Service.CreateScope(owner: this);
            _scope.RequestPrefixes("overview-background", "overview-terrain", "overview-ranking", "overview-marker-", "overview-facility-", "overview-unit-", "button-overview-");
            _map ??= new(Service, Skin);
            BuildButtons();
            _viewport.QueueRedraw();
        }
        foreach (var pair in _buttons)
        {
            string role = "button-overview-" + pair.Key.ToString().ToLowerInvariant();
            if (pair.Value.TextureClickMask is null) pair.Value.TextureClickMask = Service.ClickMask(Service.Roles[role]);
        }
        if (_revision != Session.Snapshot.Revision)
        {
            _revision = Session.Snapshot.Revision;
            _rankings = StrategyOverview.Rankings(Session.View);
            _map!.Update(Session.View);
        }
        _buttons[OverviewLayer.Forces].SetPressedNoSignal(State.ShowForces);
        _buttons[OverviewLayer.Facilities].SetPressedNoSignal(State.ShowFacilities);
        _buttons[OverviewLayer.Units].SetPressedNoSignal(State.ShowUnits);
        var drawState = (_revision, State.ShowForces, State.ShowFacilities, State.ShowUnits);
        if (_drawState != drawState) { _drawState = drawState; QueueRedraw(); }
    }

    public override void _ExitTree()
    {
        World.ViewportPresentationChanged -= _viewport.QueueRedraw;
        _viewport.Draw -= DrawViewport;
        _scope?.Dispose(); _map?.Dispose();
    }

    public bool Navigate(Vector2 position)
    {
        var local = position - Origin;
        if (!Geometry.TryGrid(local.X / 800, local.Y / 400, out double x, out double y)) return false;
        var world = Geometry.ToWorld(x, y);
        World.FocusWorld(new((float)world.X, (float)world.Y));
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
        if (!State.IsOpen || _scope is null) return;
        DrawTexture(Textures.Get("overview-background", this), Vector2.Zero);
        _map!.Draw(this, Textures, Origin, State.ShowForces);
        var view = Session.View;
        if (State.ShowFacilities)
            foreach (var facility in view.Settlements.Where(s => s.IsFacilityListEntry))
            {
                var center = Origin + _map!.Point(facility.X, facility.Y);
                int color = facility.OwnerId is int owner ? view.Forces.Single(f => f.Id == owner).ColorCode : 50;
                var texture = Textures.Get($"overview-{(facility.CityMarkerColor is not null ? "marker" : "facility")}-{color}", this);
                DrawTexture(texture, center - texture.GetSize()/2);
            }
        if (State.ShowUnits)
            foreach (var unit in view.World.Units)
            {
                var p = Origin + _map!.Point(unit.Cell.X, unit.Cell.Y);
                int color = view.Forces.Single(f => f.Id == unit.OwnerId).ColorCode;
                if (color < 50) DrawTexture(Textures.Get($"overview-unit-{color}", this),p-new Vector2(4,4));
            }
        DrawTexture(Textures.Get("overview-ranking", this), new(56,529));
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

    private void DrawEntry(ForceRankingEntry entry, int x, int y, int valueRight)
    {
        DrawText(entry.Rank.ToString(),new(x,y),16,16,Colors.White,HorizontalAlignment.Right);
        DrawTexture(Textures.Get($"overview-marker-{entry.Force.ColorCode}", this),new(x+20,y));
        DrawText(Texts.Resolve(entry.Force.NameKey),new(x+40,y),64,16,Colors.White);
        DrawText(entry.Value.ToString(),new(x+112,y),valueRight-(x+112),16,Colors.White,HorizontalAlignment.Right);
    }
    private void DrawText(string text, Vector2 p, float width, int size, Color color, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var font = GetThemeFont("font");
        GameTextPresenter.Draw(this, font, text, p + new Vector2(0, font.GetAscent(size)),
            GameTextStyles.For(GameTextRole.Resource) with { FontSize = size, Foreground = color }, alignment, width);
    }
    private void DrawViewport()
    {
        if (!State.IsOpen || _scope is null) return;
        var half = World.ViewportSize / (2 * World.ZoomFactor);
        var a = Geometry.FromWorld(World.CameraPosition.X-half.X, World.CameraPosition.Y-half.Y);
        var b = Geometry.FromWorld(World.CameraPosition.X+half.X, World.CameraPosition.Y+half.Y);
        var points = Geometry.ClipViewport(a.U,a.V,b.U,b.V).Select(p => Origin + new Vector2((float)p.U*800,(float)p.V*400)).ToArray();
        if (points.Length > 2) _viewport.DrawPolyline([..points,points[0]],Colors.White,1);
    }
}
