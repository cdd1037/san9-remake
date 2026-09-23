using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

/// <summary>Shared catalog window. Domain queries and navigation belong to its caller.</summary>
public partial class CatalogView : Control
{
    public string Title { get; init; } = "";
    public string HintText { get; init; } = "";
    public IReadOnlyList<CatalogPage> Pages { get; init; } = [];
    public Func<int, CatalogRow[]> ReadRows { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public Func<int, bool>? PortraitDeceased { get; init; }
    public Func<int, string?>? Portrait { get; init; }
    public IReadOnlyDictionary<string, Texture2D> Textures { get; init; } = null!;
    public Vector2 PortraitSize { get; init; } = new(64, 80);
    public Action<int>? Activated { get; init; }
    public Action<int>? CurrentChanged { get; init; }
    public Action? Closed { get; init; }
    public int InitialPage { get; init; }
    public int? InitialCurrentId { get; init; }
    public int InitialOffset { get; init; }
    public Action<int, int, int?>? ViewChanged { get; init; }
    public CatalogTableState TableState { get; init; } = new();
    private CatalogTableState _state => TableState;
    private readonly GameTextPresenter _text = new();
    private readonly List<CatalogSkinButton> _headers = [];
    private readonly List<CatalogSkinButton> _tabs = [];
    private readonly List<Label[]> _cells = [];
    private Control _grid = null!;
    private SkinnedScrollBar _scroll = null!;
    private AsyncPortrait? _portrait;
    private Rect2 _body;
    private Vector2 _tableOrigin;
    private int _page;
    private bool _refreshing;
    private CatalogRow[]? _boundRows;
    private int _boundOffset = -1;
    private Label? _help;

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        int width = Math.Max(Pages.Max(p => p.Columns.Sum(c => c.Width)) + 16, 7 * 62 + 52) + 80;
        int height = 226 + 24 * CatalogTableState.VisibleRows + (Pages.Count > 7 ? 32 : 0);
        _body = new((1024 - width) / 2, (768 - height) / 2, width, height);
        _tableOrigin = _body.Position + new Vector2(40, Pages.Count > 7 ? 148 : 116);
        var title = LabelAt(Title, _body.Position + new Vector2(-16, -8), new(248, 56), 24);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        for (int i = 0; i < Pages.Count; i++)
        {
            int page = i;
            var tab = new CatalogSkinButton { Textures = Textures, Kind = CatalogButtonKind.Tab, Title = Pages[i].Title,
                Position = _body.Position + new Vector2(86 + i % 7 * 62, 75 + i / 7 * 32), Size = new(58, 26) };
            tab.Pressed += () => ChangePage(page); AddChild(tab); _tabs.Add(tab);
        }
        var close = new InformationActionButton { Title = "关闭", Textures = Textures,
            Position = new(_body.End.X - 160, _body.End.Y - 48), Size = new(80, 36) };
        close.Pressed += () => Closed?.Invoke(); AddChild(close);
        IconButton("information-action-close", new(_body.End.X - 40, _body.Position.Y + 32), () => Closed?.Invoke());
        IconButton("catalog-help", new(_body.End.X - 72, _body.Position.Y + 8), ToggleHelp);
        if (Portrait is not null)
        {
            _portrait = new AsyncPortrait { Service = Service, View = San9.Content.Images.PortraitView.Face,
                IsDeceased = () => _state.Current is { } current && (PortraitDeceased?.Invoke(current.Id) ?? false), Position = new(_body.Position.X + 28 - PortraitSize.X, _tableOrigin.Y + 42), Size = PortraitSize,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_portrait);
        }
        AddChild(new GameMessageBar { Textures = Textures, Text = HintText });
        ChangePage(InitialPage, InitialCurrentId, InitialOffset);
    }

