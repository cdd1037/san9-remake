using Godot;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Content.Interface;

namespace San9.Client.Godot;

/// <summary>Fixed-formation preparation shares the roster, officer card and numeric editor.</summary>
public partial class SpecialDeploymentView : FixedPage
{
    protected override Color PageBackground => new(0, 0, 0, .35f);
    public SpecialDeploymentDraft Draft { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action Proceed { get; init; } = null!;
    private bool Construction => Draft.Kind == SpecialDeploymentKind.Construction;
    private Vector2 Origin => Construction ? new(208, 208) : new(288, 256);
    private Control? _body, _child;
    private CommandWindow _window = null!;
    private StyleBoxTexture _field = null!;
    private readonly GameTextPresenter _text = new();
    private readonly List<(string Skin, Rect2 Bounds)> _strips = [];
    private readonly List<Rect2> _fields = [];
    private GameMessageBar _message = null!;
    private string Text(string role) => Texts.ResolveRich(SpecialDeploymentText.Key(role));
    private string Hint => Text(Construction ? "build" : "transport");
    private string AcceptHint => Text(Draft.Members.IsEmpty ? "need-officers" : Draft.Soldiers==0 ? "need-soldiers" :
        Construction && Draft.FacilityId is null ? "need-facility" : Construction ? "next" : "transport");
    public override void _Ready()
    {
        Size = new(1024, 768); ZIndex = 25; MouseFilter = MouseFilterEnum.Stop;
        _field = CommandPanelArt.Field(Textures, this);
        _message = new GameMessageBar { Textures=Textures,Text=Hint }; AddChild(_message); Rebuild();
    }
    private void Label(string value, Rect2 bounds) => CommandPanelArt.Label(_body!, _text, value, bounds);
    private void Field(string value, Rect2 bounds) { _fields.Add(bounds); Label(value, bounds); }
    private void Strip(string title, Rect2 bounds, string skin = "deployment-label")
    { _strips.Add((skin, bounds)); Label(title, bounds); }
    private OfficerCardData? Officer(int? id)
    {
        if (id is not int person) return null;
        var officer = Draft.View.Officers.Single(o => o.Id == person);
        return new(Texts.Resolve(officer.NameKey), $"catalog-person-{person}", officer.Abilities);
    }
    private void Rebuild()
    {
        if (_window is not null) { RemoveChild(_window); _window.QueueFree(); }
        if (_body is not null) { RemoveChild(_body); _body.QueueFree(); }
        _strips.Clear(); _fields.Clear();
        _body = new Control { Position = Origin, MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        _window = new CommandWindow { Texts = Texts, Textures = Textures, Title = Draft.Title, Position = Origin, Size = Construction ? new(608,352) : new(448,256),
            ShowBehindParent = true, Closed = Closed, Accepted = Proceed, CanAccept = Draft.CanProceed,
            HintChanged = hint => _message.SetMessage(hint??Hint), AcceptHint = AcceptHint, CancelHint = Text("cancel"),
            Help = () => Notice(Construction ? "选择执行武将、士兵与工作物，再选择建设地点。" : "选择执行武将和士兵，再选择己方目标设施。") };
        AddChild(_window);
        string home = "执行设施　" + Draft.View.Settlements.Single(s => s.Id == Draft.Home.Id).ResolveName(Texts);
        Strip(home, new(0,0,home.Length*16+32,32), "deployment-facility");
        var officersButton = _window.ActionButton("执行武将", "edit", new(0,48,104,40), SelectOfficers);
        officersButton.Disabled = Draft.Candidates.Length <= 1; _window.BindHint(officersButton,Text("officers"));
        int soldierY = Construction ? 200 : 176;
        var soldiersButton = _window.ActionButton("士兵", "edit", new(0,soldierY,104,40), SelectSoldiers);
        soldiersButton.Disabled = Draft.Members.IsEmpty; _window.BindHint(soldiersButton,Text(Draft.Members.IsEmpty ? "need-officers" : "soldiers"));
        Field($"{(Draft.Members.IsEmpty ? "" : Draft.Soldiers.ToString())}/{Draft.Home.Soldiers}", new(104,soldierY,120,32));
        if (Construction)
        {
            var roster = new OfficerRosterPanel { Textures = Textures, Service = Service, Position = new(104,48),
                Columns = [new("武将",80),new("",424)], ShowPortrait = false };
            roster.SetEntries(Draft.Members.Select(id => Officer(id)!).Select(officer => new OfficerRosterEntry(officer, [officer.Name, ""])));
            _body.AddChild(roster);
            Strip("大将", new(192,56,48,24), "deployment-mode");
        }
        _body.AddChild(new OfficerCard { Textures = Textures, Service = Service, Officer = Officer(Draft.CommanderId),
            Position = Construction ? new(248,56) : new(104,48) });
        var properties = new OfficerPropertyList { Textures = Textures, LabelSkinPrefix = "deployment-mode", Position = Construction ? new(464,56) : new(312,48) };
        var commander = Draft.CommanderId is int person ? Draft.View.World.Officers.Single(o => o.Id == person) : null;
        properties.SetProperties([new("爵位", commander is null ? "" : Draft.View.Information.Offices.FirstOrDefault(o => o.Id == commander.OfficeId) is { } office ? Texts.Resolve(office.NameKey) : "无",80),
            new("指挥", commander is null ? "" : Draft.CommandCapacity(commander.Id).ToString(),56)]);
        _body.AddChild(properties);
        if (Construction)
        {
            var facilityButton = _window.ActionButton("工作物", "edit", new(0,240,104,40), SelectFacility);
            facilityButton.Disabled = !Draft.AvailableFacilities().Any();
            _window.BindHint(facilityButton,Text(Draft.Members.IsEmpty ? "need-officers" : facilityButton.Disabled ? "no-facility" : "facility"));
            Field(Draft.FacilityId is int id ? Draft.FacilityName(id,Texts) : "", new(104,240,80,32));
            Strip("资金",new(0,280,96,32)); Field($"{(Draft.FacilityId is null ? "" : Draft.Cost.ToString())}/{Draft.Money}",new(104,280,120,32));
        }
        int moraleY = Construction ? 320 : 216;
        Strip("士气",new(0,moraleY,96,32)); Field(Draft.Home.Morale.ToString(),new(104,moraleY,40,32));
        _message.SetMessage(Hint); QueueRedraw();
    }
    private void OpenChild(Control child)
    {
        _child = child;
        if (child is CatalogView)
        {
            _window.Hide(); _body!.Hide(); _message.Hide(); QueueRedraw();
        }
        AddChild(child);
    }
    private void CloseChild()
    { if (_child is not null) { RemoveChild(_child); _child.QueueFree(); _child = null; } _message.Show(); Rebuild(); }
    private void Notice(string message)
    {
        var dialog = new GameDialog { Textures = Textures, Size = new(1024,768) };
        dialog.Closed += CloseChild; OpenChild(dialog); dialog.SetNotice(message);
    }
    private void SelectSoldiers() => OpenChild(new NumberInputDialog { State = new(1,Draft.MaximumSoldiers,Draft.Soldiers),
        Textures = Textures, Subject = "士兵", Confirmed = value => { Draft.SetSoldiers(value); CloseChild(); }, Closed = CloseChild });
    private void SelectOfficers()
    {
        var picker = new CatalogView { Title = "选择武将", HintText = Text("need-officers"), CloseTitle = "中止",
            Textures = Textures, Service = Service, Pages = Construction ? ConstructionOfficerCatalog.Pages : TransportOfficerCatalog.Pages,
            ReadRows = page => Construction ? ConstructionOfficerCatalog.Read(Draft, Texts, page) : TransportOfficerCatalog.Read(Draft, Texts, page),
            Portrait = Construction ? null : id => $"catalog-person-{id}", Closed = CloseChild,
            Selection = Construction ? new CatalogSelection(Draft.Candidates,Draft.Members,Draft.MemberLimit) : null,
            ConfirmSelection = ids => { if (Draft.SetMembers(ids)) CloseChild(); },
            Activated = Construction ? null : id => { if (Draft.SetMembers([id])) CloseChild(); } };
        OpenChild(picker);
    }
    private void SelectFacility()
    {
        OpenChild(new CatalogView { Title = "选择工作物", HintText = Text("facility-catalog"), CloseTitle = "中止", Textures = Textures, Service = Service,
            Pages = ConstructionFacilityCatalog.Pages,
            ReadRows = _ => ConstructionFacilityCatalog.Read(Draft, Texts),
            Portrait = id => $"catalog-facility-{id}", PortraitSize = new(128,128),
            InitialCurrentId = Draft.FacilityId, Activated = id => { if (Draft.SetFacility(id)) CloseChild(); }, Closed = CloseChild });
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree() || _child is not null) return;
        if (input is InputEventKey { Pressed:true, Echo:false, Keycode:Key.Escape }) { GetViewport().SetInputAsHandled(); Closed(); }
        if (input is InputEventMouseButton { ButtonIndex:MouseButton.Right } mouse)
        { GetViewport().SetInputAsHandled(); if (mouse.Pressed) Closed(); }
    }
    public override void _Draw()
    {
        if (_child is CatalogView) return;
        foreach (var (skin,bounds) in _strips) CommandPanelArt.Strip(this,Textures,skin,new(Origin+bounds.Position,bounds.Size));
        foreach (var bounds in _fields)
        {
            var rect = new Rect2(Origin+bounds.Position,bounds.Size); DrawStyleBox(_field,rect);
            SkinPainter.Tile(this,Textures.Get("deployment-field-rim", this),new(rect.Position,new(rect.Size.X,4)));
            SkinPainter.Tile(this,Textures.Get("deployment-field-rim", this),new(rect.Position+new Vector2(0,rect.Size.Y-4),new(rect.Size.X,4)));
        }
        DrawLine(Origin+new Vector2(0,40),Origin+new Vector2(Construction?608:448,40),new Color("755635"),2);
    }
    public override void _ExitTree() { _text.Dispose(); _field.Dispose(); }
}
