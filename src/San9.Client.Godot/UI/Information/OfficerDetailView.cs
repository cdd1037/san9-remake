using Godot;
using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Client.Godot;

/// <summary>Officer browser presentation; navigation and selected identity are owned by the application state.</summary>
public partial class OfficerDetailView : Control, IInformationPage
{
    public OfficerDetailState State { get; init; } = null!;
    public Func<int, OfficerDetailData> Read { get; init; } = null!;
    public Func<int, string> ReadName { get; init; } = null!;
    public Action<int> OpenTreasures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public Action<int> CurrentChanged { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private OfficerDetailData _data = null!;
    private Control _body = null!;
    private Control? _overlay;
    private readonly List<DetailNameButton> _names = [];
    private SkinnedScrollBar _scroll = null!;
    private DetailActionButton _status = null!, _tactics = null!, _treasure = null!;
    private bool _refreshing;
    private GameMessageBar _message = null!;
    private DetailActionButton? _hintButton;

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        for (int row = 0; row < OfficerDetailState.VisibleRows; row++)
        {
            int slot = row;
            var button = new DetailNameButton { Textures = Textures, Position = new(907, 124 + row * 20), Size = new(64,16) };
            button.MouseEntered += () =>
            {
                int index = State.Offset + slot;
                if (index < State.Candidates.Length) { State.PointAt(State.Candidates[index]); RefreshNames(); }
            };
            button.Pressed += () => Activate(State.Candidates[State.Offset + slot]);
            AddChild(button); _names.Add(button);
        }
        _scroll = new SkinnedScrollBar { Skin = ScrollBarSkin.FromCatalog(Textures), Position = new(975,124), Size = new(16,358), ContextRequested = Closed };
        AddChild(_scroll);
        _scroll.Range.Page = OfficerDetailState.VisibleRows;
        _scroll.Range.MaxValue = Math.Max(OfficerDetailState.VisibleRows, State.Candidates.Length);
        _scroll.Range.MouseEntered += ClearNameFocus;
        _scroll.Up.MouseEntered += ClearNameFocus; _scroll.Down.MouseEntered += ClearNameFocus;
        _scroll.Range.ValueChanged += value => { if (!_refreshing) { State.Scroll((int)value); RefreshNames(); } };
        _message = new GameMessageBar { Textures = Textures }; AddChild(_message);
        _treasure = Action("宝物", "treasure", new(888,533), new(120,40), ShowTreasures, hint: "officer-detail-treasures");
        _status = Action("状态", "tab", new(888,585), new(120,40), () => SelectPage(OfficerDetailPage.Status), hint: "officer-detail-status");
        _tactics = Action("兵法", "tab", new(888,629), new(120,40), () => SelectPage(OfficerDetailPage.Tactics), hint: "officer-detail-tactics");
        Action("关闭", "close", new(875,687), new(108,44), Closed, hint: "detail-close");
        Icon("close-icon",new(964,56), Closed);
        Icon("help",new(934,33), ShowHelp);
        RefreshNames(); RefreshBody();
    }

