using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

/// <summary>Shared catalog window. Domain queries and navigation belong to its caller.</summary>
public partial class CatalogView : FixedPage, IInformationPage
{
    protected override Color PageBackground => new(0, 0, 0, .45f);
    public string Title { get; init; } = "";
    public string HintText { get; init; } = "";
    public string CloseTitle { get; init; } = "关闭";
    public IReadOnlyList<CatalogPage> Pages { get; init; } = [];
    public Func<int, CatalogRow[]> ReadRows { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public Func<int, bool>? PortraitDeceased { get; init; }
    public Func<int, string?>? Portrait { get; init; }
    public TextureScope Textures { get; init; } = null!;
    public Vector2 PortraitSize { get; init; } = new(64, 80);
    public Action<int>? Activated { get; init; }
    public Action<int>? CurrentChanged { get; init; }
    public Action? Closed { get; init; }
    public Action? Confirmed { get; init; }
    public Action? ShowMap { get; init; }
    public bool TabStripFollowsPage { get; init; }
    public int? InitialCurrentId { get; init; }
    public CatalogTableState TableState { get; init; } = new();
    public CatalogSelection? Selection { get; init; }
    public Func<IReadOnlySet<int>, bool>? CanConfirmSelection { get; init; }
    private bool SelectionAccepted => Selection is { Selected.Count: > 0 } && (CanConfirmSelection?.Invoke(Selection.Selected) ?? true);
    public Action<IReadOnlySet<int>>? ConfirmSelection { get; init; }
    private bool HasPairedActions => Selection is not null || Confirmed is not null;
    private int SelectionWidth => Selection is null ? 0 : 40;
    private InformationActionButton? _confirmSelection;
    private CatalogBulkButton? _selectAll, _clearSelection;
    private CatalogTableState _state => TableState;
    private readonly GameTextPresenter _text = new();
    private readonly List<CatalogSkinButton> _headers = [];
    private readonly List<CatalogSkinButton> _tabs = [];
    private readonly List<Label[]> _cells = [];
    private readonly List<Label> _checks = [];
    private Control _grid = null!;
    private SkinnedScrollBar _scroll = null!;
    private AsyncPortrait? _portrait;
    private Rect2 _body;
    private Vector2 _tableOrigin;
    private int _page => _state.Page;
    private int _visibleRows;
    private bool _needsScroll;
    private bool _refreshing;
    private CatalogRow[]? _boundRows;
    private int _boundOffset = -1;
    private Label? _help;
    private int? _pressedRowId;
    private int? _hoveredCheckboxId;
    private int? _pointerRowId;
    private bool? _dragSelectionValue;
    private int? _dragRowIndex;

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        MouseExited += () => { _pointerRowId = null; _hoveredCheckboxId = null; QueueRedraw(); };
        if (InitialCurrentId is int initial) _state.SelectId(initial);
        BuildSurface(ReadRows(_page));
    }
    private void BuildSurface(CatalogRow[] rows)
    {
        _visibleRows = Math.Clamp(rows.Length, 9, CatalogTableState.VisibleRows);
        _needsScroll = rows.Length > CatalogTableState.VisibleRows;
        int width = Math.Max(Pages.Max(p => p.Columns.Sum(c => c.Width)) + SelectionWidth + (_needsScroll ? 16 : 0),
            Math.Min(Pages.Count, 7) * 62 + 52) + 80;
        int height = 226 + 24 * _visibleRows + (Pages.Count > 7 ? 32 : 0);
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
        var close = new InformationActionButton
        {
            Title = Selection is null ? CloseTitle : "中止",
            Textures = Textures,
            Position = new(_body.End.X - 160, _body.End.Y - 48),
            Size = new(80, 48)
        };
        close.Pressed += () => Closed?.Invoke(); AddChild(close);
        if (Selection is not null)
        {
            CatalogBulkButton BulkButton(string title, Vector2 at, Action action)
            {
                var button = new CatalogBulkButton { Title = title, Textures = Textures, Position = at, Size = new(112, 32) };
                button.Pressed += action; AddChild(button); return button;
            }
            _selectAll = BulkButton("总括选择", new(_body.Position.X + 80, _body.End.Y - 64), () => { Selection.SelectAll(_state.Rows.Select(r => r.Id)); RefreshRows(); });
            _clearSelection = BulkButton("总括解除", new(_body.Position.X + 200, _body.End.Y - 64), () => { Selection.Clear(); RefreshRows(); });
            _confirmSelection = new InformationActionButton { Title = "决定", SkinPrefix = "catalog-accept-", FontSize = 24,
                TextRole = GameButtonTextRole.ConfirmYes,
                Textures = Textures, Position = new(_body.End.X - 248, _body.End.Y - 48), Size = new(80, 48) };
            _confirmSelection.Pressed += () => { if (SelectionAccepted) ConfirmSelection?.Invoke(Selection.Selected); };
            AddChild(_confirmSelection);
        }
        if (Confirmed is not null)
        {
            var confirm = new InformationActionButton { Title = "决定", SkinPrefix = "catalog-accept-", FontSize = 24,
                TextRole = GameButtonTextRole.ConfirmYes,
                Textures = Textures, Position = new(_body.End.X - 248, _body.End.Y - 48), Size = new(80, 48) };
            confirm.Pressed += Confirmed; AddChild(confirm);
        }
        if (ShowMap is not null)
        {
            var map = new InformationActionButton { Title = "地图", MapToggle = true,
                TextRole = GameButtonTextRole.MapMode, Textures = Textures,
                Position = new(_body.End.X - (HasPairedActions ? 440 : 352), _body.End.Y - 64), Size = new(108, 32) };
            map.Pressed += ShowMap; AddChild(map);
        }
        IconButton("information-action-close", new(_body.End.X - 40, _body.Position.Y + 32), () => Closed?.Invoke());
        IconButton("catalog-help", new(_body.End.X - 72, _body.Position.Y + 8), ToggleHelp);
        if (Portrait is not null)
        {
            _portrait = new AsyncPortrait { Service = Service, View = San9.Content.Images.PortraitView.Face,
                IsDeceased = () => _state.Current is { } current && (PortraitDeceased?.Invoke(current.Id) ?? false), Size = PortraitSize,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_portrait);
        }
        AddChild(new GameMessageBar { Textures = Textures, Text = HintText });
        ChangePage(_page, rows);
    }

