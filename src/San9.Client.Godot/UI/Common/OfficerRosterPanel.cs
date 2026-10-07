using Godot;

namespace San9.Client.Godot;

public sealed record OfficerRosterColumn(string Title, int Width, Color? TextColor = null,
    HorizontalAlignment Alignment = HorizontalAlignment.Center);
public sealed record OfficerRosterEntry(OfficerCardData Officer, IReadOnlyList<string> Cells)
{
    public IReadOnlyDictionary<int, string> Icons { get; init; } = new Dictionary<int, string>();
}

/// <summary>Command roster with configurable columns and page size, independent of selection rules.</summary>
public partial class OfficerRosterPanel : Control
{
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public IReadOnlyList<OfficerRosterColumn> Columns { get; init; } = [];
    public int VisibleRows { get; init; } = 5;
    public bool ShowPagination { get; init; }
    public bool ShowPortrait { get; init; } = true;
    public int? BodyHeight { get; init; }
    public int PageNumberWidth { get; init; } = 56;
    public int PaginationSpacing { get; init; } = 2;
    public int PaginationTop { get; init; } = 4;
    public int PaginationLabelTop { get; init; } = 10;
    public int CountGap { get; init; } = 70;
    public bool PortraitFollowsPage { get; init; } = true;
    public int Page { get; private set; }
    public int PageCount => Math.Max(1, (_entries.Length + VisibleRows - 1) / VisibleRows);
    private OfficerRosterEntry[] _entries = [];
    private readonly GameTextPresenter _text = new();
    private StyleBoxTexture _field = null!;
    private Control? _body;
    private float TableWidth => Columns.Sum(c => c.Width);
    private float PanelHeight => BodyHeight ?? Math.Max(144, 44 + VisibleRows * 20);
    private float PanelWidth => TableWidth + (ShowPortrait ? 216 : 0);
    private float NextPageX => 56 + PageNumberWidth + PaginationSpacing;
    private float PageLabelY => PanelHeight + PaginationLabelTop;

    public void SetEntries(IEnumerable<OfficerRosterEntry> entries)
    {
        var snapshot = entries.ToArray();
        if (snapshot.Any(e => e.Cells.Count != Columns.Count))
            throw new ArgumentException("Roster cells must match the configured columns.", nameof(entries));
        _entries = snapshot;
        Page = 0;
        if (IsNodeReady()) Rebuild();
    }

    public override void _Ready()
    {
        if (VisibleRows < 1 || Columns.Count == 0 || Columns.Any(c => c.Width < 48))
            throw new InvalidOperationException("A roster needs a positive page size and columns at least 48 pixels wide.");
        MouseFilter = MouseFilterEnum.Ignore;
        _field = CommandPanelArt.Field(Textures);
        Rebuild();
    }

    public void ShowPage(int page)
    {
        Page = Math.Clamp(page, 0, PageCount-1);
        if (IsNodeReady()) Rebuild();
    }

    private void Rebuild()
    {
        if (_body is not null) { RemoveChild(_body); _body.QueueFree(); }
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_body);
        Size = new(PanelWidth, PanelHeight+(ShowPagination || PageCount > 1 ? 44 : 0));
        float x = 0;
        for (int column = 0; column < Columns.Count; column++)
        {
            var definition = Columns[column];
            CommandPanelArt.Label(_body, _text, definition.Title, new(x+8,8,definition.Width-16,24));
            for (int row = 0; row < VisibleRows; row++)
            {
                int index = Page*VisibleRows+row;
                if (index >= _entries.Length) break;
                int inset = _entries[index].Icons.ContainsKey(column) ? 12 : 0;
                CommandPanelArt.Label(_body, _text, _entries[index].Cells[column], new(x+8+inset,40+row*20,definition.Width-16-inset,20), definition.TextColor, definition.Alignment);
            }
            x += definition.Width;
        }
        if (ShowPortrait) _body.AddChild(new OfficerCard { Service = Service, Textures = Textures, Position = new(TableWidth+8,8),
            Officer = _entries.Skip(PortraitFollowsPage ? Page*VisibleRows : 0).FirstOrDefault()?.Officer });
        if (ShowPagination || PageCount > 1)
        {
            PageButton("previous", 0, Page == 0, -1);
            CommandPanelArt.Label(_body, _text, _entries.Length == 0 ? "" : $"{Page+1}/{PageCount}", new(56,PageLabelY,PageNumberWidth,24));
            PageButton("next", NextPageX, Page == PageCount-1, 1);
            var count = new OfficerPropertyList { Textures = Textures, LabelSkinPrefix = "deployment-mode", Position = new(NextPageX+CountGap,PageLabelY) };
            count.SetProperties([new("武将", _entries.Length == 0 ? "" : $"{_entries.Length}名", 56)]);
            _body.AddChild(count);
        }
        QueueRedraw();
    }

    private void PageButton(string role, float x, bool disabled, int direction)
    {
        string prefix = "deployment-page-" + role;
        var button = new CommandPageButton { Textures = Textures, SkinPrefix = prefix,
            Position = new(x,PanelHeight+PaginationTop), Disabled = disabled, FocusMode = FocusModeEnum.None };
        button.Pressed += () => ShowPage(Page+direction);
        _body!.AddChild(button);
    }

    public override void _Draw()
    {
        DrawStyleBox(_field, new(0,0,PanelWidth,PanelHeight));
        if (ShowPagination || PageCount > 1)
        {
            DrawStyleBox(_field, new(56,PageLabelY,PageNumberWidth,24));
            SkinPainter.Tile(this, Textures["deployment-field-rim"], new(56,PageLabelY,PageNumberWidth,4));
            SkinPainter.Tile(this, Textures["deployment-field-rim"], new(56,PageLabelY+20,PageNumberWidth,4));
        }
        float x = 0;
        foreach (var column in Columns)
        {
            CommandPanelArt.Strip(this, Textures, "deployment-roster-header", new(x+8,8,column.Width-16,24));
            x += column.Width;
            CommandPanelArt.ColumnRule(this, Textures, new(x,8), PanelHeight-16);
        }
        for (int row = 0; row < VisibleRows && Page*VisibleRows+row < _entries.Length; row++)
            foreach (var (column, role) in _entries[Page*VisibleRows+row].Icons)
                DrawTexture(Textures[role], new(Columns.Take(column).Sum(c => c.Width)+3,40+row*20));
    }

    public override void _ExitTree() { _text.Dispose(); _field.Dispose(); }
}