    private void ClearNameFocus() { State.ClearFocus(); RefreshNames(); }
    public void RefreshData() { _scroll.Range.MaxValue = Math.Max(OfficerDetailState.VisibleRows, State.Candidates.Length); RefreshNames(); RefreshBody(); }
    private void Activate(int id) { State.Activate(id); CurrentChanged(id); RefreshNames(); RefreshBody(); }
    private void SelectPage(OfficerDetailPage page) { State.SelectPage(page); RefreshBody(); }
    private DetailActionButton Action(string title, string role, Vector2 position, Vector2 size, Action action, Control? parent = null, string? hint = null)
    {
        var button = new DetailActionButton { Title = title, Role = role, Textures = Textures, Position = position, Size = size };
        button.MouseEntered += ClearNameFocus;
        if (hint is not null)
        {
            button.HintRole = hint;
            button.MouseEntered += () => { _hintButton = button; RefreshHint(); };
            button.MouseExited += () => { if (_hintButton == button) _hintButton = null; RefreshHint(); };
        }
        button.Pressed += action; (parent ?? this).AddChild(button); return button;
    }
    private void RefreshHint()
    {
        string role = _hintButton?.HintRole ?? "officer-detail";
        if (_hintButton == _treasure && _treasure.Disabled) role = "officer-detail-treasures-empty";
        _message.SetMessage(Texts.ResolveRich(StrategyHintText.Key(role)), _hintButton is null || !_hintButton.Disabled);
    }
    private void Icon(string role, Vector2 position, Action action)
    {
        var button = new TextureButton { Position = position, TextureNormal = Textures["detail-"+role], TextureHover = Textures["detail-"+role+"-1"], TexturePressed = Textures["detail-"+role+"-2"] };
        button.MouseEntered += ClearNameFocus;
        button.Pressed += action; AddChild(button);
    }
    private Label Label(string value, Rect2 rect, int fontSize = 16, HorizontalAlignment alignment = HorizontalAlignment.Left, Control? parent = null, Color? color = null)
    {
        var label = new Label { Text = value, Position = rect.Position, Size = rect.Size, ClipText = true, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = alignment, MouseFilter = MouseFilterEnum.Ignore };
        (parent ?? _body).AddChild(label);
        _text.Apply(label, GameTextStyles.CatalogRow with { FontSize = fontSize, Foreground = color ?? new Color("f0f0f0") });
        return label;
    }
    private void RefreshNames()
    {
        _refreshing = true;
        for (int i = 0; i < _names.Count; i++)
        {
            int index = State.Offset + i;
            _names[i].Visible = index < State.Candidates.Length;
            if (_names[i].Visible) _names[i].Bind(ReadName(State.Candidates[index]), State.FocusedId == State.Candidates[index]);
        }
        _scroll.Visible = State.Candidates.Length > OfficerDetailState.VisibleRows;
        _scroll.Range.Value = State.Offset; _scroll.UpdateArrows(); _refreshing = false;
    }
    private void RefreshBody()
    {
        _data = Read(State.CurrentId);
        if (_body is not null) { RemoveChild(_body); _body.QueueFree(); }
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        Label("武将情报",new(108,50,112,28),24,HorizontalAlignment.Center);
        Label(_data.Name,new(232,50,196,28),24,HorizontalAlignment.Center);
        var portrait = new AsyncPortrait { Service = Service, Role = _data.PortraitRole, IsDeceased = () => _data.IsDeceased, Position = new(94,118), Size = new(240,240), MouseFilter = MouseFilterEnum.Ignore };
        _body.AddChild(portrait);
        var radar = new OfficerAbilityRadar { Abilities = _data.RadarAbilities, Position = new(431,156), Size = new(164,164) }; _body.AddChild(radar);
        _body.AddChild(new TextureRect { Texture = Textures["detail-radar-grid"], Position = new(431,156), MouseFilter = MouseFilterEnum.Ignore });
        string[] names = ["统率","武力","智力","政治"];
        for (int i = 0; i < 4; i++)
        {
            Label(names[i],new(660,120+i*32,48,24),16,HorizontalAlignment.Center);
            Label(_data.AbilityValues[i],new(714,120+i*32,72,24),16,HorizontalAlignment.Center);
        }
        Label("统率",new(488,132,48,16),16,HorizontalAlignment.Center,color:new Color("f8f850"));
        Label("武力",new(392,228,40,16),16,HorizontalAlignment.Center,color:new Color("e85440"));
        Label("智力",new(488,324,48,16),16,HorizontalAlignment.Center,color:new Color("60d868"));
        Label("政治",new(600,228,40,16),16,HorizontalAlignment.Center,color:new Color("5098d8"));
        if (State.Page == OfficerDetailPage.Status)
        {
            int[] xs = [96,288,464,640];
            for (int column = 0; column < _data.Fields.Length; column++)
            for (int row = 0; row < _data.Fields[column].Length; row++)
            {
                int x=xs[column], y=404+32*row, labelWidth=column==3?96:64;
                var field=_data.Fields[column][row];
                Label(field.Label,new(x,y,labelWidth,24),16,HorizontalAlignment.Center);
                Label(field.Value,new(x+labelWidth+8,y,column is 0 or 3?104:88,24),16,HorizontalAlignment.Center);
            }
            Label("列传",new(96,636,64,24),16,HorizontalAlignment.Center);
            var biography = new GameRichTextLabel { Position = new(180,640), Size = new(296,92), Textures = Textures };
            _body.AddChild(biography); biography.Present(_data.Biography, GameTextStyles.CatalogRow);
            biography.AddThemeConstantOverride("line_separation", -3);
            biography.AutowrapMode=TextServer.AutowrapMode.Arbitrary;
        }
        else
        {
            Label("兵法",new(96,404,64,24),16,HorizontalAlignment.Center);
            for(int i=0;i<_data.Tactics.Length;i++)
            {
                var group=_data.Tactics[i];int y=412+32*i;
                Label(group.Name,new(176,y,52,24),16,HorizontalAlignment.Center);
                Label(group.Proficiency.ToString(),new(232,y,48,24),16,HorizontalAlignment.Center);
                for(int j=0;j<group.Tactics.Length;j++)
                    Label(group.Tactics[j].Name,new(298+72*j,y,68,24),16,HorizontalAlignment.Center,color:new Color(group.Tactics[j].Available?"f0f0f0":"a4a4a4"));
            }
        }
        _status.Selected=State.Page==OfficerDetailPage.Status; _tactics.Selected=State.Page==OfficerDetailPage.Tactics;
        _treasure.Disabled=_data.Treasures.Length==0;
        RefreshHint();
        QueueRedraw();
    }
    private void CloseOverlay()
    {
        if (_overlay is null) return;
        RemoveChild(_overlay); _overlay.QueueFree(); _overlay=null;
    }
    private Control Overlay()
    {
        CloseOverlay();
        _overlay=new Panel { Position=new(260,190),Size=new(520,390),ZIndex=5,MouseFilter=MouseFilterEnum.Stop };
        _overlay.AddThemeStyleboxOverride("panel",new StyleBoxFlat { BgColor=new Color("241e13"),BorderColor=new Color("a08b48"),BorderWidthLeft=2,BorderWidthRight=2,BorderWidthTop=2,BorderWidthBottom=2 });
        AddChild(_overlay);return _overlay;
    }
    private void ShowHelp()
    {
        var panel=Overlay();
        Label("武将情报",new(20,12,480,36),24,parent:panel);
        Label("单击右侧姓名或按回车切换武将。\n上下键移动名单焦点，滚轮翻阅名单。\n状态、兵法页在切换武将后保持。\n宝物显示当前武将持有的可见宝物。\nEsc 或右键返回。",new(24,68,472,230),18,parent:panel);
        Action("关闭","close",new(206,330),new(108,44),CloseOverlay,panel);
    }
    private void ShowTreasures()
    {
        if (_data.Treasures.Length > 0) OpenTreasures(State.CurrentId);
    }
    public override void _Input(InputEvent input)
    {
        if(!IsVisibleInTree())return;
        if(input is InputEventKey {Pressed:true,Keycode:Key.Escape} || input is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Right})
        { GetViewport().SetInputAsHandled(); if(_overlay is not null)CloseOverlay();else Closed();return; }
        if(_overlay is not null)return;
        if(input is InputEventMouseButton {Pressed:true} mouse && new Rect2(872,72,152,460).HasPoint(GetLocalMousePosition()) && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {State.Scroll(State.Offset+(mouse.ButtonIndex==MouseButton.WheelDown?1:-1));GetViewport().SetInputAsHandled();RefreshNames();}
        if(input is not InputEventKey {Pressed:true} key)return;
        if(key.Keycode is Key.Up or Key.Down)
        {State.MoveFocus(key.Keycode==Key.Down?1:-1);GetViewport().SetInputAsHandled();RefreshNames();}
        else if(key.Keycode==Key.Enter && State.FocusedId is int id)
        {GetViewport().SetInputAsHandled();Activate(id);}
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
        if(_data is null)return;
        DrawTexture(Textures["detail-background"],Vector2.Zero);
        DrawTextureRectRegion(Textures["detail-list-top"],new(872,72,152,352),new(0,0,152,352));
        DrawTexture(Textures["detail-list-bottom"],new(872,424));
        Strip("title",new(56,34,568,60));Strip("name",new(232,50,196,28));
        DrawTexture(Textures["detail-portrait-frame"],new(76,100));
        Panel(new(360,100,446,276));DrawTexture(Textures["detail-radar-background"],new(380,120));
        for(int i=0;i<4;i++){Strip("label",new(660,120+i*32,48,24));Strip("field",new(714,120+i*32,72,24));}
        if(State.Page==OfficerDetailPage.Status)
        {
            Panel(new(76,384,792,224));Panel(new(76,616,424,112));
            DrawTexture(Textures["detail-biography-field"],new(168,636));
            int[] xs=[96,288,464,640];
            for(int column=0;column<4;column++)
            for(int row=0;row<_data.Fields[column].Length;row++)
            {int x=xs[column],y=404+32*row,w=column==3?96:64;Strip("label",new(x,y,w,24));Strip("field",new(x+w+8,y,column is 0 or 3?104:88,24));}
            foreach(int x in new[]{278,454,630})DrawLine(new(x,404),new(x,588),new Color("79683e"));
            Strip("label",new(96,636,64,24));
        }
        else
        {
            Panel(new(76,384,536,324));DrawTexture(Textures["detail-tactics-field"],new(168,404));Strip("label",new(96,404,64,24));
            for(int i=0;i<9;i++)
            {int y=412+32*i;Strip("tactic-category",new(176,y,52,24));Strip("field",new(232,y,48,24));
                for(int j=0;j<_data.Tactics[i].Tactics.Length;j++)DrawTexture(Textures[$"detail-tactic-{i}"+(_data.Tactics[i].Tactics[j].Available?"":"-1")],new(298+72*j,y));}
        }
    }
    public override void _ExitTree()=>_text.Dispose();
}

