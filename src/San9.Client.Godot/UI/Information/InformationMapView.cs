using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Client.Godot;

public partial class InformationMapView : Control, IInformationPage
{
    public Func<StrategySnapshot> ReadView { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public InformationCategory Category { get; init; }
    public Func<int[]> ReadCandidates { get; init; } = null!;
    private int[] Candidates => ReadCandidates();
    public Action<int> Activated { get; init; } = null!;
    public Action ShowList { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public string Title { get; init; } = "";
    public string HintText { get; init; } = "";
    public string CloseTitle { get; init; } = "关闭";
    public CatalogSelection? Selection { get; init; }
    public Func<IReadOnlySet<int>, bool>? CanConfirmSelection { get; init; }
    public Action<IReadOnlySet<int>>? ConfirmSelection { get; init; }
    private InformationActionButton? _confirmSelection;
    private CatalogBulkButton? _clearSelection;
    private static readonly Vector2 Origin = new(86,86);
    private static readonly Vector2 SummaryOrigin = new(92,496);
    private static readonly int[] SummaryColumns = [16,196,376,572];
    private static readonly int[] SummaryWidths = [176,176,192,192];
    private InformationMapMarker[] _markers = [];
    private InformationField[] _fields = [];
    private int? _hovered;
    private int? _pressedTarget;
    private readonly GameTextPresenter _text = new();
    private readonly List<Label> _values = [];
    public override void _Ready()
    {
        Size=new(1024,768); MouseFilter=MouseFilterEnum.Stop;
        _markers=AdministrativeMap.Markers(ReadView(),Category,Candidates);
        Label(Title.Length > 0 ? Title : InformationQuery.MenuTitle(Category)[1..]+"一览",new(88,22),new(192,36),24);
        if(Candidates.Length>0) _fields=AdministrativeMap.Summary(ReadView(),Texts,new(Category,Candidates[0]));
        for(int i=0;i<_fields.Length;i++)
        {
            int column = i/4;
            var p=SummaryOrigin+new Vector2(SummaryColumns[column],16+i%4*32);
            Label(_fields[i].Label,p+new Vector2(2,0),new(80,24));
            _values.Add(Label("",p+new Vector2(88,0),new(SummaryWidths[column]-92,24),16,HorizontalAlignment.Right));
        }
        Button("一览",new(Selection is null ? 612 : 528,680),ShowList,true); Button(CloseTitle,new(808,696),Closed,false);
        if (Selection is not null)
        {
            _confirmSelection = new InformationActionButton { Title = "决定", SkinPrefix = "catalog-accept-", FontSize = 24,
                TextRole = GameButtonTextRole.ConfirmYes,
                Textures = Textures, Position = new(720,696), Size = new(80,48) };
            _confirmSelection.Pressed += () => { if (SelectionAccepted) ConfirmSelection?.Invoke(Selection.Selected); };
            AddChild(_confirmSelection);
            _clearSelection = new CatalogBulkButton { Title = "总括解除", Textures = Textures,
                Position = new(256,680), Size = new(112,32) };
            _clearSelection.Pressed += () => { Selection.Clear(); RefreshSelection(); };
            AddChild(_clearSelection); RefreshSelection();
        }
        var close=new TextureButton { Position=new(932,56), TextureNormal=Textures["information-action-close"],
            TextureHover=Textures["information-action-close-1"],TexturePressed=Textures["information-action-close-2"] };
        close.Pressed+=Closed; AddChild(close);
        AddChild(new TextureRect { Position=new(900,32), Texture=Textures["catalog-help"], MouseFilter=MouseFilterEnum.Ignore });
        AddChild(new GameMessageBar { Textures=Textures,Text=HintText.Length > 0 ? HintText : "请选择欲观看其情报的"+(Category==InformationCategory.Forces?"势力":"都市")+"。" });
        MouseExited+=()=>Hover(null);
    }
    private Label Label(string text,Vector2 p,Vector2 size,int font=16,HorizontalAlignment alignment=HorizontalAlignment.Center)
    {
        var label=new Label { Text=text,Position=p,Size=size,ClipText=true,VerticalAlignment=VerticalAlignment.Center,
            HorizontalAlignment=alignment,MouseFilter=MouseFilterEnum.Ignore };
        AddChild(label);_text.Apply(label,GameTextStyles.CatalogRow with { FontSize=font });return label;
    }
    private void Button(string text,Vector2 p,Action action,bool mapToggle)
    { var button=new InformationActionButton {Title=text,MapToggle=mapToggle,
        TextRole=mapToggle?GameButtonTextRole.MapMode:GameButtonTextRole.ConfirmNo,
        Textures=Textures,Position=p,Size=mapToggle?new(108,32):new(80,48)};button.Pressed+=action;AddChild(button); }
    private static Vector2 Center(InformationMapMarker m)=>Origin+new Vector2(2*(m.X-m.Y)+401,m.X+m.Y);
    private int? Target(Vector2 p)
    {
        // The marker may extend past its territory, but never broadens the candidate filter.
        var marker = _markers.LastOrDefault(m => new Rect2(Center(m)-new Vector2(12,12),new(24,24)).HasPoint(p));
        if (marker is not null) return marker.TargetId;
        var local = p - Origin;
        foreach (var area in _markers)
            if (Service.Roles.TryGetValue($"overview-city-{area.CityId}",out var asset) &&
                Service.Hit(asset,(int)Math.Floor(local.X),(int)Math.Floor(local.Y))) return area.TargetId;
        return null;
    }
    public void RefreshData()
    {
        _markers = AdministrativeMap.Markers(ReadView(), Category, Candidates);
        UpdateSummary(Target(GetLocalMousePosition()));
    }
    private void Hover(int? id)
    {
        if (_hovered == id) return;
        UpdateSummary(id);
    }
    private void UpdateSummary(int? id)
    {
        _hovered = id;
        if(id is int selected)_fields=AdministrativeMap.Summary(ReadView(),Texts,new(Category,selected));
        for(int i=0;i<_values.Count;i++)_values[i].Text=id is null?"":_fields[i].Value;
        QueueRedraw();
    }
    public override void _GuiInput(InputEvent input)
    {
        AcceptEvent();
        if(input is InputEventMouseMotion motion)Hover(Target(motion.Position));
        if(input is InputEventMouseButton {ButtonIndex:MouseButton.Left} click)
        {
            var target = Target(click.Position);
            if (click.Pressed) _pressedTarget = target;
            else
            {
                var pressed = _pressedTarget; _pressedTarget = null;
                if (target is int id && pressed == id)
                {
                    if (Selection is null) { Activated(id); return; }
                    Selection.Toggle(id); RefreshSelection();
                }
            }
            QueueRedraw();
        }
    }
    private bool SelectionAccepted => Selection is { Selected.Count: > 0 } && (CanConfirmSelection?.Invoke(Selection.Selected) ?? true);
    private void RefreshSelection()
    {
        if (_confirmSelection is not null) _confirmSelection.Disabled = !SelectionAccepted;
        if (_clearSelection is not null) _clearSelection.Disabled = Selection!.Selected.Count == 0;
        QueueRedraw();
    }
    public override void _Process(double delta) => Hover(Target(GetLocalMousePosition()));
    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right } mouse)
        {
            GetViewport().SetInputAsHandled();
            if (!mouse.Pressed) Closed();
            return;
        }
        if(input is InputEventKey {Pressed:true,Echo:false,Keycode:Key.Escape})
        { GetViewport().SetInputAsHandled();Closed(); }
    }
    private void Tile(string role,Rect2 r)
    {
        SkinPainter.Tile(this, Textures[role], r);
    }
    private void Strip(string role,Vector2 point,float width)
    {
        var left=Textures[role+"-left"]; var right=Textures[role+"-right"];
        Tile(role+"-middle",new(point+new Vector2(left.GetWidth(),0),new(width-left.GetWidth()-right.GetWidth(),left.GetHeight())));
        DrawTexture(left,point); DrawTexture(right,point+new Vector2(width-right.GetWidth(),0));
    }
    public override void _Draw()
    {
        DrawRect(new(0,0,1024,768),new Color(0,0,0,.45f));
        Tile("information-background",new(56,24,896,720));
        Tile("information-frame-top",new(96,24,816,8));Tile("information-frame-bottom",new(96,736,816,8));
        Tile("information-frame-left",new(48,160,24,440));Tile("information-frame-right",new(948,160,16,440));
        DrawTexture(Textures["information-frame-top-left"],new(8,8));DrawTexture(Textures["information-frame-top-right"],new(728,8));
        DrawTexture(Textures["information-frame-bottom-left"],new(8,544));DrawTexture(Textures["information-frame-bottom-right"],new(728,544));
        Strip("information-title",new(16,8),264);
        DrawTexture(Textures["information-frame-ornament"],new(0,8));
        Strip("information-footer",new(Selection is null ? 732 : 644,680),Selection is null ? 260 : 348);
        DrawTexture(Textures["overview-terrain"],Origin);
        var forces=ReadView().Forces.ToDictionary(f=>f.Id);
        foreach(var marker in _markers)
        {
            int color=marker.OwnerId is int owner?forces[owner].ColorCode:50;
            string role=$"overview-city-{marker.CityId}";
            if(Textures.Assets.ContainsKey(role))
            {
                var tint = new Color(Skin.Colors[$"overview-force-{color}"]);
                if (marker.TargetId is int target && _hovered == target) tint = tint.Lerp(Colors.White,.45f);
                DrawTexture(Textures[role],Origin,tint);
            }
        }
        DrawTexture(Textures["overview-borders"],Origin);
        if (Selection is not null)
        {
            var selected = Textures["information-map-selected"];
            foreach (var marker in _markers)
                if (marker.TargetId is int id && Selection.Selected.Contains(id))
                    DrawTexture(selected,Center(marker)-selected.GetSize()/2);
        }
        foreach(var marker in _markers)
        {
            int color=marker.TargetId is null?50:marker.OwnerId is int owner?forces[owner].ColorCode:50;
            string role = Category == InformationCategory.Cities ? "information-map-city" : $"information-map-force-{color}";
            int frame = marker.TargetId is null ? 3 :
                _hovered == marker.TargetId ? (_pressedTarget == marker.TargetId ? 2 : 1) : 0;
            var t=Textures[role+(frame == 0 ? "" : $"-{frame}")];
            DrawTexture(t,Center(marker)-t.GetSize()/2);
        }
        DrawTexture(Textures["information-summary"],SummaryOrigin);
    }
    public override void _ExitTree()=>_text.Dispose();
}
