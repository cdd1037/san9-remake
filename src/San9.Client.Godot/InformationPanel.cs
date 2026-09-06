using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Client.Godot;

public partial class InformationPanel : Control
{
    public InformationBrowser State { get; } = new();
    public GameSession Session { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public IReadOnlyDictionary<string, ImageTexture> Textures { get; init; } = null!;
    public Action<int> Locate { get; init; } = null!;
    private InformationQuery _query = null!;
    private readonly GameTextPresenter _text = new();
    private Control _content = null!;
    private SkinnedScrollBar? _scroll;
    private InformationTable? _table;
    private InformationRow[] _rows = [];
    private readonly List<Control> _rowControls = [];
    private readonly Rect2 _body = new(200, 104, 648, 576);
    private readonly Rect2 _menu = new(842, 47, 104, 272);
    public InformationMenuButton? HoverMenuButton => State.IsMenu
        ? _content.GetChildren().OfType<InformationMenuButton>().FirstOrDefault(b => b.IsHovered()) : null;
    private CatalogView? _catalog;
    private CatalogTableState _officerCatalogState = new();
    private CatalogTableState _treasureCatalogState = new();
    private bool _dirty;
    private OfficerDetailView? _officerDetail;
    private readonly OfficerDetailState _officerDetailState = new();
    private LineEdit? _note;
    private InformationActionButton? _mapButton;
    public override void _Ready()
    {
        Name = "InformationPanel"; Size = new(1024, 768); ZIndex = 20;
        MouseFilter = MouseFilterEnum.Stop; Visible = false;
        _query = new(Session, Texts);
        Session.Changed += OnChanged;
    }
    private void OnChanged() => _dirty = true;
    public void OpenMenu() { State.OpenMenu(); Rebuild(); }
    public void Open(InformationCategory category) { State.Close(); State.Open(category); Rebuild(); }
    public void Back() { State.Back(); Rebuild(); }
    public override void _Process(double delta)
    { if (_dirty && State.IsOpen) { _dirty = false; Rebuild(); } }
    private void Rebuild()
    {
        Visible = State.IsOpen; QueueRedraw();
        if (_content is not null) { RemoveChild(_content); _content.QueueFree(); }
        _content = new Control { Size = Size, MouseFilter = MouseFilterEnum.Ignore }; AddChild(_content);
        _catalog = null; _officerDetail = null; _scroll = null; _rowControls.Clear(); _note = null; _mapButton = null;
        if (!Visible) return;
        if (State.IsMenu)
        {
            foreach (var category in Enum.GetValues<InformationCategory>())
            {
                var button = new InformationMenuButton
                {
                    Title = InformationQuery.MenuTitle(category), HintText = Texts.Resolve(_query.MenuHintKey(category)),
                    Textures = Textures, Position = new(858, 63 + (int)category * 24), Size = new(72, 24),
                    Disabled = !_query.Available(category)
                };
                button.Pressed += () => { State.Open(category); Rebuild(); };
                _content.AddChild(button);
            }
            return;
        }
        if (State.Category == InformationCategory.Officers && !State.IsDetail)
        {
            if (State.SelectedId is null) _officerCatalogState = new();
            _catalog = new CatalogView
            {
                TableState = _officerCatalogState, Title = "武将一览", HintText = Texts.Resolve(StrategyHintText.Key("catalog-officers")), Pages = OfficerCatalog.Pages, Textures = Textures,
                InitialPage = State.Page, InitialCurrentId = State.SelectedId, InitialOffset = State.Offset,
                ReadRows = page => _query.Read(State.Category, page, State.ForceFilter, State.SettlementFilter).Rows
                    .Select(row => new CatalogRow(row.Key.Id, row.Cells)).ToArray(),
                Portrait = id => Textures[$"catalog-person-{id}"],
                Closed = Back, Activated = id =>
                {
                    _officerDetailState.Open(_officerCatalogState.Rows.Select(row => row.Id), id);
                    State.ShowDetail(id); Rebuild();
                },
                ViewChanged = (page, offset, selected) =>
                {
                    State.SelectPage(page, OfficerCatalog.Pages.Count); State.Scroll(offset, _query.Read(State).Rows.Length);
                    if (selected is int id) State.Select(id);
                }
            };
            _content.AddChild(_catalog); return;
        }
        if (State.Category == InformationCategory.Officers && State.IsDetail)
        {
            _officerDetail = new OfficerDetailView
            {
                State = _officerDetailState, Textures = Textures, Texts = Texts,
                Read = id => OfficerDetails.Read(Session.View, Texts, id),
                ReadName = id => Texts.Resolve(Session.View.Officers.Single(o => o.Id == id).NameKey),
                OpenTreasures = id => { State.Navigate(new("宝物", InformationCategory.Treasures, HolderId: id)); Rebuild(); },
                CurrentChanged = State.Select, Closed = Back
            };
            _content.AddChild(_officerDetail); return;
        }
        if (State.Category == InformationCategory.Treasures)
        {
            if (State.SelectedId is null) _treasureCatalogState = new();
            _catalog = new CatalogView
            {
                TableState = _treasureCatalogState, Title = "宝物一览", HintText = "请选择欲观看其情报的宝物。",
                Pages = TreasureCatalog.Pages, Textures = Textures,
                InitialCurrentId = State.SelectedId, InitialOffset = State.Offset,
                ReadRows = _ => TreasureCatalog.Read(Session.View, Texts, State.HolderFilter, State.ForceFilter, State.SettlementFilter)
                    .Rows.Select(row => new CatalogRow(row.Key.Id, row.Cells)).ToArray(),
                Portrait = id => Textures[$"information-treasure-{id}"], PortraitSize = new(128, 128),
                Closed = Back,
                ViewChanged = (_, offset, selected) =>
                {
                    State.Scroll(offset, _treasureCatalogState.Rows.Length);
                    if (selected is int id) State.Select(id);
                }
            };
            _content.AddChild(_catalog); return;
        }
        _table = _query.Read(State); _rows = State.Order(_table);
        State.Scroll(State.Offset, _rows.Length);
        if (State.SelectedId is null || !_rows.Any(r => r.Key.Id == State.SelectedId)) State.Select(_rows.FirstOrDefault()?.Key.Id ?? -1);
        if (State.IsDetail && _rows.Length == 0) State.Back();
        if (State.IsDetail) BuildDetail(); else BuildTable();
        ActionButton("关闭", new(723, 625), new(80, 36), Back);
        var close = new TextureButton { Position = new(804, 123), Size = new(40, 32),
            TextureNormal = Textures["information-action-close"], TextureHover = Textures["information-action-close-1"],
            TexturePressed = Textures["information-action-close-2"] };
        close.Pressed += Back; _content.AddChild(close);
        LabelAt("Esc / 右键返回", new(210, 649), new(240, 24), 14, new Color("b8b0a0"));
    }
    private Label LabelAt(string text, Vector2 at, Vector2 size, int fontSize = 16, Color? color = null, Control? parent = null)
    {
        var label = new Label { Text = text, Position = at, Size = size, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        (parent ?? _content).AddChild(label);
        _text.Apply(label, GameTextStyles.For(GameTextRole.Resource) with { FontSize = fontSize, Foreground = color ?? new Color("f0f0f0") });
        return label;
    }
    private Button PlainButton(string text, Vector2 at, Vector2 size, Action action, bool selected = false)
    {
        var button = new Button { Text = text, Position = at, Size = size, ClipText = true, FocusMode = FocusModeEnum.All };
        button.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = selected ? new Color("604b26") : new Color(0, 0, 0, .15f), BorderColor = new Color("50493a"), BorderWidthBottom = 1 });
        button.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = new Color("4b4930") });
        button.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = new Color("776332") });
        GameTextPresenter.Apply(button, GameTextStyles.For(GameTextRole.Resource));
        button.Pressed += action; _content.AddChild(button); return button;
    }
    private InformationActionButton ActionButton(string text, Vector2 at, Vector2 size, Action action)
    {
        var button = new InformationActionButton { Title = text, Textures = Textures, Position = at, Size = size };
        button.Pressed += action; _content.AddChild(button); return button;
    }
    private void Title(string title) => LabelAt(title, new(213, 94), new(330, 58), 24);
    private void BuildTable()
    {
        Title(_table!.Title);
        for (int p = 0; p < _table.Pages.Length; p++)
        {
            int page = p;
            var button = ActionButton(_table.Pages[p], new(235 + p * 78, 173), new(68, 28), () => { State.SelectPage(page, _table.Pages.Length); Rebuild(); });
            button.ToggleMode = true; button.SetPressedNoSignal(State.Page == p);
        }
        float[] widths = ColumnWidths(); float x = 224;
        for (int c = 0; c < _table.Columns.Length; c++)
        {
            int column = c;
            string arrow = State.SortColumn == c ? State.Descending ? " ▾" : " ▴" : "";
            var header = PlainButton(_table.Columns[c] + arrow, new(x, 216), new(widths[c], 24), () => { State.Sort(column, _table.Columns.Length); Rebuild(); });
            header.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("14573d"), BorderColor = new Color("547060"), BorderWidthBottom = 2, BorderWidthRight = 1, BorderWidthTop = 1 });
            x += widths[c];
        }
        _scroll = new SkinnedScrollBar { Skin = ScrollBarSkin.FromInterface(Textures), Position = new(814, 240), Size = new(18, 360) };
        _content.AddChild(_scroll);
        _scroll.Range.MaxValue = Math.Max(InformationBrowser.PageSize, _rows.Length); _scroll.Range.Page = InformationBrowser.PageSize;
        _scroll.Range.Value = State.Offset;
        _scroll.Range.ValueChanged += value => { State.Scroll((int)value, _rows.Length); DrawRows(); };
        _scroll.UpdateArrows();
        DrawRows();
        if (_rows.Length == 0) LabelAt("暂无记录", new(430, 370), new(180, 30), 20, new Color("c8b888"));
        LabelAt($"共 {_rows.Length} 项", new(224, 606), new(200, 24), 14, new Color("b8b0a0"));
        if (State.Category == InformationCategory.Chronicle)
            ActionButton("输入年表", new(464, 621), new(110, 32), BeginNote);
        else if (State.Category is not (InformationCategory.Ranks or InformationCategory.Records))
        {
            _mapButton = ActionButton("地图", new(464, 621), new(110, 32), LocateSelected);
            UpdateMapButton();
        }
    }
    private float[] ColumnWidths()
    {
        if (_table!.Columns.Length == 2) return [144, 446];
        if (State.Category == InformationCategory.Ranks) return [90, 90, 70, 340];
        float width = 590f / _table.Columns.Length;
        return Enumerable.Repeat(width, _table.Columns.Length).ToArray();
    }
    private void DrawRows()
    {
        foreach (var control in _rowControls) { _content.RemoveChild(control); control.QueueFree(); }
        _rowControls.Clear();
        float[] widths = ColumnWidths();
        for (int index = State.Offset; index < Math.Min(_rows.Length, State.Offset + InformationBrowser.PageSize); index++)
        {
            var row = _rows[index];
            var button = new InformationRowButton { Textures = Textures, Position = new(224, 240 + (index - State.Offset) * 24), Size = new(590, 24), Selected = State.SelectedId == row.Key.Id };
            _content.AddChild(button); _rowControls.Add(button);
            button.MouseEntered += () => { State.Select(row.Key.Id); UpdateMapButton(); foreach (var item in _rowControls.OfType<InformationRowButton>()) item.Selected = item == button; };
            button.Pressed += () =>
            {
                if (State.Category is InformationCategory.Ranks or InformationCategory.Records or InformationCategory.Chronicle) return;
                State.ShowDetail(row.Key.Id); Rebuild();
            };
            float x = 0;
            for (int c = 0; c < row.Cells.Length; c++)
            {
                var label = LabelAt(row.Cells[c].Text, new(x + 4, 0), new(widths[c] - 8, 24), 16, parent: button);
                if (row.Cells[c].Number is not null) label.HorizontalAlignment = HorizontalAlignment.Right;
                x += widths[c];
            }
        }
    }
    private void BuildDetail()
    {
        var detail = _query.Detail(new(State.Category, State.SelectedId!.Value));
        Title(detail.Title);
        bool hasImage = detail.ImageRole is not null;
        if (hasImage)
        {
            var image = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Texture = Textures[detail.ImageRole!], Position = new(230, 188), Size = new(160, 160),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
            _content.AddChild(image);
        }
        for (int i = 0; i < detail.Fields.Length; i++)
        {
            int column = i % 2, line = i / 2;
            LabelAt(detail.Fields[i].Label, new((hasImage ? 414 : 230) + column * (hasImage ? 205 : 290), 188 + line * 42), new(hasImage ? 72 : 96, 32), 16, new Color("d8c898"));
            LabelAt(detail.Fields[i].Value, new((hasImage ? 486 : 328) + column * (hasImage ? 205 : 290), 188 + line * 42), new(hasImage ? 128 : 180, 32), 18);
        }
        for (int i = 0; i < detail.Links.Length; i++)
        {
            var link = detail.Links[i];
            ActionButton(link.Label, new(235 + i * 100, 546), new(90, 30), () => { State.Navigate(link); Rebuild(); });
        }
        ActionButton("上一项", new(275, 600), new(90, 30), () => MoveDetail(-1));
        ActionButton("下一项", new(375, 600), new(90, 30), () => MoveDetail(1));
        var locate = ActionButton("地图", new(510, 600), new(90, 30), LocateSelected);
        locate.Disabled = detail.SettlementId is null;
    }
    private void MoveDetail(int direction)
    {
        int i = Array.FindIndex(_rows, r => r.Key.Id == State.SelectedId);
        if (_rows.Length > 0) { State.ShowDetail(_rows[Math.Clamp(i + direction, 0, _rows.Length - 1)].Key.Id); Rebuild(); }
    }
    private void LocateSelected()
    {
        if (_rows.FirstOrDefault(r => r.Key.Id == State.SelectedId)?.SettlementId is not int id) return;
        State.Close(); Rebuild(); Locate(id);
    }
    private void UpdateMapButton()
    {
        if (_mapButton is not null)
            _mapButton.Disabled = !_rows.Any(r => r.Key.Id == State.SelectedId && r.SettlementId is not null);
    }
    private void BeginNote()
    {
        if (_note is not null) return;
        _note = new LineEdit { PlaceholderText = "记录一件大事（最多240字）", Position = new(228, 606), Size = new(450, 34), MaxLength = 240 };
        _content.AddChild(_note); _note.GrabFocus();
        _note.TextSubmitted += text => { if (!string.IsNullOrWhiteSpace(text)) { Session.AddChronicleNote(text); Rebuild(); } };
    }
    public override void _Input(InputEvent @event)
    {
        if (!Visible || _catalog is not null || _officerDetail is not null) return;
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        { if (_note is not null) Rebuild(); else Back(); GetViewport().SetInputAsHandled(); }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { Back(); GetViewport().SetInputAsHandled(); }
        else if (!State.IsMenu && !State.IsDetail && _note is null && @event is InputEventKey { Pressed: true } key)
        {
            int i = Math.Max(0, Array.FindIndex(_rows, r => r.Key.Id == State.SelectedId));
            int target = key.Keycode switch { Key.Up => i - 1, Key.Down => i + 1, Key.Pageup => i - 15, Key.Pagedown => i + 15, Key.Home => 0, Key.End => _rows.Length - 1, _ => i };
            if (target != i && _rows.Length > 0)
            {
                target = Math.Clamp(target, 0, _rows.Length - 1); State.Select(_rows[target].Key.Id);
                if (target < State.Offset || target >= State.Offset + 15) State.Scroll(target < State.Offset ? target : target - 14, _rows.Length);
                Rebuild(); GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.Enter && _rows.Length > 0 && State.Category is not (InformationCategory.Ranks or InformationCategory.Records or InformationCategory.Chronicle))
            { State.ShowDetail(_rows[i].Key.Id); Rebuild(); GetViewport().SetInputAsHandled(); }
            else if (key.Keycode is Key.Up or Key.Down or Key.Pageup or Key.Pagedown or Key.Home or Key.End)
                GetViewport().SetInputAsHandled();
        }
    }
    public override void _GuiInput(InputEvent @event)
    {
        if (_catalog is not null || _officerDetail is not null) return;
        if (@event is InputEventMouseButton { Pressed: true } mouse)
        {
            if (State.IsMenu && !_menu.HasPoint(mouse.Position)) Back();
            else if (!State.IsMenu && !State.IsDetail && mouse.ButtonIndex is MouseButton.WheelDown or MouseButton.WheelUp)
            { State.Scroll(State.Offset + (mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1), _rows.Length); if (_scroll is not null) _scroll.Range.Value = State.Offset; }
        }
        AcceptEvent();
    }
    private void Tile(string role, Rect2 rect)
    {
        var texture = Textures[role];
        for (float y = 0; y < rect.Size.Y; y += texture.GetHeight())
            for (float x = 0; x < rect.Size.X; x += texture.GetWidth())
            {
                Vector2 size = new(Math.Min(texture.GetWidth(), rect.Size.X - x), Math.Min(texture.GetHeight(), rect.Size.Y - y));
                DrawTextureRectRegion(texture, new(rect.Position + new Vector2(x, y), size), new(Vector2.Zero, size));
            }
    }
    private void Strip(string prefix, Vector2 at, float width)
    {
        var left = Textures[prefix + "-left"]; var right = Textures[prefix + "-right"];
        DrawTexture(left, at); Tile(prefix + "-middle", new(at + new Vector2(left.GetWidth(), 0), new(width - left.GetWidth() - right.GetWidth(), left.GetHeight())));
        DrawTexture(right, at + new Vector2(width - right.GetWidth(), 0));
    }
    public override void _Draw()
    {
        if (_catalog is not null || _officerDetail is not null) return;
        if (!Visible) return;
        if (State.IsMenu)
        {
            Tile("information-menu-center", _menu);
            Tile("information-menu-top", new(842, 47, 104, 16)); Tile("information-menu-left", new(842, 47, 16, 272));
            Tile("information-menu-right", new(930, 47, 16, 272)); Tile("information-menu-bottom", new(842, 303, 104, 16));
            DrawTexture(Textures["information-menu-top-left"], new(842, 47)); DrawTexture(Textures["information-menu-top-right"], new(930, 47));
            DrawTexture(Textures["information-menu-bottom-left"], new(842, 303)); DrawTexture(Textures["information-menu-bottom-right"], new(914, 287));
            return;
        }
        DrawRect(new(0, 0, 1024, 768), new Color(0, 0, 0, .45f));
        DrawTextureRectRegion(Textures["information-background"], _body, new(0, 0, _body.Size.X, _body.Size.Y));
        Tile("information-frame-top", new(264, 96, 344, 8)); Tile("information-frame-bottom", new(408, 664, 200, 24));
        Tile("information-frame-left", new(184, 320, 24, 144)); Tile("information-frame-right", new(840, 320, 16, 144));
        DrawTexture(Textures["information-frame-top-left"], new(144, 88)); DrawTexture(Textures["information-frame-top-right"], new(608, 88));
        DrawTexture(Textures["information-frame-bottom-left"], new(144, 464)); DrawTexture(Textures["information-frame-bottom-right"], new(608, 464));
        Strip("information-title", new(144, 88), 304);
        DrawTexture(Textures["information-frame-ornament"], new(144, 88));
        Strip("information-footer", new(648, 616), 224);
        if (!State.IsDetail) DrawRect(new(224, 240, 590, 360), new Color(0, 0, 0, .52f));
    }
    public override void _ExitTree() { Session.Changed -= OnChanged; _text.Dispose(); }
}

