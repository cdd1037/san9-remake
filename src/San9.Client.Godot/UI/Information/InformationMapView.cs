using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Client.Godot;

public partial class InformationMapView : Control, IInformationPage
{
    public GameSession Session { get; init; } = null!;
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
    private static readonly Vector2 Origin = new(88,88);
    private InformationMapMarker[] _markers = [];
    private InformationField[] _fields = [];
    private int? _hovered;
    private int? _pressedTarget;
    private readonly GameTextPresenter _text = new();
    private readonly List<Label> _values = [];
    public override void _Ready()
    {
        Size=new(1024,768); MouseFilter=MouseFilterEnum.Stop;
        _markers=AdministrativeMap.Markers(Session.View,Category,Candidates);
        Label(InformationQuery.MenuTitle(Category)[1..]+"一览",new(88,22),new(160,36),24);
        if(Candidates.Length>0) _fields=AdministrativeMap.Summary(Session.View,Texts,new(Category,Candidates[0]));
        for(int i=0;i<_fields.Length;i++)
        {
            var p=new Vector2(110+i/4*192,516+i%4*32);
            Label(_fields[i].Label,p,new(88,24));
            _values.Add(Label("",p+new Vector2(88,0),new(100,24),16,HorizontalAlignment.Right));
        }
        Button("一览",new(612,680),ShowList,true); Button("关闭",new(808,696),Closed,false);
        var close=new TextureButton { Position=new(932,56), TextureNormal=Textures["information-action-close"],
            TextureHover=Textures["information-action-close-1"],TexturePressed=Textures["information-action-close-2"] };
        close.Pressed+=Closed; AddChild(close);
        AddChild(new TextureRect { Position=new(900,32), Texture=Textures["catalog-help"], MouseFilter=MouseFilterEnum.Ignore });
        AddChild(new GameMessageBar { Textures=Textures,Text="请选择欲观看其情报的"+(Category==InformationCategory.Forces?"势力":"都市")+"。" });
        MouseExited+=()=>Hover(null);
    }
    private Label Label(string text,Vector2 p,Vector2 size,int font=16,HorizontalAlignment alignment=HorizontalAlignment.Center)
    {
        var label=new Label { Text=text,Position=p,Size=size,ClipText=true,VerticalAlignment=VerticalAlignment.Center,
            HorizontalAlignment=alignment,MouseFilter=MouseFilterEnum.Ignore };
        AddChild(label);_text.Apply(label,GameTextStyles.CatalogRow with { FontSize=font });return label;
    }
    private void Button(string text,Vector2 p,Action action,bool mapToggle)
    { var button=new InformationActionButton {Title=text,MapToggle=mapToggle,Textures=Textures,Position=p,Size=mapToggle?new(108,32):new(80,48)};button.Pressed+=action;AddChild(button); }
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
        _markers = AdministrativeMap.Markers(Session.View, Category, Candidates);
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
        if(id is int selected)_fields=AdministrativeMap.Summary(Session.View,Texts,new(Category,selected));
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
                if (target is int id && pressed == id) { Activated(id); return; }
            }
            QueueRedraw();
        }
    }
    public override void _Process(double delta) => Hover(Target(GetLocalMousePosition()));
    public override void _Input(InputEvent input)
    {
        if(input is InputEventKey {Pressed:true,Keycode:Key.Escape} || input is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Right})
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
        Strip("information-footer",new(732,680),260);
        DrawTexture(Textures["overview-terrain"],Origin);
        var forces=Session.View.Forces.ToDictionary(f=>f.Id);
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
        foreach(var marker in _markers)
        {
            int color=marker.TargetId is null?50:marker.OwnerId is int owner?forces[owner].ColorCode:50;
            string role = Category == InformationCategory.Cities ? "information-map-city" : $"information-map-force-{color}";
            int frame = marker.TargetId is null ? 3 : _hovered == marker.TargetId ? (_pressedTarget == marker.TargetId ? 2 : 1) : 0;
            var t=Textures[role+(frame == 0 ? "" : $"-{frame}")];
            DrawTexture(t,Center(marker)-t.GetSize()/2);
        }
        DrawRect(new(96,500,776,152),new Color("635238"),false,2);
        for(int i=0;i<16;i++)
        {
            var p=new Vector2(108+i/4*192,516+i%4*32);
            Strip("detail-field",p,188);
            Strip("detail-label",p+new Vector2(2,0),88);
        }
    }
    public override void _ExitTree()=>_text.Dispose();
}
