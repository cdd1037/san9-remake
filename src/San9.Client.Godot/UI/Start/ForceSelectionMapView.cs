using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;

namespace San9.Client.Godot;

/// <summary>Transactional force picker; map and list edit the same draft until confirmed.</summary>
public partial class ForceSelectionMapView : ModalView
{
    public OpeningMapDefinition Definition { get; init; } = null!;
    public ScenarioForceSelection Scenario { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public IReadOnlyDictionary<string, San9.Content.Images.ImageAsset> Skin { get; init; } = null!;
    public IReadOnlySet<int> InitialSelection { get; init; } = new HashSet<int>();
    public Action Closed { get; init; } = null!;
    public Action<IReadOnlySet<int>> Confirmed { get; init; } = null!;
    public CatalogSelection Selection { get; private set; } = null!;
    private TextureScope _textures = null!;
    private TextureScope _mapTextures = null!;
    private OpeningMapView _map = null!;
    private readonly GameTextPresenter _text = new();
    private readonly List<Label> _values = [];
    private InformationActionButton _confirm = null!;
    private CatalogBulkButton _clear = null!;
    private CatalogView? _list;
    private static readonly int[] FieldY = [132, 180, 212, 244, 292, 324, 372, 404, 436, 484, 516, 548, 580];
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        Selection = new(Scenario.Forces.Select(f => f.Id), InitialSelection, 8);
        var assets = Service.Roles.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (role, asset) in Skin) assets[role] = asset;
        _textures = Service.CreateScope(assets);
        _textures.RequestPrefixes("information-", "catalog-", "opening-");
        _mapTextures = Service.CreateScope(Service.LibraryAssets); _mapTextures.Request(Definition.Assets);
        _map = new OpeningMapView { Name = "Map", Definition = Definition, CityColors = Scenario.CityColors, Forces = Scenario.Forces, Textures = _mapTextures,
            SelectionTextures = _textures,
            Position = new(108, 112), Selected = Selection.Selected, Hovered = Summary, Activated = id => { Selection.Toggle(id); Refresh(); } };
        AddChild(_map);
        Label("选择势力", new(84, 40), new(128, 32), 24);
        string[] labels = ["势力", "官爵", "信望", "兵法", "都市", "在任", "人口", "兵役人口", "士兵", "资金", "兵粮", "资金收支", "兵粮收支"];
        for (int i = 0; i < labels.Length; i++)
        {
            Label(labels[i], new(710, FieldY[i]), new(98, 24));
            _values.Add(Label("", new(822, FieldY[i]), new(88, 24)));
        }
        _confirm = Button("决定", new(720, 690), new(80, 48), Confirm, "catalog-accept-");
        _confirm.Name = "Confirm";
        Button("中止", new(808, 690), new(80, 48), Closed).Name = "Cancel";
        Button("一览", new(524, 674), new(112, 32), ShowList, mapToggle: true).Name = "List";
        _clear = new CatalogBulkButton { Name = "Clear", Title = "总括解除", Textures = _textures, Position = new(256, 674), Size = new(112, 32) };
        _clear.Pressed += () => { Selection.Clear(); Refresh(); }; AddChild(_clear);
        var close = new TextureButton { Position = new(928, 62), TextureNormal = _textures.Get("information-action-close", this),
            TextureHover = _textures.Get("information-action-close-1", this), TexturePressed = _textures.Get("information-action-close-2", this) };
        close.Pressed += Closed; AddChild(close);
        AddChild(new TextureRect { Position = new(896, 38), Texture = _textures.Get("catalog-help", this), MouseFilter = MouseFilterEnum.Ignore });
        Label("请选择势力。", new(0, 736), new(1024, 24)); Refresh();
    }
    private void Confirm() { if (Selection.Selected.Count > 0) Confirmed(Selection.Selected); }
    private void Refresh()
    {
        _confirm.Disabled = Selection.Selected.Count == 0; _clear.Disabled = Selection.Selected.Count == 0; _map.QueueRedraw();
    }
    private void Summary(int? id)
    {
        var f = Scenario.Forces.FirstOrDefault(f => f.Id == id);
        string[] fields = ["name", "rank", "trust", "tactics", "cities", "officers", "population", "draft", "soldiers", "money", "food", "money-balance", "food-balance"];
        string[] values = fields.Select(field => f is null ? "" : OpeningForceCatalog.Cell(f, field).Text).ToArray();
        for (int i = 0; i < _values.Count; i++) _values[i].Text = values[i];
    }
    private Label Label(string title, Vector2 p, Vector2 size, int font = 16)
    {
        var label = new Label { Text = title, Position = p, Size = size, ClipText = true,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label); _text.Apply(label, new(font, Colors.White, GameTextEffect.Outline, Colors.Black)); return label;
    }
    private InformationActionButton Button(string title, Vector2 p, Vector2 size, Action action, string? prefix = null, bool mapToggle = false)
    {
        var button = new InformationActionButton { Title = title, Textures = _textures, Position = p, Size = size,
            SkinPrefix = prefix, MapToggle = mapToggle, TextRole = mapToggle ? GameButtonTextRole.MapMode : prefix is null ? GameButtonTextRole.ConfirmNo : GameButtonTextRole.ConfirmYes };
        button.Pressed += action; AddChild(button); return button;
    }
    private void ShowList()
    {
        if (_list is not null) return;
        foreach (var child in GetChildren().OfType<CanvasItem>()) child.Hide();
        _list = new CatalogView { Name = "ForceList", Title = "选择势力", HintText = "请选择势力。", CloseTitle = "中止",
            Service = Service, Textures = _textures, Selection = Selection, ConfirmSelection = selected => Confirmed(selected),
            Closed = Closed, ShowMap = CloseList,
            WindowPainter = (canvas, body) => OpeningWindowPainter.Draw(canvas, _textures, body), MinimumBodyWidth = 788, TitleWidth = 208, MapButtonWidth = 112,
            Pages = OpeningForceCatalog.Pages,
            ReadRows = page => Scenario.Forces.Select(f => new CatalogRow(f.Id, OpeningForceCatalog.Pages[page].Columns.Select(c => OpeningForceCatalog.Cell(f, c.Id)).ToArray())).ToArray() };
        AddChild(_list); QueueRedraw();
    }
    private void CloseList()
    {
        RemoveChild(_list!); _list!.QueueFree(); _list = null;
        foreach (var child in GetChildren().OfType<CanvasItem>()) child.Show();
        Refresh(); QueueRedraw();
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree() || _list is not null) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Closed(); }
    }
    public override void _Draw()
    {
        DrawRect(new(0, 0, 1024, 768), new(0, 0, 0, .45f));
        if (_list is not null) return;
        if (!_textures.IsReady) { _textures.BindRedraw(this); return; }
        OpeningWindowPainter.Draw(this, _textures, new(56, 30, 912, 708));
        DrawTexture(_textures.Get("opening-panel", this), new(88, 92));
    }
    public override void _ExitTree() { _textures.Dispose(); _mapTextures.Dispose(); _text.Dispose(); }
}