public partial class DetailActionButton : BaseButton
{
    public string? HintRole {get;set;}
    public string Title {get;init;}="";
    public string Role {get;init;}="";
    public TextureScope Textures {get;init;}=null!;
    public bool Selected {get;set;}
    private readonly GameTextPresenter _text=new();
    private Label _label=null!;
    public override void _Ready()
    {
        _label=new Label {Text=Title,Size=Size,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore};AddChild(_label);
        RefreshText();
    }
    private void RefreshText()
    {
        bool pressed = !Disabled && (Selected || ButtonPressed);
        _label.Position = pressed ? new(2,2) : Vector2.Zero;
        _text.Apply(_label, GameTextStyles.Button(Role == "treasure" ? GameButtonTextRole.Facilities : GameButtonTextRole.MapMode,
            IsHovered(), pressed, Disabled) with { FontSize = 20 });
    }
    private (bool Disabled, bool Selected, bool Pressed, bool Hovered)? _visual;
    public override void _Process(double delta)
    {
        var state = (Disabled, Selected, ButtonPressed, IsHovered());
        if (_visual == state) return;
        _visual = state; RefreshText(); QueueRedraw();
    }
    public override void _Draw()
    {
        int frame=Disabled?3:Selected||ButtonPressed?2:IsHovered()?1:0;
        if(Role is "tab" or "treasure")DrawTexture(Textures["detail-button-shadow"],Vector2.Zero);
        DrawTexture(Textures["detail-"+Role+(frame==0?"":$"-{frame}")],Role is "tab" or "treasure" ? new(4,4) : Vector2.Zero);
    }
    public override void _ExitTree()=>_text.Dispose();
}

