using Godot;
using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Client.Godot;

public partial class TreasureDetailView : Control
{
    public TreasureDetailState State { get; init; } = null!;
    public Func<int, TreasureDetailData> Read { get; init; } = null!;
    public Func<int, string> ReadName { get; init; } = null!;
    public IReadOnlyDictionary<string, ImageTexture> Textures { get; init; } = null!;
    public Action<int> CurrentChanged { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    private GameMessageBar _message = null!;
    private readonly GameTextPresenter _text = new();
    private readonly List<DetailNameButton> _names = [];
    private Control _body = null!;
    private SkinnedScrollBar _scroll = null!;
    private TreasureDetailData _data = null!;
    private bool _refreshing;

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        for (int row = 0; row < TreasureDetailState.VisibleRows; row++)
        {
            int slot = row;
            var button = new DetailNameButton { Textures = Textures, Position = new(890, 124 + row * 20), Size = new(96, 16) };
            button.MouseEntered += () => { State.PointAt(State.Candidates[State.Offset + slot]); RefreshNames(); };
            button.Pressed += () => Activate(State.Candidates[State.Offset + slot]);
            AddChild(button); _names.Add(button);
        }
        _scroll = new SkinnedScrollBar { Skin = ScrollBarSkin.FromCatalog(Textures), Position = new(990,124),
            Size = new(16,498), ContextRequested = Closed };
        AddChild(_scroll); _scroll.Range.Page = TreasureDetailState.VisibleRows;
        _scroll.Range.MaxValue = Math.Max(TreasureDetailState.VisibleRows, State.Candidates.Count);
        _scroll.Range.ValueChanged += value => { if (!_refreshing) { State.Scroll((int)value); RefreshNames(); } };
        _message = new GameMessageBar { Textures = Textures }; AddChild(_message);
        SetHint("treasure-detail");
        var close = new DetailActionButton { Title = "关闭", Role = "close", Textures = Textures, Position = new(875,687), Size = new(108,44) };
        close.MouseEntered += () => { ClearNameFocus(); SetHint("detail-close"); };
        close.MouseExited += () => SetHint("treasure-detail");
        close.Pressed += Closed; AddChild(close);
        Icon("close-icon", new(964,56), Closed);
        Icon("help", new(934,33));
        RefreshNames(); RefreshBody();
    }
    private void Icon(string role, Vector2 position, Action? action = null)
    {
        var button = new TextureButton { Position = position, TextureNormal = Textures["detail-" + role],
            TextureHover = Textures["detail-" + role + "-1"], TexturePressed = Textures["detail-" + role + "-2"] };
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
    private void SetHint(string role) => _message.SetMessage(Texts.Resolve(StrategyHintText.Key(role)), true);
    private void ClearNameFocus() { State.ClearFocus(); RefreshNames(); }
    private void Activate(int id) { State.Activate(id); CurrentChanged(id); RefreshNames(); RefreshBody(); }
    private void RefreshNames()
    {
        _refreshing = true;
        for (int row = 0; row < _names.Count; row++)
        {
            int index = State.Offset + row;
            _names[row].Visible = index < State.Candidates.Count;
            if (_names[row].Visible) _names[row].Bind(ReadName(State.Candidates[index]), State.FocusedId == State.Candidates[index]);
        }
        _scroll.Visible = State.Candidates.Count > TreasureDetailState.VisibleRows;
        _scroll.Range.Value = State.Offset; _scroll.UpdateArrows(); _refreshing = false; QueueRedraw();
    }
    private void RefreshBody()
    {
        _data = Read(State.CurrentId);
        foreach (Node child in _body.GetChildren()) { _body.RemoveChild(child); child.QueueFree(); }
        Label("宝物情报", new(96,50,120,28), 24, centered: true);
        Label(_data.Name, new(232,50,148,28), 24, centered: true);
        _body.AddChild(new TextureRect { Texture = Textures[_data.PortraitRole], Position = new(94,118), Size = new(240,240),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore });
        Label("所有", new(380,120,64,24), centered: true); Label(_data.Holder, new(452,120,88,24), centered: true);
        var description = Label(_data.Description, new(392,156,168,68));
        description.VerticalAlignment = VerticalAlignment.Top;
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.AddThemeConstantOverride("line_spacing", -3);
        Label("种类", new(380,272,64,24), centered: true); Label(_data.Category, new(452,272,80,24), centered: true);
        Label(_data.Effect, new(538,272,88,24), centered: true);
        Label("能力", new(380,304,64,24), centered: true); Label(_data.Ability, new(452,304,80,24), centered: true);
        if (_data.TacticGroup is not null)
            Label(_data.Tactic, new(538,304,65,24), centered: true);
        QueueRedraw();
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape } || input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Closed(); return; }
        if (input is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        { GetViewport().SetInputAsHandled(); State.Scroll(State.Offset + (mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1)); RefreshNames(); }
        if (input is not InputEventKey { Pressed: true } key) return;
        if (key.Keycode is Key.Up or Key.Down)
        { GetViewport().SetInputAsHandled(); State.MoveFocus(key.Keycode == Key.Down ? 1 : -1); RefreshNames(); }
        else if (key.Keycode == Key.Enter && State.FocusedId is int id)
        { GetViewport().SetInputAsHandled(); Activate(id); }
    }
    private void Tile(string role,Rect2 rect)
    {
        var texture=Textures["detail-"+role];
        for(float y=0;y<rect.Size.Y;y+=texture.GetHeight())
        for(float x=0;x<rect.Size.X;x+=texture.GetWidth())
        {var size=new Vector2(Math.Min(texture.GetWidth(),rect.Size.X-x),Math.Min(texture.GetHeight(),rect.Size.Y-y));DrawTextureRectRegion(texture,new(rect.Position+new Vector2(x,y),size),new(Vector2.Zero,size));}
    }
    private void Strip(string role,Rect2 rect)
    {
        var left=Textures["detail-"+role+"-left"];var right=Textures["detail-"+role+"-right"];
        Tile(role+"-middle",new(rect.Position+new Vector2(left.GetWidth(),0),new(rect.Size.X-left.GetWidth()-right.GetWidth(),rect.Size.Y)));
        DrawTexture(left,rect.Position);DrawTexture(right,new(rect.End.X-right.GetWidth(),rect.Position.Y));
    }
    private void Panel(Rect2 r)
    {
        Tile("panel-center",r);
        Tile("panel-top",new(r.Position+new Vector2(20,0),new(r.Size.X-40,20)));
        Tile("panel-bottom",new(r.Position+new Vector2(20,r.Size.Y-20),new(r.Size.X-40,20)));
        Tile("panel-left",new(r.Position+new Vector2(0,20),new(20,r.Size.Y-40)));
        Tile("panel-right",new(r.Position+new Vector2(r.Size.X-20,20),new(20,r.Size.Y-40)));
        DrawTexture(Textures["detail-panel-top-left"],r.Position);DrawTexture(Textures["detail-panel-top-right"],new(r.End.X-20,r.Position.Y));
        DrawTexture(Textures["detail-panel-bottom-left"],new(r.Position.X,r.End.Y-20));DrawTexture(Textures["detail-panel-bottom-right"],r.End-new Vector2(20,20));
    }
    public override void _Draw()
    {
        if (_data is null) return;
        DrawTexture(Textures["detail-background"], Vector2.Zero);
        DrawTextureRectRegion(Textures["detail-treasure-list-top"], new(856,72,168,492), new(0,0,168,492));
        DrawTexture(Textures["detail-treasure-list-bottom"], new(856,564));
        Strip("title", new(56,34,568,60)); Strip("name", new(232,50,148,28));
        DrawTexture(Textures["detail-portrait-frame"], new(76,100));
        Panel(new(360,100,224,144));
        Strip("label", new(380,120,64,24)); Strip("field", new(452,120,88,24));
        DrawTexture(Textures["detail-treasure-description"], new(380,152));
        Panel(new(360,252,286,96));
        Strip("label", new(380,272,64,24)); Strip("field", new(452,272,80,24)); Strip("field", new(538,272,88,24));
        Strip("label", new(380,304,64,24)); Strip("field", new(452,304,80,24));
        if (_data.TacticGroup is int group) DrawTexture(Textures[$"detail-tactic-{group}"], new(538,304));
    }
    public override void _ExitTree() => _text.Dispose();
}
