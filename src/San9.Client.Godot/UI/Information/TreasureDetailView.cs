using Godot;
using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Client.Godot;

public partial class TreasureDetailView : FixedPage, IInformationPage
{
    protected override Color PageBackground => Colors.Black;
    public DetailSelectionState State { get; init; } = null!;
    public Func<int, TreasureDetailData> Read { get; init; } = null!;
    public Func<int, string> ReadName { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Action<int> CurrentChanged { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    private GameMessageBar _message = null!;
    private readonly GameTextPresenter _text = new();
    private DetailSidebar _sidebar = null!;
    private Control _body = null!;
    private TreasureDetailData _data = null!;

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        _sidebar = new DetailSidebar { State = State, Textures = Textures, ReadName = ReadName,
            Rows = DetailSelectionState.VisibleRows, NameBounds = new(890,124,96,16), Activated = Activate, Closed = Closed };
        AddChild(_sidebar);
        _message = new GameMessageBar { Textures = Textures }; AddChild(_message);
        SetHint("treasure-detail");
        var close = new DetailActionButton { Title = "关闭", Role = "close", Textures = Textures, Position = new(875,687), Size = new(108,44) };
        close.MouseEntered += () => { ClearNameFocus(); SetHint("detail-close"); };
        close.MouseExited += () => SetHint("treasure-detail");
        close.Pressed += Closed; AddChild(close);
        Icon("close-icon", new(964,56), Closed);
        Icon("help", new(934,33));
        RefreshBody();
    }
    private void Icon(string role, Vector2 position, Action? action = null)
    {
        var button = new TextureButton { Position = position, TextureNormal = Textures.Get("detail-" + role, this),
            TextureHover = Textures.Get("detail-" + role + "-1", this), TexturePressed = Textures.Get("detail-" + role + "-2", this) };
        if (action is not null) button.Pressed += action;
        AddChild(button);
    }
    private Label Label(string text, Rect2 rect, int size = 16, Control? parent = null, bool centered = false)
    {
        var label = new Label { Text = text, Position = rect.Position, Size = rect.Size, ClipText = true,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            MouseFilter = MouseFilterEnum.Ignore };
        (parent ?? _body).AddChild(label); _text.Apply(label, GameTextStyles.CatalogRow with { FontSize = size }); return label;
    }
    private void SetHint(string role) => _message.SetMessage(Texts.ResolveRich(StrategyHintText.Key(role)), true);
    private void ClearNameFocus() { State.ClearFocus(); _sidebar.Refresh(); }
    public void RefreshData() { _sidebar.Refresh(); RefreshBody(); }
    private void Activate(int id) { State.Activate(id); CurrentChanged(id); _sidebar.Refresh(); RefreshBody(); }
    private void RefreshBody()
    {
        _data = Read(State.CurrentId);
        foreach (Node child in _body.GetChildren()) { _body.RemoveChild(child); child.QueueFree(); }
        Label("宝物情报", new(96,50,120,28), 24, centered: true);
        Label(_data.Name, new(232,50,148,28), 24, centered: true);
        _body.AddChild(new AsyncPortrait { Service = Service, Role = _data.PortraitRole, Position = new(94,118), Size = new(240,240),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore });
        Label("所有", new(380,120,64,24), centered: true); Label(_data.Holder, new(452,120,88,24), centered: true);
        var description = new GameRichTextLabel { Position = new(392,156), Size = new(168,68), Textures = Textures };
        _body.AddChild(description); description.Present(_data.Description, GameTextStyles.CatalogRow);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Label("种类", new(380,272,64,24), centered: true); Label(_data.Category, new(452,272,80,24), centered: true);
        Label(_data.Effect, new(538,272,88,24), centered: true);
        Label("能力", new(380,304,64,24), centered: true); Label(_data.Ability, new(452,304,80,24), centered: true);
        if (_data.TacticGroup is not null)
            Label(_data.Tactic, new(538,304,65,24), centered: true);
        if (_data.EquipmentStatus.Length > 0)
            Label($"装备状态：{_data.EquipmentStatus}", new(380,365,220,24));
        QueueRedraw();
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape } || input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Closed(); return; }
        _sidebar.HandleInput(input);
    }
    private void Tile(string role,Rect2 rect)
    {
        new DetailSkinPainter(this, Textures).Tile(role, rect);
    }
    private void Strip(string role,Rect2 rect)
    {
        new DetailSkinPainter(this, Textures).Strip(role, rect);
    }
    private void Panel(Rect2 r)
    {
        new DetailSkinPainter(this, Textures).Panel(r);
    }
    public override void _Draw()
    {
        if (_data is null) return;
        DrawTexture(Textures.Get("detail-background", this), Vector2.Zero);
        DrawTextureRectRegion(Textures.Get("detail-treasure-list-top", this), new(856,72,168,492), new(0,0,168,492));
        DrawTexture(Textures.Get("detail-treasure-list-bottom", this), new(856,564));
        Strip("title", new(56,34,568,60)); Strip("name", new(232,50,148,28));
        DrawTexture(Textures.Get("detail-portrait-frame", this), new(76,100));
        Panel(new(360,100,224,144));
        Strip("label", new(380,120,64,24)); Strip("field", new(452,120,88,24));
        DrawTexture(Textures.Get("detail-treasure-description", this), new(380,152));
        Panel(new(360,252,286,96));
        Strip("label", new(380,272,64,24)); Strip("field", new(452,272,80,24)); Strip("field", new(538,272,88,24));
        Strip("label", new(380,304,64,24)); Strip("field", new(452,304,80,24));
        if (_data.TacticGroup is int group) DrawTexture(Textures.Get($"detail-tactic-{group}", this), new(538,304));
    }
    public override void _ExitTree() => _text.Dispose();
}
