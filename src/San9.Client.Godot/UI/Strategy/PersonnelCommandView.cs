using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Client.Godot;

public partial class PersonnelCommandView : FixedPage
{
    protected override Color PageBackground => new(0, 0, 0, .35f);
    public PersonnelDraft Draft { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action Submit { get; init; } = null!;
    public Action ChooseMap { get; init; } = null!;
    public Action SelectionChanged { get; init; } = null!;
    public bool HasChild => _child is not null;
    private Control? _body, _child;
    private readonly GameTextPresenter _text = new();
    private Rect2 Bounds => Draft.Kind switch { "summon" => new(216,222,592,324), "move" => new(304,248,416,272),
        "search" => new(304,280,416,208), _ => new(288,236,448,296) };
    public override void _Ready() { Size = new(1024,768); MouseFilter = MouseFilterEnum.Stop; Rebuild(); }
    public override void _Process(double delta) { if (!Draft.IsCurrent) Closed(); }
    private void Label(string value, Rect2 bounds) => CommandPanelArt.Label(_body!, _text, value, bounds);
    private OfficerCardData Card(int id)
    {
        var person = Draft.View.Officers.Single(p => p.Id == id);
        return new(Texts.Resolve(person.NameKey), $"catalog-person-{id}", person.Abilities);
    }
    private void Person(int? id, Vector2 at)
    {
        _body!.AddChild(new OfficerCard { Service = Service, Textures = Textures, Position = at, Officer = id is int person ? Card(person) : null });
    }
    private void Properties(Vector2 at, params OfficerProperty[] values)
    {
        var properties = new OfficerPropertyList { Textures = Textures, LabelSkinPrefix = "deployment-mode", Position = at };
        properties.SetProperties(values);
        _body!.AddChild(properties);
    }
    public void Rebuild()
    {
        if (_body is not null) { RemoveChild(_body); _body.QueueFree(); }
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        var bounds = Bounds; var at = bounds.Position;
        var window = new CommandWindow { Texts = Texts, Textures = Textures, Title = CommandText.Resolve(Texts, "title." + Draft.Kind), Position = at, Size = bounds.Size,
            CanAccept = Draft.CanExecute, Accepted = Submit, Closed = Closed, Help = () => Notice(Texts.Resolve("ui.command." + Draft.Kind)) };
        _body.AddChild(window);
        string source = CommandText.Resolve(Texts, "value.source", Draft.PlaceName(Texts, Draft.Home.Id));
        float sourceWidth = source.Sum(c => c < 128 ? 8 : 16) + 32;
        _body.AddChild(new PersonnelCommandFields { Textures = Textures, Bounds = bounds, Kind = Draft.Kind, SourceWidth = sourceWidth });
        Label(source, new(at, new(sourceWidth,32)));
        if (Draft.Kind != "summon")
        {
            int? days = Draft.Members.IsEmpty ? null : Draft.ArrivalDays(Draft.Members[0]);
            _body.AddChild(new CommandArrivalField { Textures = Textures, Position = at + new Vector2(256,0),
                Size = new(152,32), Text = CommandArrivalField.FormatDays(Texts, days) });
        }
        if (Draft.Kind is "summon" or "move")
        {
            if (Draft.Kind == "move")
            {
                window.ActionButton(CommandText.Resolve(Texts, "label.target-facility"), "edit", new(0,48,104,40), ChooseMap).Disabled = Draft.Targets.Length < 2;
                Label(Draft.TargetId is int target ? Draft.DestinationName(Texts, target) : "", new(at + new Vector2(104,48), new(144,32)));
            }
            int y = Draft.Kind == "summon" ? 48 : 88;
            window.ActionButton(CommandText.Resolve(Texts, "label.actor"), "edit", new(0,y,104,40), SelectOfficers).Disabled = Draft.Candidates.Length < 2;
            var roster = new OfficerRosterPanel { Service = Service, Textures = Textures, Position = at + new Vector2(104,y),
                VisibleRows = Draft.Kind == "summon" ? 10 : 5, ShowPagination = true,
                PaginationSpacing = Draft.Kind == "summon" ? 2 : 4,
                PaginationTop = Draft.Kind == "summon" ? 4 : 8,
                PaginationLabelTop = Draft.Kind == "summon" ? 10 : 12,
                CountGap = Draft.Kind == "summon" ? 70 : 68,
                Columns = Draft.Kind == "summon" ? [new(CommandText.Resolve(Texts, "label.officer"),80), new(CommandText.Resolve(Texts, "label.location"),96), new(CommandText.Resolve(Texts, "label.arrival"),96)] : [new(CommandText.Resolve(Texts, "label.officer"),80)] };
            roster.SetEntries(Draft.Members.Select(id => new OfficerRosterEntry(Card(id), Draft.Kind == "summon"
                ? [Card(id).Name, Draft.PlaceName(Texts,Draft.View.Officers.Single(p => p.Id == id).SettlementId!.Value), CommandText.Resolve(Texts, "value.days", Draft.ArrivalDays(id))]
                : [Card(id).Name])));
            _body.AddChild(roster);
        }
        else if (Draft.Kind == "search")
        {
            window.ActionButton(CommandText.Resolve(Texts, "label.target-region"), "edit", new(0,48,104,40), ChooseMap).Disabled = Draft.Targets.Length < 2;
            Label(Draft.TargetId is int target ? Draft.PlaceName(Texts,target) : "", new(at + new Vector2(104,48), new(128,32)));
            window.ActionButton(CommandText.Resolve(Texts, "label.actor"), "edit", new(0,88,104,40), SelectOfficers).Disabled = Draft.Candidates.Length < 2;
            Person(Draft.Members.IsEmpty ? null : Draft.Members[0], at + new Vector2(104,88));
            Properties(at + new Vector2(312,88), new OfficerProperty(CommandText.Resolve(Texts, "label.politics"), Draft.Members.IsEmpty ? "" : Card(Draft.Members[0]).Abilities.Politics.ToString(), 40));
        }
        else
        {
            window.ActionButton(CommandText.Resolve(Texts, "label.target-officer"), "edit", new(0,48,104,40), SelectRecruit).Disabled = Draft.Targets.Length < 2;
            Person(Draft.TargetId, at + new Vector2(104,48));
            var targetPerson = Draft.View.Officers.SingleOrDefault(p => p.Id == Draft.TargetId);
            Properties(at + new Vector2(312,48),
                new(CommandText.Resolve(Texts, "label.force"), targetPerson?.OwnerId is int owner ? Texts.Resolve(Draft.View.Forces.Single(f => f.Id == owner).NameKey) : targetPerson is null ? "" : "—", 80),
                new(CommandText.Resolve(Texts, "label.loyalty"), targetPerson?.OwnerId is null ? "" : Math.Min(100,Draft.View.World.Officers.Single(p => p.Id == targetPerson.Id).Loyalty).ToString(), 40));
            window.ActionButton(CommandText.Resolve(Texts, "label.actor"), "edit", new(0,176,104,40), SelectOfficers).Disabled = Draft.Candidates.Length < 2;
            Person(Draft.Members.IsEmpty ? null : Draft.Members[0], at + new Vector2(104,176));
            Properties(at + new Vector2(312,176), new OfficerProperty(CommandText.Resolve(Texts, "label.politics"), Draft.Members.IsEmpty ? "" : Card(Draft.Members[0]).Abilities.Politics.ToString(), 40));
        }
        _body.AddChild(new GameMessageBar { Textures = Textures, Text = Draft.Kind == "employ" ? CommandText.Resolve(Texts, "hint.employ") : Texts.Resolve(CommandMenuSkin.HintKey(Draft.Kind)) });
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
        bool multiple = Draft.Kind is "summon" or "move";
        _child = new CatalogView { Title = CommandText.Resolve(Texts, "select.officer"), HintText = Texts.Resolve(CommandMenuSkin.OfficerSelectionHintKey), Service = Service, Textures = Textures,
            CloseTitle = CommandText.Resolve(Texts, "action.cancel"), Pages = PersonnelCatalog.OfficerPages(Draft,Texts), ReadRows = page => PersonnelCatalog.Officers(Draft,Texts,page), Portrait = id => $"catalog-person-{id}",
            InitialCurrentId = Draft.Members.IsEmpty ? null : Draft.Members[0],
            Selection = multiple ? new(Draft.Candidates,Draft.Members,Draft.SelectionLimit) : null, CanConfirmSelection = ids => Draft.CanSelect(ids),
            Activated = id => { if (Draft.SetMembers([id])) { CloseChild(); SelectionChanged(); } },
            ConfirmSelection = ids => { if (Draft.SetMembers(ids)) { CloseChild(); SelectionChanged(); } }, Closed = CloseChild };
        AddChild(_child);
    }
    private void SelectRecruit()
    {
        _child = new CatalogView { Title = CommandText.Resolve(Texts, "select.officer"), HintText = CommandText.Resolve(Texts, "hint.employ"), CloseTitle = CommandText.Resolve(Texts, "action.cancel"), Service = Service, Textures = Textures,
            Pages = PersonnelCatalog.OfficerPages(Draft,Texts,targets:true), ReadRows = page => PersonnelCatalog.Officers(Draft,Texts,page,targets:true),
            Portrait = id => $"catalog-person-{id}", InitialCurrentId = Draft.TargetId,
            Activated = id => { if (Draft.SetTarget(id)) { CloseChild(); SelectionChanged(); } }, Closed = CloseChild };
        AddChild(_child);
    }
    public void ShowTargetList(Action cancelled, Action<int> selected)
    {
        if (_child is not null) return;
        Show(); _body!.Hide();
        _child = new CatalogView { Title = Draft.Kind == "search" ? CommandText.Resolve(Texts, "select.region") : CommandText.Resolve(Texts, "select.facility"), Service = Service, Textures = Textures,
            CloseTitle = CommandText.Resolve(Texts, "action.cancel"), HintText = Draft.Kind == "search" ? CommandText.Resolve(Texts, "hint.search-region") : CommandText.Resolve(Texts, "hint.target-facility"),
            Pages = PersonnelCatalog.TargetPages(Draft,Texts), ReadRows = page => PersonnelCatalog.Targets(Draft,Texts,page),
            InitialCurrentId = Draft.TargetId, Activated = id => { CloseChild(); selected(id); }, Closed = () => { CloseChild(); cancelled(); },
            ShowMap = () => { CloseChild(); cancelled(); } };
        AddChild(_child);
    }
    public override void _GuiInput(InputEvent input)
    {
        if (_child is null && input is InputEventMouseButton { Pressed:false, ButtonIndex:MouseButton.Right }) Closed();
        AcceptEvent();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (_child is not null || !Visible) return;
        if (input is InputEventKey { Pressed:true,Echo:false,Keycode:Key.Escape }) { GetViewport().SetInputAsHandled(); Closed(); }
    }
    public override void _ExitTree() => _text.Dispose();
}

internal partial class PersonnelCommandFields : Control
{
    public TextureScope Textures { get; init; } = null!;
    public Rect2 Bounds { get; init; }
    public string Kind { get; init; } = "";
    public float SourceWidth { get; init; }
    private StyleBoxTexture _field = null!;
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; _field = CommandPanelArt.Field(Textures); }
    public override void _Draw()
    {
        var at = Bounds.Position;
        CommandPanelArt.Strip(this,Textures,"deployment-facility",new(at,new(SourceWidth,32)));
        SkinPainter.Tile(this,Textures["deployment-divider"],new(at+new Vector2(0,40),new(Bounds.Size.X,5)));
        if (Kind is "search" or "move") Field(new(at+new Vector2(104,48),new(Kind == "search" ? 128 : 144,32)));
    }
    private void Field(Rect2 bounds)
    {
        DrawStyleBox(_field,bounds);
        SkinPainter.Tile(this,Textures["deployment-field-rim"],new(bounds.Position,new(bounds.Size.X,4)));
        SkinPainter.Tile(this,Textures["deployment-field-rim"],new(bounds.Position+new Vector2(0,bounds.Size.Y-4),new(bounds.Size.X,4)));
    }
    public override void _ExitTree() => _field.Dispose();
}
