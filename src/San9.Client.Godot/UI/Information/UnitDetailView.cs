using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class UnitDetailView : Control, IInformationPage
{
    public const int VisibleRows = 22;
    public DetailSelectionState State { get; init; } = null!;
    public Func<int, UnitDetailData> Read { get; init; } = null!;
    public Func<int, string> ReadName { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Action<int> CurrentChanged { get; init; } = null!;
    public Action<int> OpenOfficers { get; init; } = null!;
    public Action<int> OpenOfficer { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private readonly List<DetailNameButton> _names = [];
    private Control? _body;
    private SkinnedScrollBar _scroll = null!;
    private DetailActionButton _officers = null!;
    private bool _refreshing;
    private bool _help;
    public override void _Ready()
    {
        Size = new(1024,768); MouseFilter = MouseFilterEnum.Stop;
        State.SetVisibleRows(VisibleRows);
        for (int row = 0; row < VisibleRows; row++)
        {
            int slot = row;
            var name = new DetailNameButton { Textures = Textures, Position = new(898,124+row*20), Size = new(88,16) };
            name.MouseEntered += () => { State.PointAt(State.Candidates[State.Offset+slot]); RefreshNames(); };
            name.Pressed += () => Activate(State.Candidates[State.Offset+slot]); AddChild(name); _names.Add(name);
        }
        _scroll = new SkinnedScrollBar { Skin = ScrollBarSkin.FromCatalog(Textures), Position = new(990,124), Size = new(16,438), ContextRequested = Closed };
        AddChild(_scroll); _scroll.Range.Page = VisibleRows;
        _scroll.Range.ValueChanged += value => { if (!_refreshing) { State.Scroll((int)value); RefreshNames(); } };
        _officers = Action("武将", "treasure", new(888,629), new(120,40), () => OpenOfficers(State.CurrentId));
        Action("关闭", "close", new(875,687), new(108,44), Closed);
        Icon("close-icon",new(964,56),Closed); Icon("help",new(934,33),()=> { _help = !_help; RefreshBody(); });
        AddChild(new GameMessageBar { Textures = Textures, Text = "现在显示的是部队情报。" });
        RefreshNames(); RefreshBody();
    }
    private DetailActionButton Action(string text, string role, Vector2 at, Vector2 size, Action action)
    {
        var button = new DetailActionButton { Title = text, Role = role, Position = at, Size = size, Textures = Textures };
        button.Pressed += action; AddChild(button); return button;
    }
    private void Icon(string role, Vector2 at, Action action)
    {
        var button = new TextureButton { Position = at, TextureNormal = Textures["detail-"+role], TextureHover = Textures["detail-"+role+"-1"], TexturePressed = Textures["detail-"+role+"-2"] };
        button.Pressed += action; AddChild(button);
    }
    public void RefreshData() { _scroll.Range.MaxValue = Math.Max(VisibleRows, State.Candidates.Count); RefreshNames(); RefreshBody(); }
    private void Activate(int id) { State.Activate(id); CurrentChanged(id); RefreshNames(); RefreshBody(); }
    private void RefreshNames()
    {
        _refreshing = true;
        for (int row = 0; row < _names.Count; row++)
        {
            int index = State.Offset + row; _names[row].Visible = index < State.Candidates.Count;
            if (_names[row].Visible) _names[row].Bind(ReadName(State.Candidates[index]), State.FocusedId == State.Candidates[index]);
        }
        _scroll.Range.MaxValue = Math.Max(VisibleRows, State.Candidates.Count);
        _scroll.Visible = State.Candidates.Count > VisibleRows; _scroll.Range.Value = State.Offset; _scroll.UpdateArrows(); _refreshing = false;
    }
    private void Label(string text, Rect2 rect, int font = 16, bool center = false)
    {
        var label = new Label { Text = text, Position = rect.Position, Size = rect.Size, ClipText = true, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left, MouseFilter = MouseFilterEnum.Ignore };
        _body!.AddChild(label); _text.Apply(label, GameTextStyles.CatalogRow with { FontSize = font });
    }
    private void Field(string label, string value, int y)
    { Label(label,new(624,y,96,24),center:true); Label(value,new(728,y,112,24),center:true); }
    private void RefreshBody()
    {
        var data = Read(State.CurrentId);
        if (_body is not null) { RemoveChild(_body); _body.QueueFree(); }
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        Label("部队情报",new(108,50,112,28),24,true); Label(data.Name,new(232,50,124,28),24,true);
        Label(data.Condition,new(494,50,64,28),20,true);
        Label("布阵",new(96,116,40,24));
        _body.AddChild(new FormationBoardView { Data = data.Board, Textures = Textures, Position = new(136,116), Size = new(432,264), OfficerActivated = OpenOfficer });
        Label("兵法发动率：越靠前排的武将越高。\n狙击对象：前锋及中锋的武将可能遭到狙击。\n单挑对象：敌我双方的前锋。",new(180,396,388,56),14);
        Label("阵形　"+data.Formation,new(96,476,220,24)); Label("船　"+data.Ship,new(356,476,220,24));
        Label("陆上视野",new(96,508,100,24)); Label("水上视野",new(356,508,100,24));
        _body.AddChild(new FormationVisionView { Data = data.LandVision, Position = new(177,512), Size = new(120,120) });
        _body.AddChild(new FormationVisionView { Data = data.WaterVision, Position = new(435,512), Size = new(120,120) });
        if (data.LandVision is null) Label("—",new(208,552,48,24),center:true);
        if (data.WaterVision is null) Label("—",new(468,552,48,24),center:true);
        Field("势力",data.Force,120); Field("所属",data.Home,152); Field("士兵",data.Soldiers,196);
        Field("伤兵",data.Wounded,228); Field("士气",data.Morale,260);
        for (int i = 0; i < data.Orders.Count; i++) Field(data.Orders[i].Label,data.Orders[i].Value,304+i*32);
        Label("陆上",new(728,508,48,24),center:true); Label("水上",new(792,508,48,24),center:true);
        string[] labels = ["对部队","对守兵","对城壁","守备力","机动力"];
        for (int i=0;i<labels.Length;i++)
        {
            Label(labels[i],new(624,544+i*32,96,24),center:true);
            Label(data.Land[i].Text,new(728,544+i*32,48,24),center:true); Label(data.Water[i].Text,new(792,544+i*32,48,24),center:true);
        }
        _officers.Disabled = data.OfficerIds.Count == 0;
        if (_help)
        {
            var panel = new Panel { Position = new(180,240), Size = new(388,120), MouseFilter = MouseFilterEnum.Stop };
            panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat { BgColor = new Color("171b1b") }); _body.AddChild(panel);
            Label("选择右侧名单切换部队。\n点击布阵武将查看情报；武将按钮打开在队名单。\nEsc 或右键返回。",new(192,252,364,96),16);
        }
        QueueRedraw();
    }
    public override void _Input(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape } || input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); if (_help) { _help=false; RefreshBody(); } else Closed(); }
        else if (input is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        { State.Scroll(State.Offset+(mouse.ButtonIndex==MouseButton.WheelDown?1:-1)); RefreshNames(); GetViewport().SetInputAsHandled(); }
        else if (input is InputEventKey { Pressed: true } key)
        {
            if (key.Keycode is Key.Up or Key.Down) { State.MoveFocus(key.Keycode==Key.Down?1:-1); RefreshNames(); GetViewport().SetInputAsHandled(); }
            else if (key.Keycode==Key.Enter && State.FocusedId is int id) { GetViewport().SetInputAsHandled(); Activate(id); }
        }
    }
    public override void _Draw()
    {
        DrawTexture(Textures["detail-background"],Vector2.Zero);
        DrawTextureRectRegion(Textures["detail-unit-list-top"],new(864,72,160,452),new(0,0,160,452));
        DrawTexture(Textures["detail-unit-list-bottom"],new(864,524));
        DrawTexture(Textures["formation-panel-detail"],new(76,100));
        var painter = new DetailSkinPainter(this,Textures);
        painter.Strip("title",new(56,34,568,60)); painter.Strip("name",new(232,50,124,28));
        DrawTextureRect(Textures["detail-status-badge"],new(492,48,68,32),false);
        painter.Panel(new(604,100,256,612));
        foreach (int y in new[] {120,152,196,228,260,304,336,368,400,432,464})
        { painter.Strip("label",new(624,y,96,24)); painter.Strip("field",new(728,y,112,24)); }
        foreach (int y in new[] {184,292,500}) DrawLine(new(620,y),new(840,y),new Color("9c8141"));
        painter.Strip("admin-label",new(728,508,48,24)); painter.Strip("admin-label",new(792,508,48,24));
        for (int i=0;i<5;i++)
        { int y=544+i*32; painter.Strip("label",new(624,y,96,24)); painter.Strip("field",new(728,y,48,24)); painter.Strip("field",new(792,y,48,24)); }
    }
    public override void _ExitTree() => _text.Dispose();
}