    private void IconButton(string role, Vector2 position, Action action)
    {
        var button = new TextureButton { Position = position, TextureNormal = Textures[role], TextureHover = Textures[role + "-1"], TexturePressed = Textures[role + "-2"] };
        button.Pressed += action; AddChild(button);
    }
    private Label LabelAt(string text, Vector2 position, Vector2 size, int fontSize = 16, Control? parent = null)
    {
        var label = new Label { Text = text, Position = position, Size = size, VerticalAlignment = VerticalAlignment.Center,
            ClipText = true, MouseFilter = MouseFilterEnum.Ignore };
        (parent ?? this).AddChild(label);
        _text.Apply(label, GameTextStyles.For(GameTextRole.Resource) with { FontSize = fontSize });
        return label;
    }
    private void ChangePage(int page, int? current = null, int offset = 0)
    {
        bool restoreSort = _grid is null;
        int sortColumn = _state.SortColumn;
        bool descending = _state.Descending;
        if (_grid is not null) { RemoveChild(_grid); _grid.QueueFree(); }
        _headers.Clear(); _cells.Clear();
        _boundRows = null;
        current ??= _state.Current?.Id;
        _page = page; _state.SetRows(ReadRows(page), current, offset);
        if (restoreSort && sortColumn >= 0 && sortColumn < Pages[page].Columns.Length)
        {
            _state.Sort(sortColumn); if (!descending) _state.Sort(sortColumn);
            if (current is int id) _state.PointAt(Array.FindIndex(_state.Rows, row => row.Id == id));
        }
        _grid = new Control { Position = _tableOrigin, MouseFilter = MouseFilterEnum.Ignore }; AddChild(_grid);
        var columns = Pages[page].Columns;
        int width = columns.Sum(c => c.Width), x = 0;
        for (int i = 0; i < columns.Length; i++)
        {
            int column = i;
            var header = new CatalogSkinButton { Textures = Textures, Kind = CatalogButtonKind.Header, Title = columns[i].Title,
                Position = new(x, 0), Size = new(columns[i].Width, 24) };
            header.Pressed += () => { _state.Sort(column); RefreshRows(); };
            _grid.AddChild(header); _headers.Add(header); x += columns[i].Width;
        }
        for (int row = 0; row < CatalogTableState.VisibleRows; row++)
        {
            var labels = new Label[columns.Length]; x = 0;
            for (int c = 0; c < columns.Length; c++)
            {
                labels[c] = LabelAt("", new(x + 4, 24 + row * 24), new(columns[c].Width - 8, 24), parent: _grid);
                _text.Apply(labels[c], GameTextStyles.CatalogRow);
                labels[c].HorizontalAlignment = columns[c].Alignment switch { CatalogAlignment.Right => HorizontalAlignment.Right, CatalogAlignment.Center => HorizontalAlignment.Center, _ => HorizontalAlignment.Left };
                x += columns[c].Width;
            }
            _cells.Add(labels);
        }
        _scroll = new SkinnedScrollBar { Skin = ScrollBarSkin.FromCatalog(Textures), Position = new(width, 24), Size = new(16, 360), ContextRequested = Closed };
        _grid.AddChild(_scroll);
        _scroll.Range.MaxValue = Math.Max(CatalogTableState.VisibleRows, _state.Rows.Length); _scroll.Range.Page = CatalogTableState.VisibleRows;
        _scroll.Range.ValueChanged += value => { if (!_refreshing) { _state.Scroll((int)value); RefreshRows(); } };
        for (int i = 0; i < _tabs.Count; i++) _tabs[i].Selected = i == page;
        RefreshRows();
    }
    private void RefreshRows()
    {
        _refreshing = true;
        // Selection changes do not rebind cell text. Sorting/reloading replaces Rows.
        if (!ReferenceEquals(_boundRows, _state.Rows) || _boundOffset != _state.Offset)
        {
            for (int r = 0; r < _cells.Count; r++)
            {
                int index = _state.Offset + r;
                for (int c = 0; c < _cells[r].Length; c++)
                    _cells[r][c].Text = index < _state.Rows.Length ? _state.Rows[index].Cells[c].Text : "";
            }
            _boundRows = _state.Rows; _boundOffset = _state.Offset;
        }
        for (int c = 0; c < _headers.Count; c++) _headers[c].SortDirection = _state.SortColumn == c ? _state.Descending ? -1 : 1 : 0;
        _scroll.Range.Value = _state.Offset; _scroll.UpdateArrows();
        if (_portrait is not null) _portrait.Role = _state.Current is { } selected ? Portrait!(selected.Id) : null;
        if (_state.Current is { } row) CurrentChanged?.Invoke(row.Id);
        ViewChanged?.Invoke(_page, _state.Offset, _state.Current?.Id);
        _refreshing = false; QueueRedraw();
    }
    private void ToggleHelp()
    {
        if (_help is not null) { _help.QueueFree(); _help = null; return; }
        _help = LabelAt("页签切换显示列；点击表头排序。\n上下键选择，滚轮或滚动条翻阅。\nCtrl+Tab 切换页签，回车查看详情。\nEsc 或右键返回。", _body.Position + new Vector2(80, 220), new(_body.Size.X - 160, 150));
        _help.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("161b1b"), ContentMarginLeft = 16 });
        _help.ZIndex = 5; _help.MouseFilter = MouseFilterEnum.Stop;
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true } mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Right) { AcceptEvent(); Closed?.Invoke(); return; }
            if (mouse.ButtonIndex is MouseButton.WheelDown or MouseButton.WheelUp)
            { AcceptEvent(); _state.Scroll(_state.Offset + (mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1)); RefreshRows(); return; }
            if (mouse.ButtonIndex == MouseButton.Left && RowAt(mouse.Position) is int index)
            { AcceptEvent(); _state.PointAt(index); RefreshRows(); Activated?.Invoke(_state.Current!.Id); }
        }
        if (input is InputEventMouseMotion motion && RowAt(motion.Position) is int hovered && hovered != _state.CurrentIndex)
        { _state.PointAt(hovered); RefreshRows(); }
    }
    private int? RowAt(Vector2 position)
    {
        var rect = new Rect2(_tableOrigin + new Vector2(0, 24), new(Pages[_page].Columns.Sum(c => c.Width), 360));
        if (!rect.HasPoint(position)) return null;
        int index = _state.Offset + (int)((position.Y - rect.Position.Y) / 24);
        return index < _state.Rows.Length ? index : null;
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        if (input is InputEventMouseButton { Pressed: true } mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Right)
            {
                GetViewport().SetInputAsHandled();
                if (_help is not null) ToggleHelp(); else Closed?.Invoke();
                return;
            }
            if (_help is null && mouse.ButtonIndex is MouseButton.WheelDown or MouseButton.WheelUp)
            {
                _state.Scroll(_state.Offset + (mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1));
                GetViewport().SetInputAsHandled(); RefreshRows(); return;
            }
        }
        if (input is not InputEventKey { Pressed: true } key) return;
        if (_help is not null && key.Keycode != Key.Escape) { GetViewport().SetInputAsHandled(); return; }
        // Consume input before callbacks can detach this view from the scene tree.
        if (key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            if (_help is not null) ToggleHelp(); else Closed?.Invoke();
        }
        else if (key.CtrlPressed && key.Keycode == Key.Tab)
        {
            GetViewport().SetInputAsHandled();
            ChangePage((_page + Pages.Count + (key.ShiftPressed ? -1 : 1)) % Pages.Count);
        }
        else if (key.Keycode == Key.Enter)
        {
            GetViewport().SetInputAsHandled();
            if (_state.Current is { } row) Activated?.Invoke(row.Id);
        }
        else
        {
            int index = key.Keycode switch { Key.Up => _state.CurrentIndex - 1, Key.Down => _state.CurrentIndex + 1,
                Key.Pageup => _state.CurrentIndex - 15, Key.Pagedown => _state.CurrentIndex + 15, Key.Home => 0, Key.End => _state.Rows.Length - 1, _ => int.MinValue };
            if (index == int.MinValue) return;
            GetViewport().SetInputAsHandled();
            _state.MoveTo(index); RefreshRows();
        }
    }

    private void Tile(string role, Rect2 rect)
    {
        var texture = Textures[role];
        for (float y = 0; y < rect.Size.Y; y += texture.GetHeight())
        for (float x = 0; x < rect.Size.X; x += texture.GetWidth())
        {
            var size = new Vector2(Math.Min(texture.GetWidth(), rect.Size.X - x), Math.Min(texture.GetHeight(), rect.Size.Y - y));
            DrawTextureRectRegion(texture, new(rect.Position + new Vector2(x,y), size), new(Vector2.Zero,size));
        }
    }
    private void Strip(string role, Vector2 point, float width)
    {
        var left = Textures[role + "-left"]; var right = Textures[role + "-right"];
        Tile(role + "-middle", new(point + new Vector2(left.GetWidth(),0), new(width-left.GetWidth()-right.GetWidth(),left.GetHeight())));
        DrawTexture(left,point); DrawTexture(right,point+new Vector2(width-right.GetWidth(),0));
    }
    public override void _Draw()
    {
        if (_grid is null) return;
        DrawRect(new(0,0,1024,768),new Color(0,0,0,.45f));
        DrawTextureRectRegion(Textures["information-background"], _body, new(Vector2.Zero,_body.Size));
        float x=_body.Position.X,y=_body.Position.Y,right=_body.End.X,bottom=_body.End.Y;
        Tile("information-frame-top",new(x+64,y-8,_body.Size.X-304,8));
        Tile("information-frame-bottom",new(x+208,bottom-16,_body.Size.X-448,24));
        Tile("information-frame-left",new(x-16,y+216,24,_body.Size.Y-432));
        Tile("information-frame-right",new(right-8,y+216,16,_body.Size.Y-432));
        DrawTexture(Textures["information-frame-top-left"],new(x-56,y-16));
        DrawTexture(Textures["information-frame-top-right"],new(right-240,y-16));
        DrawTexture(Textures["information-frame-bottom-left"],new(x-56,bottom-216));
        DrawTexture(Textures["information-frame-bottom-right"],new(right-240,bottom-216));
        Strip("information-title",new(x-40,y-16),264);
        DrawTexture(Textures["information-frame-ornament"],new(x-56,y-16));
        Strip("information-footer",new(right-236,bottom-64),260);
        var tabs = new Rect2(x+40,y+64,_body.Size.X-80,Pages.Count>7?80:48);
        Tile("catalog-tabs-top",new(tabs.Position+new Vector2(12,0),new(tabs.Size.X-24,8)));
        Tile("catalog-tabs-bottom",new(tabs.Position+new Vector2(12,tabs.Size.Y-8),new(tabs.Size.X-24,8)));
        Tile("catalog-tabs-left",new(tabs.Position+new Vector2(0,24),new(12,tabs.Size.Y-48)));
        Tile("catalog-tabs-right",new(tabs.Position+new Vector2(tabs.Size.X-12,24),new(12,tabs.Size.Y-48)));
        DrawTexture(Textures["catalog-tabs-top-left"],tabs.Position); DrawTexture(Textures["catalog-tabs-top-right"],new(tabs.End.X-12,tabs.Position.Y));
        DrawTexture(Textures["catalog-tabs-bottom-left"],new(tabs.Position.X,tabs.End.Y-24)); DrawTexture(Textures["catalog-tabs-bottom-right"],tabs.End-new Vector2(12,24));
        int width=Pages[_page].Columns.Sum(c=>c.Width);
        DrawTableFrame(width + 18);
        int current=_state.CurrentIndex-_state.Offset;
        if(current>=0 && current<15)
        {
            var point=_tableOrigin+new Vector2(0,28+current*24);
            var left = Textures["catalog-row-left-2"];
            var rightCap = Textures["catalog-row-right-2"];
            DrawTexture(left,point);
            DrawTexture(rightCap,point+new Vector2(width-rightCap.GetWidth(),0));
            // The source left cap ends in a transparent column; the middle covers it (RE-0151).
            int middleStart = left.GetWidth() - 1;
            Tile("catalog-row-middle-2",new(point+new Vector2(middleStart,0),new(width-rightCap.GetWidth()-middleStart,left.GetHeight())));
        }
        if(_portrait is not null) DrawTextureRect(Textures["catalog-portrait-frame"], new(_portrait.Position - new Vector2(10,12), PortraitSize + new Vector2(20,24)), false);
    }
    private void DrawTableFrame(int width)
    {
        Vector2 origin = _tableOrigin - Vector2.One;
        const int height = 390;
        foreach (string edge in new[] { "top", "bottom" })
        {
            bool bottom = edge == "bottom";
            float y = bottom ? 25 : 0;
            float h = bottom ? height - 25 : 28;
            var left = Textures["catalog-frame-" + edge + "-left"];
            var middle = Textures["catalog-frame-" + edge];
            var right = Textures["catalog-frame-" + edge + "-right"];
            float sourceY = bottom ? left.GetHeight() - h : 0;
            void Part(Texture2D texture, float x, float w) => DrawTextureRectRegion(texture,
                new(origin + new Vector2(x, y), new(w, h)), new(0, sourceY, w, h));
            for (float x = left.GetWidth(); x < width - right.GetWidth(); x += middle.GetWidth())
                Part(middle, x, Math.Min(middle.GetWidth(), width - right.GetWidth() - x));
            Part(left, 0, left.GetWidth()); Part(right, width - right.GetWidth(), right.GetWidth());
        }
    }
    public override void _ExitTree() => _text.Dispose();
}

