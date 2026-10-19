using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class InformationMapView : FixedPage, IInformationPage
{
    protected override Color PageBackground => new(0, 0, 0, .45f);
    public Func<StrategySnapshot> ReadView { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public InformationCategory Category { get; init; }
    public Func<int[]> ReadCandidates { get; init; } = null!;
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
    private OverviewMapLayer _map = null!;
    private InformationMapMarker[] _markers = [];
    private int? _hovered;
    private int? _pressedTarget;
    private readonly GameTextPresenter _text = new();
    private readonly List<Label> _values = [];
    private readonly Node2D _territories = new();
    private readonly Node2D _borders = new();
    private readonly Node2D _markerLayer = new();
    private ShaderMaterial _highlight = null!;
    private ImageTexture? _targetMask;
    private readonly Dictionary<int, Vector3> _targetCodes = [];
    private StrategySnapshot? _snapshot;
    private AdministrativeCatalog _catalog = null!;
    private readonly Dictionary<int, InformationField[]> _summaries = [];
    private Vector2? _lastMousePosition;

    private InformationField[] Summary(int id)
    {
        if (!_summaries.TryGetValue(id, out var fields))
            _summaries.Add(id, fields = AdministrativeMap.Summary(_catalog, new(Category, id)));
        return fields;
    }

    public override void _Ready()
    {
        Size=new(1024,768); MouseFilter=MouseFilterEnum.Stop;
        _map = new(Service, Skin);
        _highlight = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/information_map_highlight.gdshader") };
        _territories.Material = _highlight;
        _territories.Draw += () => _map.DrawTerritories(_territories, Origin);
        _borders.Draw += () => _map.DrawBorders(_borders, Origin);
        _markerLayer.Draw += DrawMarkers;
        AddChild(_territories); AddChild(_borders); AddChild(_markerLayer);
        RefreshData();
        Label(Title.Length > 0 ? Title : InformationQuery.MenuTitle(Category)[1..]+"一览",new(88,22),new(192,36),24);
        var candidates = ReadCandidates();
        var fields = candidates.Length > 0 ? Summary(candidates[0]) : [];
        for(int i=0;i<fields.Length;i++)
        {
            int column = i/4;
            var p=SummaryOrigin+new Vector2(SummaryColumns[column],16+i%4*32);
            Label(fields[i].Label,p+new Vector2(2,0),new(80,24));
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
        var close=new TextureButton { Position=new(932,56), TextureNormal=Textures.Get("information-action-close", this),
            TextureHover=Textures.Get("information-action-close-1", this),TexturePressed=Textures.Get("information-action-close-2", this) };
        close.Pressed+=Closed; AddChild(close);
        AddChild(new TextureRect { Position=new(900,32), Texture=Textures.Get("catalog-help", this), MouseFilter=MouseFilterEnum.Ignore });
        AddChild(new GameMessageBar { Textures=Textures,Text=HintText.Length > 0 ? HintText : "请选择欲观看其情报的"+(Category==InformationCategory.Forces?"势力":"都市")+"。" });
        MouseExited+=()=>Hover(null);
        UpdateSummary(_hovered);
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
    private Vector2 Center(InformationMapMarker m)=>Origin+_map.Point(m.X,m.Y);
    private int? Target(Vector2 p)
    {
        // The marker may extend past its territory, but never broadens the candidate filter.
        var marker = _markers.LastOrDefault(m => new Rect2(Center(m)-new Vector2(12,12),new(24,24)).HasPoint(p));
        if (marker is not null) return marker.TargetId;
        var local = p - Origin;
        int? city = Service.OverviewMap.CityAt(local.X / 800, local.Y / 400);
        return city is int id ? _markers.FirstOrDefault(m => m.CityId == id)?.TargetId : null;
    }
    public void RefreshData()
    {
        var view = ReadView();
        if (!ReferenceEquals(_snapshot, view))
        {
            _snapshot = view;
            _catalog = new(view, Texts);
            _summaries.Clear();
        }
        _markers = AdministrativeMap.Markers(view, Category, ReadCandidates());
        _map.Update(view);
        RefreshTargetMask();
        _lastMousePosition = GetLocalMousePosition();
        UpdateSummary(Target(_lastMousePosition.Value));
        _territories.QueueRedraw(); _borders.QueueRedraw();
    }
    private void RefreshTargetMask()
    {
        _targetCodes.Clear();
        var colors = new Dictionary<int, OverviewColor>();
        foreach (var marker in _markers)
        {
            if (marker.TargetId is not int id) continue;
            if (!_targetCodes.TryGetValue(id, out var code))
            {
                // Dense display codes encode target membership, independently of domain IDs.
                int index = _targetCodes.Count;
                code = new(index & 255, (index >> 8) & 255, (index >> 16) & 255);
                _targetCodes.Add(id, code);
            }
            colors.Add(marker.CityId, new((byte)code.X, (byte)code.Y, (byte)code.Z));
        }
        var pixels = Service.OverviewMap.Territories(colors);
        using var image = Image.CreateFromData(pixels.Width, pixels.Height, false, Image.Format.Rgba8, pixels.Pixels);
        if (_targetMask is null) _targetMask = ImageTexture.CreateFromImage(image);
        else _targetMask.Update(image);
        _highlight.SetShaderParameter("target_mask", _targetMask);
    }
    private void Hover(int? id)
    {
        if (_hovered == id) return;
        UpdateSummary(id);
    }
    private void UpdateSummary(int? id)
    {
        _hovered = id;
        _highlight.SetShaderParameter("hovered_code", id is int target && _targetCodes.TryGetValue(target, out var code) ? code : new Vector3(-1, -1, -1));
        var fields = id is int selected ? Summary(selected) : null;
        for(int i=0;i<_values.Count;i++)_values[i].Text=fields is null?"":fields[i].Value;
        _markerLayer.QueueRedraw();
    }
    public override void _GuiInput(InputEvent input)
    {
        AcceptEvent();
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
            _markerLayer.QueueRedraw();
        }
    }
    private bool SelectionAccepted => Selection is { Selected.Count: > 0 } && (CanConfirmSelection?.Invoke(Selection.Selected) ?? true);
    private void RefreshSelection()
    {
        if (_confirmSelection is not null) _confirmSelection.Disabled = !SelectionAccepted;
        if (_clearSelection is not null) _clearSelection.Disabled = Selection!.Selected.Count == 0;
        _markerLayer.QueueRedraw();
    }
    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        var position = GetLocalMousePosition();
        if (_lastMousePosition == position) return;
        _lastMousePosition = position;
        Hover(Target(position));
    }
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
        SkinPainter.Tile(this, Textures.Get(role, this), r);
    }
    private void Strip(string role,Vector2 point,float width)
    {
        var left=Textures.Get(role+"-left", this); var right=Textures.Get(role+"-right", this);
        Tile(role+"-middle",new(point+new Vector2(left.GetWidth(),0),new(width-left.GetWidth()-right.GetWidth(),left.GetHeight())));
        DrawTexture(left,point); DrawTexture(right,point+new Vector2(width-right.GetWidth(),0));
    }
    public override void _Draw()
    {
        Tile("information-background",new(56,24,896,720));
        Tile("information-frame-top",new(96,24,816,8));Tile("information-frame-bottom",new(96,736,816,8));
        Tile("information-frame-left",new(48,160,24,440));Tile("information-frame-right",new(948,160,16,440));
        DrawTexture(Textures.Get("information-frame-top-left", this),new(8,8));DrawTexture(Textures.Get("information-frame-top-right", this),new(728,8));
        DrawTexture(Textures.Get("information-frame-bottom-left", this),new(8,544));DrawTexture(Textures.Get("information-frame-bottom-right", this),new(728,544));
        Strip("information-title",new(16,8),264);
        DrawTexture(Textures.Get("information-frame-ornament", this),new(0,8));
        Strip("information-footer",new(Selection is null ? 732 : 644,680),Selection is null ? 260 : 348);
        DrawTexture(Textures.Get("overview-terrain", this), Origin);
        DrawTexture(Textures.Get("information-summary", this),SummaryOrigin);
    }
    private void DrawMarkers()
    {
        var forces=ReadView().Forces.ToDictionary(f=>f.Id);
        if (Selection is not null)
        {
            var selected = Textures.Get("information-map-selected", this);
            foreach (var marker in _markers)
                if (marker.TargetId is int id && Selection.Selected.Contains(id))
                    _markerLayer.DrawTexture(selected,Center(marker)-selected.GetSize()/2);
        }
        foreach(var marker in _markers)
        {
            int color=marker.TargetId is null?50:marker.OwnerId is int owner?forces[owner].ColorCode:50;
            string role = Category == InformationCategory.Cities ? "information-map-city" : $"information-map-force-{color}";
            int frame = marker.TargetId is null ? 3 :
                _hovered == marker.TargetId ? (_pressedTarget == marker.TargetId ? 2 : 1) : 0;
            var t=Textures.Get(role+(frame == 0 ? "" : $"-{frame}"), this);
            _markerLayer.DrawTexture(t,Center(marker)-t.GetSize()/2);
        }
    }
    public override void _ExitTree()
    {
        _territories.Material = null;
        _highlight.Dispose(); _targetMask?.Dispose();
        _map.Dispose(); _text.Dispose();
    }
}
