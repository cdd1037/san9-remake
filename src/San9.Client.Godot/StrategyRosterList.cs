using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class StrategyRosterList : Control
{
    public StrategyRoster Roster { get; } = new();
    public IReadOnlyDictionary<string, ImageTexture> Textures { get; init; } = null!;
    public San9.Content.Text.TextCatalog Texts { get; init; } = null!;
    public San9.Content.Interface.InterfaceSkin Skin { get; init; } = null!;
    public Action<RosterKey>? Activated { get; init; }
    public Action? ContextRequested { get; init; }
    public IReadOnlyList<RosterRowButton> Rows => _rows;
    private readonly List<RosterRowButton> _rows = [];
    private VScrollBar _bar = null!;
    private Button _up = null!, _down = null!;
    public VScrollBar ScrollBar => _bar;

    public override void _Ready()
    {
        Size = new(116, 280); MouseFilter = MouseFilterEnum.Stop;
        for (int i = 0; i <= RosterPageState.PageSize; i++)
        {
            var row = new RosterRowButton { Textures = Textures, Position = new(0, i * 20),
                Scrolled = Wheel, ContextRequested = () => ContextRequested?.Invoke() };
            AddChild(row); _rows.Add(row);
            row.Pressed += () => { if (row.EntryKey is RosterKey key) Activated?.Invoke(key); };
        }
        _bar = new RosterScrollBar { Scrolled = Wheel, Position = new(100, 16), Size = new(16, 248), Step = 1, CustomStep = 1,
            Page = RosterPageState.PageSize, FocusMode = FocusModeEnum.All };
        AddChild(_bar);
        Button Arrow(string text, int y, int step)
        {
            var button = new Button { Text = text, Position = new(100, y), Size = new(16, 16), FocusMode = FocusModeEnum.All };
            button.AddThemeFontSizeOverride("font_size", 8);
            button.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("202025") });
            button.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = new Color("404045") });
            button.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = new Color("606065") });
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            button.Pressed += () => Wheel(step);
            button.MouseEntered += () => button.GrabFocus();
            button.GuiInput += @event =>
            {
                if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right }) ContextRequested?.Invoke();
                if (@event is InputEventMouseButton { Pressed: true } mouse)
                {
                    if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
                        Wheel(mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1);
                }
            };
            AddChild(button); button.Size = new(16, 16); return button;
        }
        _up = Arrow("▲", 0, -1); _down = Arrow("▼", 264, 1);
        _bar.ValueChanged += value => { Roster.Current.ScrollTo((int)value); BindRows(); };
        _bar.MouseEntered += () => _bar.GrabFocus();
        _bar.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right })
            { ContextRequested?.Invoke(); _bar.AcceptEvent(); }
        };
    }

    public void Refresh(StrategySnapshot snapshot, int corpsId)
    {
        Roster.Refresh(snapshot, corpsId);
        BindPage();
    }

    public void SelectPage(RosterPage page)
    {
        if (Roster.Page == page) return;
        Roster.SelectPage(page); BindPage();
    }

    private void BindPage()
    {
        bool blocked = _bar.IsBlockingSignals();
        _bar.SetBlockSignals(true);
        try
        {
            _bar.MaxValue = Math.Max(RosterPageState.PageSize, Roster.Current.Ordinary.Count);
            _bar.Page = RosterPageState.PageSize;
            _bar.Visible = _up.Visible = _down.Visible = Roster.Current.MaximumOffset > 0;
            _bar.SetValueNoSignal(Roster.Current.Offset);
        }
        finally { _bar.SetBlockSignals(blocked); }
        BindRows();
    }

    private void BindRows()
    {
        var entries = new[] { Roster.Current.Pinned }.Concat(Roster.Current.Visible).ToArray();
        for (int i = 0; i < _rows.Count; i++)
        {
            var entry = i < entries.Length ? entries[i] : null;
            _rows[i].Bind(entry, entry is null ? "" : Texts.Resolve(entry.NameKey),
                new Color(Skin.Colors[entry?.IsAvailable == true ? "roster-available" : "roster-unavailable"]));
        }
    }

    public void Wheel(int direction)
    {
        Roster.Current.Wheel(direction); _bar.SetValueNoSignal(Roster.Current.Offset); BindRows();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right })
        { ContextRequested?.Invoke(); AcceptEvent(); return; }
        if (@event is not InputEventMouseButton { Pressed: true } mouse) return;
        if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            Wheel(mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1);
        AcceptEvent();
    }
}