public enum CatalogButtonKind { Header, Tab }
public partial class CatalogSkinButton : BaseButton
{
    public string Title { get; init; } = "";
    public CatalogButtonKind Kind { get; init; }
    public IReadOnlyDictionary<string, Texture2D> Textures { get; init; } = null!;
    public bool Selected { get; set; }
    public int SortDirection { get; set; }
    private Label _label = null!;
    private readonly GameTextPresenter _text = new();
    private (bool,bool,bool,int)? _previous;
    public override void _Ready()
    {
        FocusMode=FocusModeEnum.None;
        _label=new Label { Text=Title,Size=Size,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore };
        AddChild(_label); Refresh();
    }
    public override void _Process(double delta)=>Refresh();
    private void Refresh()
    {
        bool pressed=GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed;
        var state=(Selected,IsHovered(),pressed,SortDirection); if(_previous==state)return;_previous=state;
        var style=GameTextStyles.CatalogHeader(IsHovered(),pressed);
        if(Kind==CatalogButtonKind.Tab)style=GameTextStyles.CatalogTab(IsHovered(),Selected);
        _text.Apply(_label,style);_label.Position=pressed||(Kind==CatalogButtonKind.Tab&&Selected)?new(2,2):Vector2.Zero;QueueRedraw();
    }
    public override void _Draw()
    {
        int frame=Selected||GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed?2:IsHovered()?1:0;
        string suffix=frame==0?"":$"-{frame}";
        if(Kind==CatalogButtonKind.Tab){DrawTextureRectRegion(Textures["catalog-tab"+suffix],new(Vector2.Zero,Size),new(Vector2.Zero,Size));return;}
        DrawTexture(Textures["catalog-header-left"+suffix],Vector2.Zero);
        var mid=Textures["catalog-header-middle"+suffix];
        for(float x=8;x<Size.X-8;x+=mid.GetWidth())
        {var size=new Vector2(Math.Min(mid.GetWidth(),Size.X-8-x),24);DrawTextureRectRegion(mid,new(new(x,0),size),new(Vector2.Zero,size));}
        DrawTexture(Textures["catalog-header-right"+suffix],new(Size.X-8,0));
        if(SortDirection!=0)DrawTexture(Textures[SortDirection>0?"catalog-sort-up":"catalog-sort-down"],new(Size.X-9,8));
    }
    public override void _ExitTree()=>_text.Dispose();
}
