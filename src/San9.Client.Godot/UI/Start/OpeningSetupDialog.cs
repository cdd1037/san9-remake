using Godot;
using San9.Application.Authoring;
using San9.Application.Presentation;
using San9.Content.Authoring;
using San9.Content.Interface;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Startup assignments are transactional; only confirmation publishes to the draft.</summary>
public partial class OpeningSetupDialog : ModalView
{
    public NewGameDraft Draft { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public ForceSelectionPresentation Definition { get; init; } = null!;
    public OpeningMapDefinition Geography { get; init; } = null!;
    public StartMenuSprite Help { get; init; } = null!;
    public StartMenuSprite Close { get; init; } = null!;
    public Action QuitRequested { get; init; } = null!;
    public bool CreateForces { get; init; }
    public bool CancelDeployment { get; init; }
    public Action Closed { get; init; } = null!;
    public Action Changed { get; init; } = null!;
    private TextureScope _art = null!, _skin = null!;
    private readonly GameTextPresenter _text = new();
    private readonly int?[] _rulers = new int?[NewGameSetup.MaximumCreatedForces];
    private readonly int?[] _cities = new int?[NewGameSetup.MaximumCreatedForces];
    private readonly List<(Label Ruler, Label City, AsyncPortrait Face, StartMenuButton Erase)> _slots = [];
    private StartMenuButton _confirm = null!;
    private Label _hint = null!;
    private CatalogView? _list;
    private OpeningCityPicker? _cityPicker;
    private GameDialog? _message;
    private readonly HashSet<int> _people = [];
    private Dictionary<int, OfficerDefaults> _officers = null!;

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _officers = Draft.Candidates.ToDictionary(p => p.Id);
        _art = Service.CreateScope(Service.LibraryAssets);
        if (CreateForces) _art.Request(Definition.NewForces.Assets.Concat([Help.Asset, Close.Asset]));
        var skin = Service.Roles.ToDictionary();
        foreach (var (role, asset) in Definition.MapSkin) skin[role] = asset;
        _skin = Service.CreateScope(skin);
        _skin.RequestPrefixes("opening-", "catalog-", "information-", "dialog-");
        if (CreateForces) BuildForces(); else ChoosePeople();
    }
    private void BuildForces()
    {
        for (int i = 0; i < Draft.Setup.Forces.Length; i++)
        { _rulers[i] = Draft.Setup.Forces[i].RulerId; _cities[i] = Draft.Setup.Forces[i].CityId; }
        OpeningPageHeading.Add(this, _text, "创作新势力");
        StartMenuButton? first = null;
        for (int i = 0; i < _rulers.Length; i++)
        {
            int slot = i;
            var layout = Definition.NewForces.Slots[i];
            Label($"新势力 {i + 1}", Point(layout.Label), new(160, 24), 18, new("00f878"));
            var ruler = Button(layout.Ruler, "君主", () => ChooseRuler(slot), hint: "选择担任新势力君主的武将。");
            first ??= ruler;
            Button(layout.City, "都市", () => ChooseCity(slot), hint: "选择新势力的所在都市。");
            var erase = Button(layout.Erase, "消去", () => { _rulers[slot] = null; _cities[slot] = null; RefreshForces(); }, hint: "消去这个新势力。");
            var name = Label("", Point(layout.RulerName), new(96, 24), 24, new("f0f0f0"));
            var city = Label("", Point(layout.CityName), new(96, 24), 24, new("f0f0f0"));
            var face = new AsyncPortrait { Service = Service, View = San9.Content.Images.PortraitView.Face,
                Position = Point(layout.Portrait), Size = new(64, 80), MouseFilter = MouseFilterEnum.Ignore };
            AddChild(face); _slots.Add((name, city, face, erase));
        }
        _confirm = Button(Definition.NewForces.Confirm, "决定", () => Apply(() => Draft.SetForces(CurrentForces())), GameButtonTextRole.ConfirmYes);
        Button(Definition.NewForces.Back, "返回", Closed, GameButtonTextRole.ConfirmNo);
        Button(Help, "", () => { }); Button(Close, "", QuitRequested);
        _hint = Label("请创作新势力。", new(0, 736), new(1024, 24));
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        RefreshForces();
        first!.GrabFocus();
    }
    private void RefreshForces()
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            var person = _rulers[i] is int id ? _officers[id] : null;
            _slots[i].Ruler.Text = person?.Name ?? "";
            _slots[i].City.Text = _cities[i] is int city ? CityName(city) : "";
            _slots[i].Face.Role = person?.PortraitKey;
            _slots[i].Erase.Disabled = person is null && _cities[i] is null;
        }
        bool complete = Enumerable.Range(0, _rulers.Length).All(i => (_rulers[i] is null) == (_cities[i] is null));
        _confirm.Disabled = !complete || CurrentForces().SequenceEqual(Draft.Setup.Forces);
    }
    private IEnumerable<CreatedForce> CurrentForces() => Enumerable.Range(0, _rulers.Length).Where(i => _rulers[i] is not null)
        .Select(i => new CreatedForce(_rulers[i]!.Value, _cities[i]!.Value));
    private void ChooseRuler(int slot)
    {
        var people = _officers.Values.Where(p => NewGameComposer.CanDeploy(p, Draft.Source) && !_rulers.Where((_, i) => i != slot).Contains(p.Id)).ToArray();
        PeopleList("选择武将", people, _rulers[slot] is int id ? [id] : [], 1, ids =>
        {
            _rulers[slot] = _officers[ids.Single()].Id; CloseList(); RefreshForces();
            if (_cities[slot] is null) ChooseCity(slot);
        }, CloseList);
    }
    private void ChooseCity(int slot) => ChooseCity("选择都市", Draft.Source.Settlements.Where(s => s.OwnerId is null &&
        !_cities.Where((_, i) => i != slot).Contains(s.Id)), _cities[slot], city =>
        {
            _cities[slot] = city; CloseCityPicker(); RefreshForces();
            if (_rulers[slot] is null) ChooseRuler(slot);
        }, CloseCityPicker);
    private void ChoosePeople()
    {
        var people = _officers.Values.Where(p => CancelDeployment ? Draft.Setup.Placements.Any(a => a.OfficerId == p.Id) :
            NewGameComposer.CanDeploy(p, Draft.Source) && !Draft.Setup.Forces.Any(f => f.RulerId == p.Id) &&
            !Draft.Setup.Placements.Any(a => a.OfficerId == p.Id)).ToArray();
        PeopleList("选择武将", people, _people, Math.Max(1, people.Length), ids =>
        {
            _people.Clear(); _people.UnionWith(ids); CloseList();
            if (CancelDeployment) Apply(() => Draft.CancelDeployment(_people.Select(id => _officers[id].Id)));
            else ChooseDestination();
        }, Closed);
    }
    private void ChooseDestination() => ChooseCity("选择都市", Draft.Compose().Settlements, null, city =>
    {
        CloseCityPicker();
        int? owner = Draft.Compose().Settlements.Single(s => s.Id == city).OwnerId;
        void Deploy(bool join) => Apply(() => Draft.Deploy(_people.Select(id => _officers[id].Id), city, join));
        if (owner is null) { Deploy(false); return; }
        _message = new GameDialog { Textures = _skin, Size = Size };
        _message.SetRichMessage(Definition.JoinForcePrompt);
        _message.Confirmed += () => { CloseMessage(); Deploy(true); };
        _message.Dismissed += () => { CloseMessage(); Deploy(false); };
        AddChild(_message);
    }, () => { CloseCityPicker(); ChoosePeople(); });
    private void PeopleList(string title, OfficerDefaults[] people, IEnumerable<int> selected, int limit,
        Action<IReadOnlySet<int>> confirmed, Action closed)
    {
        bool single = CreateForces;
        var pages = OpeningOfficerCatalog.Pages(Draft.Source, Draft.Text);
        _list = new CatalogView { Title = title, HintText = single ? "选择担任新势力君主的武将。" : "请选择武将后决定。",
            Service = Service, Textures = _skin, CloseTitle = "中止", TitleBounds = OpeningWindowPainter.TitleBounds, TabStripFollowsPage = true, MinimumBodyWidth = 588,
            WindowPainter = (canvas, body) => OpeningWindowPainter.Draw(canvas, _skin, body, pairedActions: !single),
            Pages = pages, ReadRows = page => people.Select(p => new CatalogRow(p.Id,
                pages[page].Columns.Select(c => OpeningOfficerCatalog.Cell(p, c.Id)).ToArray())).ToArray(),
            Portrait = id => _officers[id].PortraitKey, InitialCurrentId = selected.Cast<int?>().FirstOrDefault(),
            Selection = single ? null : new(people.Select(p => p.Id), selected, limit), ConfirmSelection = confirmed,
            Activated = single ? id => confirmed(new HashSet<int> { id }) : null, Closed = closed };
        AddChild(_list);
    }
    private void ChooseCity(string title, IEnumerable<SettlementInitialState> settlements, int? selected, Action<int> confirmed, Action closed)
    {
        _cityPicker = new OpeningCityPicker { Title = title, Draft = Draft, Geography = Geography, Textures = _skin, Service = Service,
            CreateForceHint = CreateForces,
            Candidates = settlements.Where(s => s.Kind == SettlementKind.City && !s.IsDormant && s.CanHostPersons).Select(s => s.Id).ToArray(),
            InitialCurrentId = selected, Confirmed = confirmed, Closed = closed };
        AddChild(_cityPicker);
    }
    private void CloseCityPicker() { RemoveChild(_cityPicker!); _cityPicker!.QueueFree(); _cityPicker = null; }
    private void CloseList() { RemoveChild(_list!); _list!.QueueFree(); _list = null; }
    private void CloseMessage() { RemoveChild(_message!); _message!.QueueFree(); _message = null; }
    private void Apply(Action action)
    {
        try { action(); Changed(); }
        catch (InvalidDataException error)
        {
            _message = new GameDialog { Textures = _skin, Size = Size };
            _message.SetNotice(error.Message);
            _message.Closed += () => { CloseMessage(); if (!CreateForces) ChoosePeople(); };
            AddChild(_message);
        }
    }
    private string CityName(int id) => Draft.Text.Resolve(Draft.Source.Settlements.Single(s => s.Id == id).NameKey);
    private static Vector2 Point(ScenarioPreviewPoint p) => new(p.X, p.Y);
    private Label Label(string title, Vector2 at, Vector2 size, int font = 16, Color? color = null)
    {
        var label = new Label { Text = title, Position = at, Size = size, ClipText = true, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label); _text.ApplyAtGlyphCell(label, new(font, color ?? Colors.White, GameTextEffect.Outline, Colors.Black), at); return label;
    }
    private StartMenuButton Button(StartMenuSprite sprite, string title, Action action, GameButtonTextRole? role = null, string? hint = null)
    {
        var image = Service.LibraryAssets[sprite.Asset];
        var button = new StartMenuButton { Textures = _art, Sprite = sprite, Title = title, TextRole = role,
            TextOffset = new(24, 16), TextSize = new(84, 24), Position = new(sprite.X, sprite.Y), Size = new(image.Width / sprite.Columns, image.Height) };
        button.Pressed += action;
        if (hint is not null)
        {
            button.MouseEntered += () => _hint.Text = hint;
            button.FocusEntered += () => _hint.Text = hint;
        }
        AddChild(button); return button;
    }
    public override void _Input(InputEvent input)
    {
        if (_list is not null || _cityPicker is not null || _message is not null || !IsVisibleInTree()) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } || input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Closed(); }
    }
    public override void _Draw()
    {
        if (!CreateForces) return;
        if (!_art.IsReady) { _art.BindRedraw(this); return; }
        foreach (var sprite in Definition.NewForces.Foreground.Concat(Definition.NewForces.Slots.Select(s => s.Frame)))
            DrawTexture(_art.Get(sprite.Asset, this), new(sprite.X, sprite.Y));
    }
    public override void _ExitTree() { _art.Dispose(); _skin.Dispose(); _text.Dispose(); }
}
