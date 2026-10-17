using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

/// <summary>Administrative detail layouts share navigation, not a generic section grid.</summary>
public partial class AdministrativeDetailView : FixedPage, IInformationPage
{
    protected override Color PageBackground => Colors.Black;
    public DetailSelectionState State { get; init; } = null!;
    public InformationCategory Category { get; init; }
    public Func<int, AdministrativeDetailData> Read { get; init; } = null!;
    public Func<int, string> ReadName { get; init; } = null!;
    public Func<InformationLink, bool> Available { get; init; } = null!;
    public Action<InformationLink> Navigate { get; init; } = null!;
    public Action<int> CurrentChanged { get; init; } = null!;
    public Func<int, Control> Preview { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public string Title { get; init; } = "";
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private readonly List<DetailNameButton> _names = [];
    private readonly List<Rect2> _panels = [];
    private readonly List<(string Role, Rect2 Rect)> _strips = [];
    private Control _body = null!;
    private SkinnedScrollBar _scroll = null!;
    private AdministrativeDetailData _data = null!;
    private bool _refreshing;
    private int _titleExtra;
    private void Badge(string role, string text, Vector2 position, Vector2 size)
    {
        _body.AddChild(new TextureRect { Texture = Textures["detail-"+role+"-badge"], Position = position,
            Size = size, MouseFilter = MouseFilterEnum.Ignore });
        Label(text,new(position,size),24,HorizontalAlignment.Center);
    }
    public static int VisibleRows(InformationCategory category) => category switch { InformationCategory.Forces => 14, InformationCategory.Corps => 16, InformationCategory.Cities => 18, _ => 21 };
    private int Rows => VisibleRows(Category);
    public override void _Ready()
    {
        Size = new(1024,768); MouseFilter = MouseFilterEnum.Stop;
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        for (int row = 0; row < Rows; row++)
        {
            int slot = row;
            var name = new DetailNameButton { Textures = Textures, Position = new(902,124 + row * 20), Size = new(88,16) };
            name.MouseEntered += () => { State.PointAt(State.Candidates[State.Offset + slot]); RefreshNames(); };
            name.Pressed += () => Activate(State.Candidates[State.Offset + slot]);
            AddChild(name); _names.Add(name);
        }
        _scroll = new SkinnedScrollBar { Skin = ScrollBarSkin.FromCatalog(Textures), Position = new(990,124), Size = new(16,Rows * 20 - 2), ContextRequested = Closed };
        AddChild(_scroll); _scroll.Range.Page = Rows;
        _scroll.Range.MaxValue = Math.Max(Rows, State.Candidates.Count);
        _scroll.Range.ValueChanged += value => { if (!_refreshing) { State.Scroll((int)value); RefreshNames(); } };
        var close = new DetailActionButton { Title = "关闭", Role = "close", Textures = Textures, Position = new(875,687), Size = new(108,44) };
        close.Pressed += Closed; AddChild(close);
        var icon = new TextureButton { Position = new(964,56), TextureNormal = Textures["detail-close-icon"],
            TextureHover = Textures["detail-close-icon-1"], TexturePressed = Textures["detail-close-icon-2"] };
        icon.Pressed += Closed; AddChild(icon);
        AddChild(new TextureRect { Position = new(932,32), Texture = Textures["detail-help"], MouseFilter = MouseFilterEnum.Ignore });
        AddChild(new GameMessageBar { Textures = Textures, Text = "现在显示的是" + Title + "。" });
        RefreshNames(); RefreshBody();
    }
    public void RefreshData() { _scroll.Range.MaxValue = Math.Max(Rows, State.Candidates.Count); RefreshNames(); RefreshBody(); }
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
        _scroll.Visible = State.Candidates.Count > Rows;
        _scroll.Range.Value = State.Offset; _scroll.UpdateArrows(); _refreshing = false;
    }
    private void Label(string text, Rect2 rect, int size = 16, HorizontalAlignment alignment = HorizontalAlignment.Left, Color? color = null)
    {
        var label = new Label { Text = text, TooltipText = text, Position = rect.Position, Size = rect.Size, ClipText = true,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = alignment, MouseFilter = MouseFilterEnum.Ignore };
        _body.AddChild(label); _text.Apply(label, GameTextStyles.CatalogRow with { FontSize = size, Foreground = color ?? GameTextStyles.CatalogRow.Foreground });
    }
    private AdministrativeField FieldData(string id) => _data.Sections.SelectMany(s => s.Fields).First(f => f.Id == id);
    private void Field(string id, float x, float y, float labelWidth, float valueWidth, string labelRole = "label")
    {
        _strips.Add((labelRole, new(x,y,labelWidth,24)));
        _strips.Add(("field", new(x+labelWidth+8,y,valueWidth,24)));
        var field = FieldData(id);
        Label(field.Label, new(x,y,labelWidth,24), alignment: HorizontalAlignment.Center);
        Label(field.Value, new(x+labelWidth+12,y,valueWidth-8,24), alignment: HorizontalAlignment.Right);
    }
    private void Column(string[] labels, float x, float y, float labelWidth, float valueWidth, string labelRole = "label")
    { for (int row = 0; row < labels.Length; row++) Field(labels[row], x, y + row * 32, labelWidth, valueWidth, labelRole); }
    private void RefreshBody()
    {
        _data = Read(State.CurrentId); _panels.Clear(); _strips.Clear();
        foreach (Node node in _body.GetChildren()) { _body.RemoveChild(node); node.QueueFree(); }
        (int titleWidth, int nameWidth) = Category switch {
            InformationCategory.Forces => (472,100), InformationCategory.Corps => (520,148),
            InformationCategory.Cities => (568,52), InformationCategory.Regions => (512,76),
            _ => throw new InvalidOperationException("Unsupported administrative detail.")
        };
        // Authored names can exceed the original examples; grow the frame instead of truncating them.
        int measured = (int)Math.Ceiling(GetThemeFont("font").GetStringSize(_data.Name,fontSize:24).X) + 4;
        int extra = Math.Max(0,measured-nameWidth);
        _strips.Add(("title", new(56,34,titleWidth+extra,60)));
        _strips.Add(("name", new(232,50,nameWidth+extra,28)));
        Label(Title, new(112,50,116,32),24); Label(_data.Name,new(234,50,nameWidth+extra-4,28),24,HorizontalAlignment.Center);
        _titleExtra = extra;
        if (Category is InformationCategory.Forces or InformationCategory.Corps) BuildAdministration(); else BuildLocation();
        var links = _data.Links.Where(l => l.OfficerId is null).ToArray();
        for (int i = 0; i < links.Length; i++)
        {
            var link = links[i];
            var button = new DetailActionButton { Title = link.Label, Role = "treasure", Textures = Textures,
                Position = new(888,629-(links.Length-1-i)*44), Size = new(120,40), Disabled = !Available(link) };
            button.Pressed += () => Navigate(link); _body.AddChild(button);
        }
        QueueRedraw();
    }
    private void BuildAdministration()
    {
        bool force = Category == InformationCategory.Forces;
        _panels.Add(new(76,100,force ? 592 : 624,156));
        _panels.Add(new(76,268,force ? 664 : 640,force ? 300 : 156));
        _strips.Add(("leader-label",new(96,120,48,24))); Label(force ? "君主" : "都督",new(96,120,48,24),alignment:HorizontalAlignment.Center);
        _body.AddChild(new TextureRect { Texture = Textures["detail-leader-background"], Position = new(152,120), MouseFilter = MouseFilterEnum.Ignore });
        Label(_data.LeaderName,new(164,120,172,24));
        if (_data.PortraitRole is { } role)
            _body.AddChild(new AsyncPortrait { Service = Service, Role = role, View = San9.Content.Images.PortraitView.Face,
                Position = new(160,148), Size = new(64,80), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale });
        if (_data.LeaderAbilities is { } abilities)
        {
            _body.AddChild(new OfficerAbilityRadar { Abilities = abilities, Position = new(258,162), Scale = new(52f/164,52f/164) });
            _body.AddChild(new TextureRect { Texture = Textures["detail-leader-radar-grid"], Position = new(258,162), MouseFilter = MouseFilterEnum.Ignore });
            Label("统率",new(264,146,40,16),16,HorizontalAlignment.Center,new Color("f8f850"));
            Label("武力",new(228,180,32,16),16,HorizontalAlignment.Center,new Color("e85440"));
            Label("智力",new(264,212,40,16),16,HorizontalAlignment.Center,new Color("60d868"));
            Label("政治",new(308,180,32,16),16,HorizontalAlignment.Center,new Color("5098d8"));
        }
        if (force)
        {
            Column(["country","rank","trust","tactics"],368,120,48,72,"admin-label");
            Column(["adviser","corps","cities","officers"],510,120,48,88,"admin-label");
        }
        else
        {
            Column(["force","cities","officers"],368,120,48,88,"admin-label");
            Column(["policy","target"],526,120,48,104,"admin-label");
        }
        Column(["population","draft","soldiers","wounded"],96,288,96,88);
        Column(["money","money-cost","money-income","money-forecast"],304,288,96,force ? 104 : 80);
        Column(["food","food-cost","food-income","food-forecast"],force ? 528 : 504,288,96,88);
        if (force)
        {
            Column(["relation","alliance-leader","alliance-target","alliance-days"],96,428,96,88);
            Column(["requester","request-target","request-days"],304,428,96,104);
        }
    }
    private void BuildLocation()
    {
        bool city = Category == InformationCategory.Cities;
        Badge("status",_data.Status == "正常" ? "一般" : _data.Status,new((city ? 372 : 432)+_titleExtra,50),new(72,32));
        if (city && _data.Feature != "—") Badge("training",_data.Feature + "修行",new(444+_titleExtra,50),new(120,32));
        _panels.Add(new(76,100,548,292));
        var preview = Preview(State.CurrentId); preview.Position = new(94,116); preview.Size = new(512,256); _body.AddChild(preview);
        _panels.Add(new(632,100,city ? 232 : 200,city ? 332 : 292));
        Column(city ? ["province","force","corps-name","commander","serving","captives","free"] : ["province","city","force","corps-name","commander","serving","captives","free"],
            652,120,city ? 96 : 64,88);
        if (city)
        {
            Column(["merchant","emperor"],652,356,96,88);
            _panels.Add(new(76,400,456,300));
            Column(["public-order","commerce","cultivation"],96,420,64,96);
            Column(["population","draft","soldiers","wounded"],280,420,96,128);
            Column(["durability","morale","trait","tactics"],96,560,64,96);
            Column(["draft-growth","money-income","food-income"],280,560,96,128);
        }
        else
        {
            _panels.Add(new(76,400,440,156));
            Column(["facility","soldiers","wounded","morale"],96,420,80,128);
            Column(["durability","recovery","tactics","trait"],328,420,64,96);
        }
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
        else if (key.Keycode == Key.Enter && State.FocusedId is int id) { GetViewport().SetInputAsHandled(); Activate(id); }
    }
    private void Tile(string role, Rect2 rect)
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
        DrawTexture(Textures["detail-background"],Vector2.Zero);
        float topHeight = Rows * 20 - 8;
        DrawTextureRectRegion(Textures["detail-list-top"],new(872,72,152,topHeight),new(0,0,152,topHeight));
        DrawTexture(Textures["detail-list-bottom"],new(872,72+topHeight));
        foreach (var panel in _panels) Panel(panel);
        foreach (var strip in _strips) Strip(strip.Role,strip.Rect);
    }
    public override void _ExitTree() => _text.Dispose();
}
