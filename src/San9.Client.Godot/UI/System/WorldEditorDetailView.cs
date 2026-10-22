using Godot;
using San9.Application.Presentation;
using San9.Core.Domain;
using San9.Content.Text;

namespace San9.Client.Godot;

/// <summary>Editor layouts use their own skin and local coordinates, independently of information views.</summary>
public partial class WorldEditorDetailView : FixedPage
{
    protected override Color PageBackground => new(0, 0, 0, .45f);
    public WorldEditorDraft Draft { get; init; } = null!;
    public int CurrentId { get; set; }
    public string Title { get; init; } = "";
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public IReadOnlyList<int> Candidates { get; init; } = [];
    public Func<int, Control> MapPreview { get; init; } = null!;
    public Action<EditorField, Action> NumberRequested { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action<int> CurrentChanged { get; init; } = _ => { };
    private readonly GameTextPresenter _text = new();
    private readonly DetailSelectionState _selection = new();
    private Control _body = null!;
    private DetailSidebar _sidebar = null!;
    private GameMessageBar _message = null!;
    private readonly List<Rect2> _panels = [];
    private readonly List<Rect2> _values = [];
    private readonly List<Rect2> _plates = [];
    private readonly List<Rect2> _captions = [];
    private readonly List<(string Role, Vector2 At)> _images = [];
    private int _corpsOffset;
    private bool _help;
    private bool Small => Draft.Kind is WorldEditorKind.Facilities or WorldEditorKind.Obstacles;
    private Rect2 Bounds => Draft.Kind switch
    {
        WorldEditorKind.Facilities => new(48,69,928,603),
        WorldEditorKind.Obstacles => new(48,69,928,567),
        _ => new(4,5,1016,Draft.Kind == WorldEditorKind.Corps ? 743 : 759)
    };
    private int NameRows => Draft.Kind switch { WorldEditorKind.Facilities => 14, WorldEditorKind.Obstacles => 13, _ => 20 };

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _selection.SetVisibleRows(NameRows); _selection.Open(Candidates, CurrentId);
        _sidebar = new DetailSidebar { State = _selection, Textures = Textures,
            Position = Bounds.Position, Rows = NameRows, RowHeight = 24,
            NameBounds = new(Bounds.Size.X - 232,136,80,16),
            ScrollSkin = ScrollBarSkin.FromEditor(Textures), ScrollBounds = new(Bounds.Size.X - 135,130,16,NameRows*24+20),
            ReadName = id => Draft.Targets.Single(t => t.Id == id).Name, Closed = Closed,
            Activated = id => { CurrentId = id; _selection.Activate(id); _corpsOffset = 0; CurrentChanged(id); Refresh(); } };
        AddChild(_sidebar);
        // The message bar belongs to the viewport, rather than the dialog's local origin.
        _message = new GameMessageBar { Textures = Textures };
        AddChild(_message);
        Refresh();
    }
    public void Refresh()
    {
        _hintButton = null;
        if (_body is not null) { RemoveChild(_body); _body.QueueFree(); }
        _body = new Control { Position = Bounds.Position, MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        _panels.Clear(); _values.Clear(); _plates.Clear(); _captions.Clear(); _images.Clear();
        Label(Title, new(93,47,Title.Length*24,24), 24);
        Header();
        _sidebar.Refresh();
        Hint(ActionButton("关闭", "close", new(Bounds.Size.X - 380, Bounds.Size.Y - 88), Closed),
            "detail-close");
        bool changed = Draft.Kind == WorldEditorKind.Corps ? Draft.ForceCorps(CurrentId).Any(c => Draft.IsChanged(c.Id)) : Draft.IsChanged(CurrentId);
        Hint(Button("取消", "cancel", new(Bounds.Size.X - 236, Bounds.Size.Y - 88), new(120,48), ResetCurrent, disabled: !changed, font:24),
            CancelHintRole, CancelHintRole + "-unchanged");
        Icon("help", new(Bounds.Size.X - 123,40), () => { _help = !_help; RefreshHint(); });
        Icon("close-icon", new(Bounds.Size.X - 89,60), Closed);
        MoveChild(_message, -1);
        RefreshHint();
        switch (Draft.Kind)
        {
            case WorldEditorKind.Officers: Officer(); break;
            case WorldEditorKind.Corps: Corps(); break;
            case WorldEditorKind.Cities: City(); break;
            default: Facility(); break;
        }
        QueueRedraw();
    }
    private BaseButton? _hintButton;
    private string _hintRole = "";
    private string? _disabledHintRole;
    private string CancelHintRole => Draft.Kind switch
    {
        WorldEditorKind.Officers => "editor-cancel-officers",
        WorldEditorKind.Corps => "editor-cancel-corps",
        WorldEditorKind.Cities => "editor-cancel-cities",
        WorldEditorKind.Facilities => "editor-cancel-facilities",
        WorldEditorKind.Obstacles => "editor-cancel-obstacles",
        _ => throw new InvalidOperationException("Unknown world editor kind.")
    };
    private void RefreshHint()
    {
        string text = _hintButton is not null
            ? Texts.ResolveRich(StrategyHintText.Key(_hintButton.Disabled ? _disabledHintRole ?? _hintRole : _hintRole))
            : _help ? "点击蓝色项目输入数值，点击兵法切换。取消恢复当前对象，关闭返回一览；一览的决定保留全部修改。"
            : "请编辑" + Title[2..] + "资料。";
        _message.SetMessage(text, _hintButton is null || !_hintButton.Disabled);
    }
    private void Hint(BaseButton button, string role, string? disabledRole = null)
    {
        void Enter() { _hintButton = button; _hintRole = role; _disabledHintRole = disabledRole; RefreshHint(); }
        void Exit()
        {
            if (_hintButton != button || button.IsHovered() || button.HasFocus()) return;
            _hintButton = null;
            RefreshHint();
        }
        button.MouseEntered += Enter;
        button.MouseExited += Exit;
        button.FocusEntered += Enter;
        button.FocusExited += Exit;
    }
    private void Header()
    {
        void Name(string text, Rect2 rect) { _plates.Add(rect); Label(text,rect,24); }
        if(Draft.Kind == WorldEditorKind.Cities)
        {
            var data=Draft.ReadAdministrativeDetail(CurrentId);
            Name(data.Name,new(209,43,52,32));
            Name(data.Status == "正常" ? "一般" : data.Status,new(269,43,52,32));
            if (data.Feature != "—") Name(data.Feature + "修行",new(333,43,100,32));
        }
        else if(Draft.Kind == WorldEditorKind.Corps) Name(Draft.ReadAdministrativeDetail(CurrentId).Name,new(209,43,100,32));
        else Name(Draft.DisplayName(CurrentId),new(Draft.Kind==WorldEditorKind.Obstacles?233:209,43,Small?124:196,32));
    }
    private void ResetCurrent()
    {
        if (Draft.Kind == WorldEditorKind.Corps)
            foreach (var corps in Draft.ForceCorps(CurrentId)) Draft.Reset(corps.Id);
        else Draft.Reset(CurrentId);
        Refresh();
    }
    private void Officer()
    {
        var fields = Draft.Fields(CurrentId);
        _images.Add(("portrait",new(97,113)));
        Label(Draft.DisplayName(CurrentId),new(109,119,280,24),24);
        _body.AddChild(new AsyncPortrait { Service = Service, Role = $"information-person-{CurrentId}",
            Position = new(107,159), Size = new(96,120), View = San9.Content.Images.PortraitView.Face, MouseFilter = MouseFilterEnum.Ignore });
        var state = Draft.Preview.World.Officers.Single(p => p.Id == CurrentId);
        _body.AddChild(new OfficerAbilityRadar { Abilities = state.BaseAbilities, Textures = Textures,
            Layout = OfficerAbilityRadarLayout.Editor, Position = new(259,178) });
        string[] axes = ["统率", "武力", "智力", "政治"];
        _panels.Add(new(414,108,336,180)); _panels.Add(new(414,293,336,52)); _panels.Add(new(93,349,656,308));
        for (int i = 0; i < axes.Length; i++) Field(fields, axes[i], new(438,120+i*32),64,68);
        string[] personal = ["情义","野心","相性","负伤","忠诚"];
        for (int i=0;i<personal.Length;i++) Field(fields,personal[i],new(602,120+i*32),64,68);
        Plate("性格",new(450,311,56,28));
        var personalities=fields.Where(f=>f.Label.StartsWith("性格 · ",StringComparison.Ordinal)).ToArray();
        for(int i=0;i<personalities.Length;i++) Choice(personalities[i],new(502+i*56,305),new(56,28),personalities[i].Label[5..]);
        Plate("兵法",new(129,367,56,28));
        string[] categories=["步兵","骑兵","弓骑","弩兵","水军","攻城","智略","谋略","策略"];
        string[] styles=["infantry","cavalry","mounted-bow","crossbow","naval","siege","knowledge","plot","strategy"];
        var tactics=Draft.OfficerTacticFields(CurrentId);
        for(int row=0;row<categories.Length;row++)
        {
            int y=361+row*32;
            Plate(categories[row],new(213,y+6,56,28));
            Number(fields.Single(f=>f.Label==categories[row]+"熟练"),"熟练",new(269,y),64,68,16);
            Choice(fields.Single(f=>f.Label==categories[row]+"精通"),new(437,y),new(56,28),"精通");
            var group=tactics.Where(t=>t.Category==row).ToArray();
            for(int column=0;column<group.Length;column++)
                Choice(group[column].Field,new(501+column*56,y),new(56,28),group[column].Field.Label,styles[row]);
        }
    }
    private void City()
    {
        _panels.Add(new(93,108,572,296)); _images.Add(("map-frame",new(117,124))); Preview(new(122,129),new(512,256));
        _panels.Add(new(93,413,655,244));
        var fields=Draft.Fields(CurrentId);
        string[] domestic=["民心","收益","收获","耐久"], maximum=["最大收益","最大收获","最大耐久"], population=["非兵役人口","兵役人口","士兵","士气"];
        for(int row=0;row<domestic.Length;row++) Field(fields,domestic[row],new(121,425+row*36),88,120,8,showMaximum:true);
        for(int row=0;row<maximum.Length;row++)
        {
            var field=fields.Single(f=>f.Label==maximum[row]);
            Button(field.Label,"field",new(341,461+row*36),new(88,28),()=>NumberRequested(field,Refresh));
        }
        for(int row=0;row<population.Length;row++) Field(fields,population[row],new(461,424+row*36),88,168,8,showMaximum:true);
        Plate("兵法",new(127,587,88,28)); Plate("特性",new(467,587,88,28));
        int martial=0,plot=0,passive=0;
        foreach(var (field,effect) in Draft.CityTacticFields(CurrentId))
        {
            (Vector2 at,string style)=effect switch
            {
                TacticEffect.FacilityAttack => (new Vector2(557+passive++*56,581),"crossbow"),
                TacticEffect.Martial => (new Vector2(212+martial++*56,581),"siege"),
                TacticEffect.Sorcery => (new Vector2(380,581),"strategy"),
                _ => (new Vector2(212+plot++*56,617),"plot")
            };
            Choice(field,at,new(56,28),field.Label[3..],style);
        }
    }
    private void Facility()
    {
        bool obstacle=Draft.Kind==WorldEditorKind.Obstacles;
        _panels.Add(new(93,108,obstacle?572:568,296)); _images.Add(("map-frame",new(117,124))); Preview(new(122,129),new(512,256));
        _panels.Add(new(93,413,obstacle?264:568,obstacle?52:88));
        var fields=Draft.Fields(CurrentId);
        Field(fields,"耐久",new(117,425),88,120,8,showMaximum:true);
        if(!obstacle) { Field(fields,"士兵",new(372,425),88,168,8,showMaximum:true); Field(fields,"士气",new(372,461),88,168,8,showMaximum:true); }
    }
    private void Corps()
    {
        _panels.Add(new(93,108,656,148));
        var corps=Draft.ForceCorps(CurrentId);
        var detail = Draft.ReadAdministrativeDetail(CurrentId);
        Plate("君主",new(117,126,60,24));
        _body.AddChild(new OfficerCard { Service = Service, Textures = Textures, Position = new(201,120),
            Officer = detail.PortraitRole is string portrait && detail.LeaderAbilities is { } abilities
                ? new(detail.LeaderName, portrait, abilities) : null });
        var overview = detail.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Id);
        string[] fieldIds = ["country","rank","trust","tactics","adviser","corps","money","food"];
        for (int index = 0; index < fieldIds.Length; index++)
        {
            var field = overview[fieldIds[index]];
            var at = new Vector2(index < 4 ? 437 : 577,132 + index % 4 * 32);
            Plate(field.Label,new(at,new(56,28)));
            var value = new Rect2(at + new Vector2(64,0),new(68,28));
            _values.Add(value);
            Label(field.Value,new(value.Position+new Vector2(4,0),value.Size-new Vector2(8,0)),alignment: index is 2 or 5 or 6 or 7 ? HorizontalAlignment.Right : HorizontalAlignment.Center);
        }
        const int visible = 8;
        for (int index = 0; index < visible; index++)
        {
            int column = index % 2, row = index / 2;
            int x = 93 + column * 337, y = 265 + row * 96;
            _panels.Add(new(x,y,320,88));
            int targetIndex = _corpsOffset + index;
            var target = targetIndex < corps.Count ? corps[targetIndex] : null;
            Plate(target?.Name ?? "",new(x+24,y+16,96,28));
            if (target is not null)
            {
                var fields = Draft.Fields(target.Id);
                Number(fields[0],"资金",new(x+148,y+12),56,84,8);
                Number(fields[1],"兵粮",new(x+148,y+48),56,84,8);
            }
            else
            {
                Button("资金","field",new(x+148,y+12),new(56,28),()=>{},disabled:true);
                Button("兵粮","field",new(x+148,y+48),new(56,28),()=>{},disabled:true);
                _values.Add(new(x+212,y+12,84,28));
                _values.Add(new(x+212,y+48,84,28));
            }
        }
        if(corps.Count>visible)
        {
            ActionButton("上一页","close",new(320,636),()=>{_corpsOffset=Math.Max(0,_corpsOffset-visible);Refresh();},80);
            ActionButton("下一页","close",new(416,636),()=>{_corpsOffset=Math.Min((corps.Count-1)/visible*visible,_corpsOffset+visible);Refresh();},80);
        }
    }
    private void Preview(Vector2 at,Vector2 size)
    {
        var preview=MapPreview(CurrentId); preview.Position=at;preview.Size=size;_body.AddChild(preview);
    }
    private void Field(IReadOnlyList<EditorField> fields,string name,Vector2 at,float labelWidth,float valueWidth,float gap=2,bool showMaximum=false)
    {
        var field=fields.SingleOrDefault(f=>f.Label==name);
        if(field is not null) Number(field,name,at,labelWidth,valueWidth,gap,showMaximum);
        else { Button(name,"field",at,new(labelWidth,28),()=>{},disabled:true); Label("—",new(at+new Vector2(labelWidth+gap,0),new(valueWidth,28))); }
    }
    private void Number(EditorField field,string title,Vector2 at,float labelWidth,float valueWidth,float gap,bool showMaximum=false)
    {
        Button(title,"field",at,new(labelWidth,28),()=>NumberRequested(field,Refresh));
        Value(field,at+new Vector2(labelWidth+gap,0),new(valueWidth,28),showMaximum ? $"{field.Value}/{field.Maximum}" : null);
    }
    private void Value(EditorField field,Vector2 at,Vector2 size,string? title=null)
    {
        _values.Add(new(at,size));
        var button=new WorldEditorButton { Title=title??field.Value.ToString(),Style="",Textures=Textures,Position=at,Size=size,TextAlignment=HorizontalAlignment.Right };
        button.Pressed+=()=>NumberRequested(field,Refresh);_body.AddChild(button);
    }
    private void Choice(EditorField field,Vector2 at,Vector2 size,string title,string style="choice") =>
        Button(title,style,at,size,()=>{field.Set(field.Value==0?1u:0u);Refresh();},field.Value!=0);
    private WorldEditorButton Button(string title,string style,Vector2 at,Vector2 size,Action action,bool selected=false,bool disabled=false,int font=16)
    {
        var button=new WorldEditorButton { Textures=Textures,Title=title,Style=style,Position=at,Size=size,Selected=selected,Disabled=disabled,FontSize=font };
        button.Pressed+=action;_body.AddChild(button);
        return button;
    }
    private WorldEditorButton ActionButton(string title,string style,Vector2 at,Action action,float width=120) => Button(title,style,at,new(width,48),action,font:24);
    private void Icon(string role,Vector2 at,Action action)
    {
        var button=new TextureButton { Position=at,TextureNormal=Textures.Get("editor-"+role,this),TextureHover=Textures.Get("editor-"+role+"-1",this),TexturePressed=Textures.Get("editor-"+role+"-2",this) };
        button.Pressed+=action;_body.AddChild(button);
    }
    private void Label(string value,Rect2 rect,int fontSize=16,Color? color=null,HorizontalAlignment alignment=HorizontalAlignment.Center)
    {
        var label=new Label { Text=value,TooltipText=value,Position=rect.Position,Size=rect.Size,ClipText=true,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=alignment,MouseFilter=MouseFilterEnum.Ignore };
        _body.AddChild(label);_text.Apply(label,GameTextStyles.CatalogRow with {FontSize=fontSize,Foreground=color??new Color("f0f0f0")});
    }
    private void Plate(string value,Rect2 rect) { _captions.Add(rect); Label(value,rect); }
    public override void _Input(InputEvent input)
    {
        if(input is InputEventKey {Pressed:true,Echo:false,Keycode:Key.Escape} || input is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Right})
        {GetViewport().SetInputAsHandled();Closed();return;}
        if (input is InputEventKey || input is InputEventMouseButton mouse && new Rect2(Bounds.Position + new Vector2(Bounds.Size.X - 248,130),
            new Vector2(144,NameRows*24+20)).HasPoint(GetGlobalTransformWithCanvas().AffineInverse() * mouse.Position))
            _sidebar.HandleInput(input);
    }
    public override void _Draw()
    {
        var painter=new WorldEditorSkinPainter(this,Textures);
        float titleWidth=Draft.Kind switch { WorldEditorKind.Officers=>313,WorldEditorKind.Cities=>341,WorldEditorKind.Corps=>217,WorldEditorKind.Facilities=>241,_=>265 };
        painter.Frame(Bounds,titleWidth);
        foreach(var panel in _panels) painter.Panel(new(panel.Position+Bounds.Position,panel.Size),panel.Position-new Vector2(93,108));
        foreach(var value in _values) painter.Value(new(value.Position+Bounds.Position,value.Size));
        foreach(var caption in _captions) painter.Caption(new(caption.Position+Bounds.Position,caption.Size));
        foreach(var plate in _plates) painter.Label(new(plate.Position+Bounds.Position,plate.Size));
        foreach(var (role,at) in _images) DrawTexture(Textures.Get("editor-"+role,this),at+Bounds.Position);
        var names=Textures.Get("editor-names",this);
        // Shorter dialogs crop the same list surface while retaining both end caps.
        var listAt=Bounds.Position+new Vector2(Bounds.Size.X-248,108);
        int height=Draft.Kind switch { WorldEditorKind.Facilities=>384,WorldEditorKind.Obstacles=>356,_=>528 };
        DrawTextureRectRegion(names,new(listAt,new(128,height-20)),new(0,0,128,height-20));
        DrawTextureRectRegion(names,new(listAt+new Vector2(0,height-20),new(128,20)),new(0,names.GetHeight()-20,128,20));
    }
    public override void _GuiInput(InputEvent input)=>AcceptEvent();
    public override void _ExitTree()=>_text.Dispose();
}