    private void IconButton(string role, Vector2 position, Action action)
    {
        var button = new TextureButton { Position = position, TextureNormal = Textures.Get(role, this), TextureHover = Textures.Get(role + "-1", this), TexturePressed = Textures.Get(role + "-2", this) };
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
    private void ChangePage(int page, CatalogRow[]? rows = null)
    {
        bool opening = _grid is null;
        if (!opening && page == _page) return;
        _state.RefreshRows(rows ?? ReadRows(page), columnsChanged: !opening);
        if (_grid is not null) { RemoveChild(_grid); _grid.QueueFree(); }
        _headers.Clear(); _cells.Clear(); _checks.Clear();
        _boundRows = null;
        _state.SelectPage(page, Pages.Count);
        _grid = new Control { Position = _tableOrigin, MouseFilter = MouseFilterEnum.Ignore }; AddChild(_grid);
        var columns = Pages[page].Columns;
        int width = columns.Sum(c => c.Width) + SelectionWidth, x = SelectionWidth;
        if (Selection is not null)
            _grid.AddChild(new CatalogSkinButton { Textures = Textures, Kind = CatalogButtonKind.Header, Title = "×", Size = new(40,24) });
        for (int i = 0; i < columns.Length; i++)
        {
            int column = i;
            var header = new CatalogSkinButton { Textures = Textures, Kind = CatalogButtonKind.Header, Title = columns[i].Title,
                Position = new(x, 0), Size = new(columns[i].Width, 24) };
            header.Pressed += () => { _state.Sort(column); RefreshRows(); };
            _grid.AddChild(header); _headers.Add(header); x += columns[i].Width;
        }
        for (int row = 0; row < _visibleRows; row++)
        {
            if (Selection is not null)
            {
                var check = LabelAt("", new(12,24+row*24), new(16,24), parent: _grid);
                check.HorizontalAlignment = HorizontalAlignment.Center;
                _text.Apply(check, GameTextStyles.CatalogRow);
                _checks.Add(check);
            }
            var labels = new Label[columns.Length]; x = SelectionWidth;
            for (int c = 0; c < columns.Length; c++)
            {
                labels[c] = LabelAt("", new(x + 4, 24 + row * 24), new(columns[c].Width - 8, 24), parent: _grid);
                _text.Apply(labels[c], GameTextStyles.CatalogRow);
                labels[c].HorizontalAlignment = columns[c].Alignment switch { CatalogAlignment.Right => HorizontalAlignment.Right, CatalogAlignment.Center => HorizontalAlignment.Center, _ => HorizontalAlignment.Left };
                x += columns[c].Width;
            }
            _cells.Add(labels);
        }
        _scroll = new SkinnedScrollBar { Skin = ScrollBarSkin.FromCatalog(Textures), Position = new(width, 24), Size = new(16, _visibleRows * 24), Visible = _needsScroll, ContextRequested = Closed };
        _grid.AddChild(_scroll);
        _scroll.Range.MaxValue = Math.Max(CatalogTableState.VisibleRows, _state.Rows.Length); _scroll.Range.Page = CatalogTableState.VisibleRows;
        _scroll.Range.ValueChanged += value => { if (!_refreshing) { _state.Scroll((int)value); RefreshRows(); } };
        for (int i = 0; i < _tabs.Count; i++) _tabs[i].Selected = i == page;
        RefreshRows();
    }
    private void ActivateCurrent()
    {
        if (_state.Current is not { } row) return;
        if (!CanActivateRow(row.Id)) return;
        if (Selection is null) Activated?.Invoke(row.Id);
        else { Selection.Toggle(row.Id); RefreshRows(); }
    }
    public void RefreshData()
    {
        var rows = ReadRows(_page);
        if (_visibleRows != Math.Clamp(rows.Length, 9, CatalogTableState.VisibleRows) ||
            _needsScroll != (rows.Length > CatalogTableState.VisibleRows))
        {
            ClearRowPress();
            foreach (var child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
            _grid = null!; _tabs.Clear(); _headers.Clear(); _cells.Clear(); _checks.Clear();
            _portrait = null; _help = null; _boundRows = null;
            BuildSurface(rows);
            return;
        }
        _state.RefreshRows(rows);
        _scroll.Range.MaxValue = Math.Max(CatalogTableState.VisibleRows, _state.Rows.Length);
        RefreshRows();
    }
    private void RefreshRows()
    {
        _refreshing = true;
        // Selection changes do not rebind cell text. Sorting/reloading replaces Rows.
        if (!ReferenceEquals(_boundRows, _state.Rows) || _boundOffset != _state.Offset)
        {
            ClearRowPress();
            _pointerRowId = null;
            _hoveredCheckboxId = null;
            for (int r = 0; r < _cells.Count; r++)
            {
                int index = _state.Offset + r;
                for (int c = 0; c < _cells[r].Length; c++)
                    _cells[r][c].Text = index < _state.Rows.Length ? _state.Rows[index].Cells[c].Text : "";
            }
            _boundRows = _state.Rows; _boundOffset = _state.Offset;
        }
        for (int r = 0; r < _cells.Count; r++)
        {
            int index = _state.Offset + r;
            var style = index < _state.Rows.Length && !CanActivateRow(_state.Rows[index].Id)
                ? GameTextStyles.CatalogDisabledRow : GameTextStyles.CatalogRow;
            foreach (var cell in _cells[r]) _text.Apply(cell, style);
        }
        for (int r = 0; r < _checks.Count; r++)
            _checks[r].Text = _state.Offset+r < _state.Rows.Length && Selection!.Selected.Contains(_state.Rows[_state.Offset+r].Id) ? "×" : "";
        for (int c = 0; c < _headers.Count; c++) _headers[c].SortDirection = _state.SortColumn == c ? _state.Descending ? -1 : 1 : 0;
        _scroll.Range.Value = _state.Offset; _scroll.UpdateArrows();
        if (_portrait is not null)
        {
            int visibleRow = _state.CurrentIndex - _state.Offset;
            _portrait.Visible = _state.Current is not null && visibleRow >= 0 && visibleRow < _visibleRows;
            if (_portrait.Visible)
            {
                // Anchor the frame to the highlighted row and keep its bottom above the footer (RE-0151).
                float frameTop = Math.Min(_tableOrigin.Y + 28 + visibleRow * 24, 704 - PortraitSize.Y - 24);
                _portrait.Position = new(_tableOrigin.X - PortraitSize.X - 12, frameTop + 12);
            }
            _portrait.Role = _portrait.Visible ? Portrait!(_state.Current!.Id) : null;
        }
        if (_state.Current is { } row) CurrentChanged?.Invoke(row.Id);
        if (Selection is not null)
        {
            _confirmSelection!.Disabled = !SelectionAccepted;
            _selectAll!.Disabled = !_state.Rows.Any(r => !Selection.Selected.Contains(r.Id) && Selection.CanToggle(r.Id));
            _clearSelection!.Disabled = Selection.Selected.Count == 0;
        }
        _refreshing = false; QueueRedraw();
    }
    private void ToggleHelp()
    {
        ClearRowPress();
        _pointerRowId = null;
        _hoveredCheckboxId = null;
        if (_help is not null) { _help.QueueFree(); _help = null; return; }
        string activationHelp = Selection is null ? "回车查看详情。" : "回车切换选择。\n按住左键划过多行选择或解除。";
        _help = LabelAt($"页签切换显示列；点击表头排序。\n上下键选择，滚轮或滚动条翻阅。\nCtrl+Tab 切换页签，{activationHelp}\nEsc 或右键返回。", _body.Position + new Vector2(80, 220), new(_body.Size.X - 160, 150));
        _help.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("161b1b"), ContentMarginLeft = 16 });
        _help.ZIndex = 5; _help.MouseFilter = MouseFilterEnum.Stop;
    }
    public override void _GuiInput(InputEvent input)
    {
        if (_help is not null) return;
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse &&
            RowAt(mouse.Position) is int index)
        {
            AcceptEvent();
            UpdateRowPointer(mouse.Position);
            int id = _state.Rows[index].Id;
            _state.PointAt(index); RefreshRows();
            if (!CanActivateRow(id)) return;
            if (mouse.Pressed)
            {
                if (Selection is not null)
                {
                    _dragSelectionValue = !Selection.Selected.Contains(id);
                    _dragRowIndex = index;
                    ContinueSelectionDrag(mouse.Position);
                }
                // The checkbox has its own hit feedback; only the body uses the bright row frame.
                _pressedRowId = mouse.Position.X >= _tableOrigin.X + SelectionWidth ? id : null;
                QueueRedraw();
            }
            else if (Selection is null) ActivateCurrent();
        }
        if (input is InputEventMouseMotion motion)
        {
            UpdateRowPointer(motion.Position);
            if (RowAt(motion.Position) is int hovered && hovered != _state.CurrentIndex)
            { _state.PointAt(hovered); RefreshRows(); }
        }
    }
    private bool CanActivateRow(int id) => Selection?.CanToggle(id) ?? true;
    private void ContinueSelectionDrag(Vector2 position)
    {
        if (_dragSelectionValue is not bool selected || Selection is null) return;
        // Clamp vertical overshoot to the last visible row, without scrolling the table.
        float top = _tableOrigin.Y + 24;
        int count = Math.Min(_visibleRows, _state.Rows.Length - _state.Offset);
        if (count <= 0) return;
        var hit = new Vector2(position.X, Math.Clamp(position.Y, top, top + count * 24 - 1));
        if (RowAt(hit) is not int index)
        {
            _dragRowIndex = null;
            return;
        }
        int first = _dragRowIndex ?? index;
        int step = index >= first ? 1 : -1;
        bool changed = false;
        for (int row = first; ; row += step)
        {
            int id = _state.Rows[row].Id;
            // A stroke assigns one value; revisiting a row must never toggle it back.
            if (Selection.Selected.Contains(id) != selected) changed |= Selection.Toggle(id);
            if (row == index) break;
        }
        _dragRowIndex = index;
        bool moved = _state.CurrentIndex != index;
        _state.PointAt(index);
        UpdateRowPointer(position);
        if (changed || moved) RefreshRows();
    }
    private void ClearRowPress()
    {
        _dragSelectionValue = null;
        _dragRowIndex = null;
        if (_pressedRowId is null) return;
        _pressedRowId = null;
        QueueRedraw();
    }
    private void UpdateRowPointer(Vector2 position)
    {
        int? row = RowAt(position);
        int? id = row is int index ? _state.Rows[index].Id : null;
        int? checkbox = Selection is not null && row is int checkboxRow &&
            new Rect2(_tableOrigin + new Vector2(12, 28 + (checkboxRow - _state.Offset) * 24), new(16, 16)).HasPoint(position)
            ? id : null;
        if (_pointerRowId == id && _hoveredCheckboxId == checkbox) return;
        _pointerRowId = id;
        _hoveredCheckboxId = checkbox;
        QueueRedraw();
    }
    private int? RowAt(Vector2 position)
    {
        var rect = new Rect2(_tableOrigin + new Vector2(0, 24), new(Pages[_page].Columns.Sum(c => c.Width) + SelectionWidth, _visibleRows * 24));
        if (!rect.HasPoint(position)) return null;
        int index = _state.Offset + (int)((position.Y - rect.Position.Y) / 24);
        return index < _state.Rows.Length ? index : null;
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        // Release can land on a child control or outside the table; it must always end the feedback.
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            if (_dragSelectionValue is not null)
            {
                ContinueSelectionDrag(GetLocalMousePosition());
                GetViewport().SetInputAsHandled();
            }
            ClearRowPress();
        }
        if (input is InputEventMouseMotion drag && _dragSelectionValue is not null)
        {
            if ((drag.ButtonMask & MouseButtonMask.Left) == 0) ClearRowPress();
            else
            {
                ContinueSelectionDrag(GetLocalMousePosition());
                GetViewport().SetInputAsHandled();
                return;
            }
        }
        if (input is InputEventMouseMotion && _help is null) UpdateRowPointer(GetLocalMousePosition());
        if (input is InputEventMouseButton { Pressed: true } mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Right)
            {
                ClearRowPress();
                GetViewport().SetInputAsHandled();
                if (_help is not null) ToggleHelp(); else Closed?.Invoke();
                return;
            }
            if (_help is null && mouse.ButtonIndex is MouseButton.WheelDown or MouseButton.WheelUp)
            {
                ClearRowPress();
                _state.Scroll(_state.Offset + (mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1));
                GetViewport().SetInputAsHandled(); RefreshRows(); return;
            }
        }
        if (input is not InputEventKey { Pressed: true } key) return;
        ClearRowPress();
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
            ActivateCurrent();
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
        SkinPainter.Tile(this, Textures.Get(role, this), rect);
    }
    private void Strip(string role, Vector2 point, float width)
    {
        var left = Textures.Get(role + "-left", this); var right = Textures.Get(role + "-right", this);
        Tile(role + "-middle", new(point + new Vector2(left.GetWidth(),0), new(width-left.GetWidth()-right.GetWidth(),left.GetHeight())));
        DrawTexture(left,point); DrawTexture(right,point+new Vector2(width-right.GetWidth(),0));
    }
    public override void _Draw()
    {
        if (_grid is null) return;
        DrawTextureRectRegion(Textures.Get("information-background", this), _body, new(Vector2.Zero,_body.Size));
        float x=_body.Position.X,y=_body.Position.Y,right=_body.End.X,bottom=_body.End.Y;
        Tile("information-frame-top",new(x+64,y-8,_body.Size.X-304,8));
        Tile("information-frame-bottom",new(x+208,bottom-16,_body.Size.X-448,24));
        Tile("information-frame-left",new(x-16,y+216,24,_body.Size.Y-432));
        Tile("information-frame-right",new(right-8,y+216,16,_body.Size.Y-432));
        DrawTexture(Textures.Get("information-frame-top-left", this),new(x-56,y-16));
        DrawTexture(Textures.Get("information-frame-top-right", this),new(right-240,y-16));
        DrawTexture(Textures.Get("information-frame-bottom-left", this),new(x-56,bottom-216));
        DrawTexture(Textures.Get("information-frame-bottom-right", this),new(right-240,bottom-216));
        Strip("information-title",new(x-40,y-16),264);
        DrawTexture(Textures.Get("information-frame-ornament", this),new(x-56,y-16));
        Strip("information-footer",new(right-(HasPairedActions ? 324 : 236),bottom-64),HasPairedActions ? 348 : 260);
        var tabs = new Rect2(x+40,y+64,TabStripFollowsPage ? Pages[_page].Columns.Sum(c => c.Width) : _body.Size.X-80,Pages.Count>7?80:48);
        Tile("catalog-tabs-top",new(tabs.Position+new Vector2(12,0),new(tabs.Size.X-24,8)));
        Tile("catalog-tabs-bottom",new(tabs.Position+new Vector2(12,tabs.Size.Y-8),new(tabs.Size.X-24,8)));
        Tile("catalog-tabs-left",new(tabs.Position+new Vector2(0,24),new(12,tabs.Size.Y-48)));
        Tile("catalog-tabs-right",new(tabs.Position+new Vector2(tabs.Size.X-12,24),new(12,tabs.Size.Y-48)));
        DrawTexture(Textures.Get("catalog-tabs-top-left", this),tabs.Position); DrawTexture(Textures.Get("catalog-tabs-top-right", this),new(tabs.End.X-12,tabs.Position.Y));
        DrawTexture(Textures.Get("catalog-tabs-bottom-left", this),new(tabs.Position.X,tabs.End.Y-24)); DrawTexture(Textures.Get("catalog-tabs-bottom-right", this),tabs.End-new Vector2(12,24));
        int width = Pages[_page].Columns.Sum(c => c.Width) + SelectionWidth;
        DrawTableFrame(width + 2 + (_needsScroll ? 16 : 0));
        int current = _state.CurrentIndex - _state.Offset;
        for (int r = 0; r < _visibleRows && _state.Offset + r < _state.Rows.Length; r++)
        {
            int id = _state.Rows[_state.Offset + r].Id;
            bool selected = Selection?.Selected.Contains(id) == true;
            bool currentRow = r == current;
            bool pressed = CanActivateRow(id) && _pressedRowId == id && _pointerRowId == id;
            if (Selection is not null)
            {
                int checkFrame = _hoveredCheckboxId == id ? 1 : selected ? 2 : 0;
                var check = Textures.Get("catalog-checkbox" + (checkFrame == 0 ? "" : $"-{checkFrame}"), this);
                var checkAt = _tableOrigin + new Vector2(12,28+r*24);
                DrawTexture(check, checkAt);

            }
            if (pressed || currentRow || selected)
                DrawRow(_tableOrigin + new Vector2(SelectionWidth,28+r*24), width-SelectionWidth, pressed ? 2 : currentRow ? 1 : 3);
        }
        if(_portrait is { Visible: true }) DrawTextureRect(Textures.Get("catalog-portrait-frame", this), new(_portrait.Position - new Vector2(12,12), PortraitSize + new Vector2(24,24)), false);
    }
    private void DrawRow(Vector2 point, int width, int frame)
    {
        var left = Textures.Get($"catalog-row-left-{frame}", this);
        var right = Textures.Get($"catalog-row-right-{frame}", this);
        DrawTexture(left,point);
        DrawTexture(right,point+new Vector2(width-right.GetWidth(),0));
        int middleStart = left.GetWidth()-1;
        Tile($"catalog-row-middle-{frame}",new(point+new Vector2(middleStart,0),new(width-right.GetWidth()-middleStart,left.GetHeight())));
    }
    private void DrawTableFrame(int width)
    {
        Vector2 origin = _tableOrigin - Vector2.One;
        int height = 30 + 24 * _visibleRows;
        foreach (string edge in new[] { "top", "bottom" })
        {
            bool bottom = edge == "bottom";
            float y = bottom ? 25 : 0;
            float h = bottom ? height - 25 : 28;
            var left = Textures.Get("catalog-frame-" + edge + "-left", this);
            var middle = Textures.Get("catalog-frame-" + edge, this);
            var right = Textures.Get("catalog-frame-" + edge + "-right", this);
            float sourceY = bottom ? left.GetHeight() - h : 0;
            void Part(Texture2D texture, float x, float w) => DrawTextureRectRegion(texture,
                new(origin + new Vector2(x, y), new(w, h)), new(0, sourceY, w, h));
            for (float x = left.GetWidth(); x < width - right.GetWidth(); x += middle.GetWidth())
                Part(middle, x, Math.Min(middle.GetWidth(), width - right.GetWidth() - x));
            Part(left, 0, left.GetWidth()); Part(right, width - right.GetWidth(), right.GetWidth());
        }
    }
    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut ||
            what == NotificationVisibilityChanged && !IsVisibleInTree()) ClearRowPress();
    }
    public override void _ExitTree()
    {
        ClearRowPress();
        _text.Dispose();
    }
}

public enum CatalogButtonKind { Header, Tab }
public partial class CatalogSkinButton : BaseButton
{
    public string Title { get; init; } = "";
    public CatalogButtonKind Kind { get; init; }
    public TextureScope Textures { get; init; } = null!;
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
        if(Kind==CatalogButtonKind.Tab){DrawTextureRectRegion(Textures.Get("catalog-tab"+suffix, this),new(Vector2.Zero,Size),new(Vector2.Zero,Size));return;}
        DrawTexture(Textures.Get("catalog-header-left"+suffix, this),Vector2.Zero);
        var mid=Textures.Get("catalog-header-middle"+suffix, this);
        for(float x=8;x<Size.X-8;x+=mid.GetWidth())
        {var size=new Vector2(Math.Min(mid.GetWidth(),Size.X-8-x),24);DrawTextureRectRegion(mid,new(new(x,0),size),new(Vector2.Zero,size));}
        DrawTexture(Textures.Get("catalog-header-right"+suffix, this),new(Size.X-8,0));
        if(SortDirection!=0)DrawTexture(Textures.Get(SortDirection>0?"catalog-sort-up":"catalog-sort-down", this),new(Size.X-9,8));
    }
    public override void _ExitTree()=>_text.Dispose();
}
