using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Content.Interface;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class PatrolView : FixedPage
{
    protected override Color PageBackground => new(0, 0, 0, .35f);
    public PatrolDraft Draft { get; init; } = null!;
    public GameSession Session { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action Submit { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private Control? _body, _child;
    private StyleBoxTexture _field = null!;

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _field = CommandPanelArt.Field(Textures);
        Rebuild();
    }
    public override void _Process(double delta) { if (Session.Snapshot.Revision != Draft.View.World.Revision) Closed(); }
    private Label Label(string value, Rect2 rect, int size = 16, Color? color = null)
    {
        var label = new Label { Text = value, Position = rect.Position, Size = rect.Size, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _body!.AddChild(label); _text.Apply(label, GameTextStyles.CatalogRow with { FontSize = size, Foreground = color ?? new Color("f0f0f0") });
        return label;
    }
    private void Rebuild()
    {
        if (_body is not null) { RemoveChild(_body); _body.QueueFree(); }
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        var window = new CommandWindow { Texts = Texts, Textures = Textures, Title = CommandText.Resolve(Texts, "title.patrol"), Position = new(280, 228), Size = new(464, 312),
            CanAccept = Draft.CanExecute, Accepted = Submit, Closed = Closed,
            Help = () => Notice(CommandText.Resolve(Texts, "hint.patrol-help")) };
        _body.AddChild(window);
        Label(CommandText.Resolve(Texts, "value.source", Draft.View.Settlements.Single(s => s.Id == Draft.Home.Id).ResolveName(Texts)), new(280,228,160,32));
        var edit = window.ActionButton(CommandText.Resolve(Texts, "label.actor"), "edit", new(0, 48, 104, 40), SelectOfficers);
        edit.Disabled = Draft.Candidates.Length < 2;
        var roster = new OfficerRosterPanel { Service = Service, Textures = Textures,
            Position = new(384,276), VisibleRows = 5, Columns = [new(CommandText.Resolve(Texts, "label.officer"),80), new(CommandText.Resolve(Texts, "label.intelligence"),64)] };
        roster.SetEntries(Draft.Members.Select(id =>
        {
            var person = Draft.View.Officers.Single(o => o.Id == id);
            string name = Texts.Resolve(person.NameKey);
            return new OfficerRosterEntry(new(name, $"catalog-person-{person.Id}", person.Abilities),
                [name, person.Abilities.Intelligence.ToString()]);
        }));
        _body.AddChild(roster);
        Label(CommandText.Resolve(Texts, "label.money"),new(280,428,96,32)); Label(CommandText.Resolve(Texts, "label.public-order"),new(280,468,96,32)); Label(CommandText.Resolve(Texts, "label.draft-growth"),new(280,508,96,32));
        Label($"{(Draft.Members.IsEmpty ? "" : Draft.Cost.ToString())}/{Draft.Money}",new(384,428,136,32));
        Label($"{Draft.Home.PublicOrder}/1000",new(384,468,96,32)); Label(Draft.DraftGrowth.ToString(),new(384,508,64,32));
        _body.AddChild(new GameMessageBar { Textures = Textures, Text = Texts.Resolve(CommandMenuSkin.HintKey("patrol")) });
        // Draw content surfaces above the command background and below its labels.
        var art = new PatrolFields { Textures = Textures, Field = _field, MouseFilter = MouseFilterEnum.Ignore };
        _body.AddChild(art); _body.MoveChild(art, 1);
    }
    private void CloseChild()
    { if (_child is not null) { RemoveChild(_child); _child.QueueFree(); _child = null; } Rebuild(); }
    private void Notice(string message)
    {
        var dialog = new GameDialog { Textures = Textures, Size = Size }; dialog.Closed += CloseChild;
        _child = dialog; AddChild(dialog); dialog.SetNotice(message);
    }
    private void SelectOfficers()
    {
        var selection = new CatalogSelection(Draft.Candidates, Draft.Members, Draft.SelectionLimit);
        var ability = OfficerCatalog.Pages[0];
        int[] columns = [0,3,8,1,2,4,5];
        CatalogRow[] Rows(int page) => OfficerCatalog.Read(Draft.View, Texts, page == 0 ? 0 : page-1, null, Draft.Home.Id).Rows
            .Where(r => Draft.Candidates.Contains(r.Key.Id)).OrderBy(r => Draft.Candidates.IndexOf(r.Key.Id))
            .Select(r => new CatalogRow(r.Key.Id, page == 0 ? columns.Select(c => r.Cells[c]).ToArray() : r.Cells.ToArray())).ToArray();
        var view = new CatalogView { Title = CommandText.Resolve(Texts, "select.officer"), HintText = Texts.Resolve(CommandMenuSkin.OfficerSelectionHintKey), Pages = [new(CommandText.Resolve(Texts, "title.patrol"), columns.Select(c => ability.Columns[c]).ToArray()), .. OfficerCatalog.Pages],
            ReadRows = Rows, Service = Service, Textures = Textures, Portrait = id => $"catalog-person-{id}", Selection = selection,
            CanConfirmSelection = ids => ids.Count * 50 <= Draft.Money,
            ConfirmSelection = ids => { if (Draft.SetMembers(ids)) CloseChild(); }, Closed = CloseChild };
        _child = view; AddChild(view);
    }
    public override void _GuiInput(InputEvent input)
    {
        if (_child is null && input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right }) Closed();
        AcceptEvent();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (_child is not null) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } || input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Closed(); }
    }
    public override void _ExitTree() { _text.Dispose(); _field.Dispose(); }
}

internal partial class PatrolFields : Control
{
    public TextureScope Textures { get; init; } = null!;
    public StyleBoxTexture Field { get; init; } = null!;
    public override void _Draw()
    {
        void Strip(string role, Rect2 rect) => SkinPainter.Strip(this, Textures[$"deployment-{role}-left"], Textures[$"deployment-{role}-middle"], Textures[$"deployment-{role}-right"], rect);
        Strip("facility",new(280,228,160,32));
        SkinPainter.Tile(this, Textures["deployment-divider"], new(280,268,464,5));
        for (int i=0;i<3;i++)
        {
            int y = 428+i*40, width = i==0?136:i==1?96:64;
            Strip("label",new(280,y,96,32));
            DrawStyleBox(Field,new(384,y,width,32));
            SkinPainter.Tile(this, Textures["deployment-field-rim"],new(384,y,width,4));
            SkinPainter.Tile(this, Textures["deployment-field-rim"],new(384,y+28,width,4));
        }
    }
}