public partial class InformationActionButton : BaseButton
{
    private readonly GameTextPresenter _text = new();
    public override void _ExitTree() => _text.Dispose();
    public string Title { get; init; } = "";
    public IReadOnlyDictionary<string, ImageTexture> Textures { get; init; } = null!;
    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        var label = new Label { Text = Title, Size = Size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label);
        _text.Apply(label, GameTextStyles.For(GameTextRole.Resource) with { FontSize = 18, Foreground = Colors.White });
    }
    public override void _Process(double delta) { Modulate = Disabled ? new Color("888888") : Colors.White; QueueRedraw(); }
    public override void _Draw()
    {
        int frame = Disabled ? 3 : ButtonPressed || GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed ? 2 : IsHovered() || HasFocus() ? 1 : 0;
        string suffix = frame == 0 ? "" : $"-{frame}";
        var left = Textures["information-action-left" + suffix]; var mid = Textures["information-action-middle" + suffix]; var right = Textures["information-action-right" + suffix];
        DrawTextureRect(left, new(0, 0, 12, Size.Y), false); DrawTextureRect(mid, new(12, 0, Size.X - 24, Size.Y), false); DrawTextureRect(right, new(Size.X - 12, 0, 12, Size.Y), false);
    }
}

public partial class InformationRowButton : BaseButton
{
    public IReadOnlyDictionary<string, ImageTexture> Textures { get; init; } = null!;
    public bool Selected { get; set; }
    public override void _Ready() { FocusMode = FocusModeEnum.All; MouseFilter = MouseFilterEnum.Pass; }
    public override void _Process(double delta) => QueueRedraw();
    public override void _Draw()
    {
        if (!Selected && !HasFocus() && !IsHovered()) return;
        DrawTextureRect(Textures["roster-row-left-1"], new(0, 4, 8, 16), false);
        DrawTextureRect(Textures["roster-row-middle-1"], new(8, 4, Size.X - 16, 16), false);
        DrawTextureRect(Textures["roster-row-right-1"], new(Size.X - 8, 4, 8, 16), false);
    }
}
