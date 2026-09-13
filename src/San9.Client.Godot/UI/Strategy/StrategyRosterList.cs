using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class StrategyRosterList : Control
{
    public StrategyRoster Roster { get; } = new();
    public IReadOnlyDictionary<string, Texture2D> Textures { get; init; } = null!;
    public San9.Content.Text.TextCatalog Texts { get; init; } = null!;
    public San9.Content.Interface.InterfaceSkin Skin { get; init; } = null!;
    public Action<RosterKey>? Activated { get; init; }
    public Action? ContextRequested { get; init; }
    public IReadOnlyList<RosterRowButton> Rows => _rows;
    private readonly List<RosterRowButton> _rows = [];
    private VScrollBar _bar = null!;
    private SkinnedScrollBar _scroll = null!;
    private TextureRect _decoration = null!;
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
        _decoration = new TextureRect { Texture = Textures["roster-scroll-decoration"],
            Position = new(100, 0), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_decoration);
        _scroll = new SkinnedScrollBar { Skin = ScrollBarSkin.FromInterface(Textures),
            Position = new(100, 0), Size = new(16, 280),
            ContextRequested = () => ContextRequested?.Invoke() };
        AddChild(_scroll);
        _bar = _scroll.Range;
        _bar.Page = RosterPageState.PageSize;
        _bar.ValueChanged += value => { Roster.Current.ScrollTo((int)value); BindRows(); };

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
            _scroll.Visible = Roster.Current.MaximumOffset > 0;
            _decoration.Visible = !_scroll.Visible;
            _bar.Visible = _scroll.Visible;
            _bar.SetValueNoSignal(Roster.Current.Offset);
        }
        finally { _bar.SetBlockSignals(blocked); }
        BindRows();
    }

    private void BindRows()
    {
        _scroll.UpdateArrows();
        var entries = new[] { Roster.Current.Pinned }.Concat(Roster.Current.Visible).ToArray();
        var evaluator = new MessageEvaluator(Texts.Messages);
        for (int i = 0; i < _rows.Count; i++)
        {
            var entry = i < entries.Length ? entries[i] : null;
            string markup = entry is null ? "" : evaluator.Evaluate(entry.MessageKey,
                new(new Dictionary<string, MessageBinding> { ["name"] = new(0, entry.ResolveName(Texts)) },
                    (_, _) => throw new InvalidDataException("Name message cannot query world state."),
                    _ => throw new InvalidDataException("Name message cannot draw randomness.")));
            _rows[i].Bind(entry, markup,
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
