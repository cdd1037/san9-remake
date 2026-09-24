using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

/// <summary>Administrative detail sections and a shared, fixed-size candidate sidebar.</summary>
public partial class AdministrativeDetailView : Control
{
    public DetailSelectionState State { get; init; } = null!;
    public Func<int, AdministrativeDetailData> Read { get; init; } = null!;
    public Func<int, string> ReadName { get; init; } = null!;
    public Func<InformationLink, bool> Available { get; init; } = null!;
    public Action<InformationLink> Navigate { get; init; } = null!;
    public Action<int> CurrentChanged { get; init; } = null!;
    public Action<int> Locate { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public string Title { get; init; } = "";
    public TextureService Service { get; init; } = null!;
    public IReadOnlyDictionary<string, Texture2D> Textures { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private readonly List<DetailNameButton> _names = [];
    private readonly List<Rect2> _panels = [];
    private Control _body = null!;
    private SkinnedScrollBar _scroll = null!;
    private AdministrativeDetailData _data = null!;
    private bool _refreshing;
    public override void _Ready()
    {
        Size = new(1024,768); MouseFilter = MouseFilterEnum.Stop;
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        for (int row = 0; row < DetailSelectionState.VisibleRows; row++)
        {
            int slot = row;
            var name = new DetailNameButton { Textures = Textures, Position = new(890,124 + row * 20), Size = new(96,16) };
            name.MouseEntered += () => { State.PointAt(State.Candidates[State.Offset + slot]); RefreshNames(); };
            name.Pressed += () => Activate(State.Candidates[State.Offset + slot]);
            AddChild(name); _names.Add(name);
        }
        _scroll = new SkinnedScrollBar { Skin = ScrollBarSkin.FromCatalog(Textures), Position = new(990,124), Size = new(16,498), ContextRequested = Closed };
        AddChild(_scroll); _scroll.Range.Page = DetailSelectionState.VisibleRows;
        _scroll.Range.MaxValue = Math.Max(DetailSelectionState.VisibleRows, State.Candidates.Count);
        _scroll.Range.ValueChanged += value => { if (!_refreshing) { State.Scroll((int)value); RefreshNames(); } };
        var close = new DetailActionButton { Title = "关闭", Role = "close", Textures = Textures, Position = new(875,687), Size = new(108,44) };
        close.Pressed += Closed; AddChild(close);
        var icon = new TextureButton { Position = new(964,56), TextureNormal = Textures["detail-close-icon"],
            TextureHover = Textures["detail-close-icon-1"], TexturePressed = Textures["detail-close-icon-2"] };
        icon.Pressed += Closed; AddChild(icon);
        RefreshNames(); RefreshBody();
    }
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
        _scroll.Visible = State.Candidates.Count > DetailSelectionState.VisibleRows;
        _scroll.Range.Value = State.Offset; _scroll.UpdateArrows(); _refreshing = false;
    }
    private void Label(string text, Rect2 rect, int size = 16, bool heading = false)
    {
        var label = new Label { Text = text, TooltipText = text, Position = rect.Position, Size = rect.Size, ClipText = true,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Pass };
        _body.AddChild(label); _text.Apply(label, GameTextStyles.CatalogRow with { FontSize = size,
            Foreground = heading ? new Color("d8c898") : new Color("f0f0f0") });
    }
    private void Section(InformationSection section, Rect2 rect, int columns = 1)
    {
        _panels.Add(rect); Label(section.Title, new(rect.Position + new Vector2(16,8), new(rect.Size.X-32,24)), heading: true);
        float width = (rect.Size.X - 32) / columns;
        int rows = (section.Fields.Length + columns - 1) / columns;
        for (int i = 0; i < section.Fields.Length; i++)
        {
            var point = rect.Position + new Vector2(16 + i / rows * width, 36 + i % rows * 27);
            float labelWidth = Math.Min(96, width * .43f);
            Label(section.Fields[i].Label, new(point, new(labelWidth,24)), heading: true);
            Label(section.Fields[i].Value, new(point + new Vector2(labelWidth,0), new(width-labelWidth-8,24)));
        }
    }
    private void RefreshBody()
    {
        _data = Read(State.CurrentId); _panels.Clear();
        foreach (Node node in _body.GetChildren()) { _body.RemoveChild(node); node.QueueFree(); }
        Label(Title + "  " + _data.Name, new(90,45,720,40), 24);
        if (_data.PortraitRole is { } role)
        {
            _body.AddChild(new AsyncPortrait { Service = Service, Role = role, Position = new(96,118), Size = new(120,144),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
            Section(_data.Sections[0], new(246,100,566,180), 2);
            for (int i = 1; i < 4; i++) Section(_data.Sections[i], new(76 + (i-1)*248,294,240,174));
            if (_data.Sections.Length > 4) Section(_data.Sections[4], new(76,484,736,158), 2);
        }
        else
        {
            Section(_data.Sections[0], new(76,100,360,262)); Section(_data.Sections[1], new(452,100,360,262));
            Section(_data.Sections[2], new(76,378,360,262));
            Section(_data.Sections[3], new(452,378,360,152));
            if (_data.Sections.Length > 4) Section(_data.Sections[4], new(452,544,360,96));
        }
        for (int i = 0; i < _data.Links.Length; i++)
        {
            var link = _data.Links[i];
            var button = new InformationActionButton { Title = link.Label, Textures = Textures, Position = new(76+i*102,678), Size = new(96,32), Disabled = !Available(link) };
            button.Pressed += () => Navigate(link); _body.AddChild(button);
        }
        var map = new InformationActionButton { Title = "地图", Textures = Textures, Position = new(714,678), Size = new(96,32), Disabled = _data.MapSettlementId is null };
        map.Pressed += () => { if (_data.MapSettlementId is int id) Locate(id); }; _body.AddChild(map); QueueRedraw();
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
        var texture = Textures["detail-" + role];
        for (float y=0; y<rect.Size.Y; y+=texture.GetHeight())
        for (float x=0; x<rect.Size.X; x+=texture.GetWidth())
        { var size = new Vector2(Math.Min(texture.GetWidth(),rect.Size.X-x),Math.Min(texture.GetHeight(),rect.Size.Y-y)); DrawTextureRectRegion(texture,new(rect.Position+new Vector2(x,y),size),new(Vector2.Zero,size)); }
    }
    private void Panel(Rect2 r)
    {
        Tile("panel-center",r);
        Tile("panel-top",new(r.Position+new Vector2(20,0),new(r.Size.X-40,20)));
        Tile("panel-bottom",new(r.Position+new Vector2(20,r.Size.Y-20),new(r.Size.X-40,20)));
        Tile("panel-left",new(r.Position+new Vector2(0,20),new(20,r.Size.Y-40)));
        Tile("panel-right",new(r.Position+new Vector2(r.Size.X-20,20),new(20,r.Size.Y-40)));
        DrawTexture(Textures["detail-panel-top-left"],r.Position); DrawTexture(Textures["detail-panel-top-right"],new(r.End.X-20,r.Position.Y));
        DrawTexture(Textures["detail-panel-bottom-left"],new(r.Position.X,r.End.Y-20)); DrawTexture(Textures["detail-panel-bottom-right"],r.End-new Vector2(20,20));
    }
    public override void _Draw()
    {
        DrawTexture(Textures["detail-background"],Vector2.Zero);
        DrawTextureRectRegion(Textures["detail-treasure-list-top"],new(856,72,168,492),new(0,0,168,492));
        DrawTexture(Textures["detail-treasure-list-bottom"],new(856,564));
        foreach (var panel in _panels) Panel(panel);
    }
    public override void _ExitTree() => _text.Dispose();
}