public partial class DetailNameButton : BaseButton
{
    public TextureScope Textures {get;init;}=null!;
    private readonly GameTextPresenter _text=new();private Label _label=null!;private bool _focused;
    public override void _Ready()
    {FocusMode=FocusModeEnum.None;_label=new Label {Size=Size,MouseFilter=MouseFilterEnum.Ignore,ClipText=true};AddChild(_label);_text.Apply(_label,GameTextStyles.CatalogRow);}
    public void Bind(string name,bool focused) { if (_label.Text == name && _focused == focused) return; _label.Text=name;_focused=focused;QueueRedraw(); }
    private bool _pressed;
    public override void _Process(double delta)
    {
        if (_pressed == ButtonPressed) return;
        _pressed = ButtonPressed; _label.Position = _pressed ? new(2,2) : Vector2.Zero; QueueRedraw();
    }
    public override void _Draw()
    {
        int frame=ButtonPressed?2:_focused?1:0;
        string suffix=frame==0?"":$"-{frame}";
        DrawTexture(Textures["roster-row-left"+suffix],Vector2.Zero);
        var middle = Textures["roster-row-middle" + suffix];
        for (float x = 8; x < Size.X - 8; x += middle.GetWidth())
        {
            float width = Math.Min(middle.GetWidth(), Size.X - 8 - x);
            DrawTextureRectRegion(middle, new(x, 0, width, 16), new(0, 0, width, 16));
        }
        DrawTexture(Textures["roster-row-right"+suffix],new(Size.X-8,0));
    }
    public override void _ExitTree()=>_text.Dispose();
}

public partial class OfficerAbilityRadar : Control
{
    public San9.Core.Domain.OfficerAbilities Abilities {get;init;}
    private ShaderMaterial? _material;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/officer_ability_radar.gdshader") };
        _material.SetShaderParameter("abilities", new Vector4(Abilities.Leadership, Abilities.Politics, Abilities.Intelligence, Abilities.War));
        AddChild(new ColorRect { Size = new(164,164), Material = _material, MouseFilter = MouseFilterEnum.Ignore });
    }
    public override void _ExitTree() => _material?.Dispose();
}
