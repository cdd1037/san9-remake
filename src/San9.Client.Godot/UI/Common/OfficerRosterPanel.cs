using Godot;

namespace San9.Client.Godot;

public sealed record OfficerRosterColumn(string Title, int Width, Color? TextColor = null);
public sealed record OfficerRosterEntry(OfficerCardData Officer, IReadOnlyList<string> Cells);

/// <summary>Command roster with configurable columns and page size, independent of selection rules.</summary>
public partial class OfficerRosterPanel : Control
{
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public IReadOnlyList<OfficerRosterColumn> Columns { get; init; } = [];
    public int VisibleRows { get; init; } = 5;
    public int Page { get; private set; }
    public int PageCount => Math.Max(1, (_entries.Length + VisibleRows - 1) / VisibleRows);
    private OfficerRosterEntry[] _entries = [];
    private readonly GameTextPresenter _text = new();
    private StyleBoxTexture _field = null!;
    private Control? _body;
    private float TableWidth => Columns.Sum(c => c.Width);
    private float PanelHeight => Math.Max(144, 44 + VisibleRows * 20);

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
        Size = new(TableWidth+216, PanelHeight+(PageCount > 1 ? 44 : 0));
        float x = 0;
        for (int column = 0; column < Columns.Count; column++)
        {
            var definition = Columns[column];
            CommandPanelArt.Label(_body, _text, definition.Title, new(x+8,8,definition.Width-16,24));
            for (int row = 0; row < VisibleRows; row++)
            {
                int index = Page*VisibleRows+row;
                if (index >= _entries.Length) break;
                CommandPanelArt.Label(_body, _text, _entries[index].Cells[column], new(x+8,40+row*20,definition.Width-16,20), definition.TextColor);
            }
            x += definition.Width;
        }
        _body.AddChild(new OfficerCard { Service = Service, Textures = Textures, Position = new(TableWidth+8,8),
            Officer = _entries.Skip(Page*VisibleRows).FirstOrDefault()?.Officer });
        if (PageCount > 1)
        {
            PageButton("上一页", 0, Page == 0, -1);
            CommandPanelArt.Label(_body, _text, $"{Page+1}/{PageCount}", new(104,PanelHeight+4,Size.X-208,32));
            PageButton("下一页", Size.X-104, Page == PageCount-1, 1);
        }
        QueueRedraw();
    }

    private void PageButton(string title, float x, bool disabled, int direction)
    {
        var button = new InformationMenuButton { Title = title, Textures = Textures, SkinPrefix = "deployment-edit",
            Position = new(x,PanelHeight+4), Size = new(104,40), TextInsets = new(0,0,8,6), Disabled = disabled,
            TextStyle = (_, _, disabled) => disabled ? GameTextStyles.CatalogDisabledRow : GameTextStyles.CatalogRow };
        button.Pressed += () => ShowPage(Page+direction);
        _body!.AddChild(button);
    }

    public override void _Draw()
    {
        DrawStyleBox(_field, new(0,0,TableWidth+216,PanelHeight));
        float x = 0;
        foreach (var column in Columns)
        {
            CommandPanelArt.Strip(this, Textures, "deployment-roster-header", new(x+8,8,column.Width-16,24));
            x += column.Width;
            CommandPanelArt.ColumnRule(this, Textures, new(x,8), PanelHeight-16);
        }
    }

    public override void _ExitTree() { _text.Dispose(); _field.Dispose(); }
}
