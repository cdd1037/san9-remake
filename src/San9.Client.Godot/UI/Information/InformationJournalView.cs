using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Client.Godot;

public partial class InformationJournalView : FixedPage, IInformationPage
{
    protected override Color PageBackground => new(0, 0, 0, .45f);
    public InformationBrowser State { get; init; } = null!;
    public GameSession Session { get; init; } = null!;
    public InformationQuery Query { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Action Back { get; init; } = null!;
    public Action<San9.Core.Domain.MapCell> LocateRecord { get; init; } = null!;
    public MapMessageLog RecordsLog { get; init; } = null!;
    public San9.Content.Interface.InterfaceSkin Skin { get; init; } = null!;
    private Control _content => this;
    private readonly GameTextPresenter _text = new();
    private SkinnedScrollBar? _scroll;
    private InformationTable? _table;
    private InformationRow[] _rows = [];
    private readonly List<(InformationRowButton Button, Label?[] Cells)> _rowControls = [];
    private readonly List<GameRichTextLabel> _recordText = [];
    private readonly List<Button> _columnHeaders = [];
    private readonly Rect2 _body = new(200, 104, 648, 576);
    private LineEdit? _note;
    private Label _count = null!, _empty = null!;
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _table = ReadTable(); _rows = State.Order(_table);
        State.Scroll(State.Offset, _rows.Length);
        BuildTable();
        ActionButton("关闭", new(723, 625), new(80, 36), Back);
        var close = new TextureButton { Position = new(804, 123), Size = new(40, 32),
            TextureNormal = Textures["information-action-close"], TextureHover = Textures["information-action-close-1"],
            TexturePressed = Textures["information-action-close-2"] };
        close.Pressed += Back; AddChild(close);
        LabelAt("Esc / 右键返回", new(210, 649), new(240, 24), 14, new Color("b8b0a0"));
    }
    public void RefreshData()
    {
        _table = ReadTable(); _rows = State.Order(_table);
        State.Scroll(State.Offset, _rows.Length);
        if (_scroll is not null) _scroll.Range.MaxValue = Math.Max(InformationBrowser.PageSize, _rows.Length);
        _count.Text = $"共 {_rows.Length} 项";
        _empty.Visible = _rows.Length == 0;
        DrawRows();
    }
    private void CancelNote()
    {
        if (_note is null) return;
        RemoveChild(_note); _note.QueueFree(); _note = null;
    }
    private InformationTable ReadTable() => State.Category == InformationCategory.Records
        ? RecordsLog.ReadHistory() : Query.Read(State);
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
            var button = ActionButton(_table.Pages[p], new(235 + p * 78, 173), new(68, 28), () => { State.SelectPage(page, _table.Pages.Length); RefreshData(); });
            button.ToggleMode = true; button.SetPressedNoSignal(State.Page == p);
        }
        float[] widths = ColumnWidths(); float x = 224;
        for (int c = 0; c < _table.Columns.Length; c++)
        {
            int column = c;
            string arrow = State.SortColumn == c ? State.Descending ? " ▾" : " ▴" : "";
            var header = PlainButton(_table.Columns[c] + arrow, new(x, 216), new(widths[c], 24), () => { State.Sort(column, _table.Columns.Length); _rows = State.Order(_table); DrawRows(); });
            header.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("14573d"), BorderColor = new Color("547060"), BorderWidthBottom = 2, BorderWidthRight = 1, BorderWidthTop = 1 });
            _columnHeaders.Add(header);
            x += widths[c];
        }
        BuildRows(widths);
        _scroll = new SkinnedScrollBar { Skin = ScrollBarSkin.FromInterface(Textures), Position = new(814, 240), Size = new(18, 360) };
        _content.AddChild(_scroll);
        _scroll.Range.MaxValue = Math.Max(InformationBrowser.PageSize, _rows.Length); _scroll.Range.Page = InformationBrowser.PageSize;
        _scroll.Range.Value = State.Offset;
        _scroll.Range.ValueChanged += value => { State.Scroll((int)value, _rows.Length); DrawRows(); };
        _scroll.UpdateArrows();
        DrawRows();
        _empty = LabelAt("暂无记录", new(430, 370), new(180, 30), 20, new Color("c8b888"));
        _empty.Visible = _rows.Length == 0;
        _count = LabelAt($"共 {_rows.Length} 项", new(224, 606), new(200, 24), 14, new Color("b8b0a0"));
        if (State.Category == InformationCategory.Chronicle)
            ActionButton("输入年表", new(464, 621), new(110, 32), BeginNote);
    }

    private float[] ColumnWidths()
    {
        if (_table!.Columns.Length == 2) return [144, 446];
        float width = 590f / _table.Columns.Length;
        return Enumerable.Repeat(width, _table.Columns.Length).ToArray();
    }
    private void BuildRows(float[] widths)
    {
        for (int slot = 0; slot < InformationBrowser.PageSize; slot++)
        {
            int visibleRow = slot;
            var button = new InformationRowButton { Textures = Textures, Position = new(224, 240 + slot * 24), Size = new(590, 24) };
            _content.AddChild(button);
            button.MouseEntered += () =>
            {
                int index = State.Offset + visibleRow;
                if (index >= _rows.Length) return;
                State.Select(_rows[index].Key.Id);
                RefreshSelection();
            };
            button.Pressed += () => ActivateRecord(State.Offset + visibleRow);
            var cells = new Label?[widths.Length];
            float x = 0;
            for (int c = 0; c < cells.Length; c++)
            {
                if (State.Category == InformationCategory.Records && c == 1)
                {
                    var rich = new GameRichTextLabel { Textures = Textures, Position = new(x + 4, 0),
                        Size = new(widths[c] - 8, 24), ClipContents = true };
                    button.AddChild(rich); _recordText.Add(rich);
                }
                else cells[c] = LabelAt("", new(x + 4, 0), new(widths[c] - 8, 24), 16, parent: button);
                x += widths[c];
            }
            _rowControls.Add((button, cells));
        }
    }
    private void DrawRows()
    {
        for (int slot = 0; slot < _rowControls.Count; slot++)
        {
            var (button, cells) = _rowControls[slot];
            int index = State.Offset + slot;
            button.Visible = index < _rows.Length;
            if (!button.Visible) continue;
            var row = _rows[index];
            for (int c = 0; c < cells.Length; c++)
            {
                if (cells[c] is not { } label) continue;
                label.Text = row.Cells[c].Text;
                label.HorizontalAlignment = row.Cells[c].Number is not null ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
            if (State.Category == InformationCategory.Records)
            {
                var rich = _recordText[slot];
                rich.Present(RecordsLog.Entries[row.Key.Id].Message.BbCode.Replace('\n', ' '),
                    new(16, new Color(Skin.Colors["map-record-normal"]), GameTextEffect.Outline, Colors.Black),
                    San9.Rendering.MessageEmphasisMode.Foreground);
                button.TooltipText = row.Cells[1].Text;
                rich.Position = new(rich.Position.X, Mathf.Floor((24 - rich.GetContentHeight()) / 2f));
            }
        }
        for (int c = 0; c < _columnHeaders.Count; c++)
            _columnHeaders[c].Text = _table!.Columns[c] + (State.SortColumn == c ? State.Descending ? " ▾" : " ▴" : "");
        if (_scroll is not null) { _scroll.Range.SetValueNoSignal(State.Offset); _scroll.UpdateArrows(); }
        RefreshSelection();
    }
    private void ActivateRecord(int index)
    {
        if (State.Category == InformationCategory.Records && index >= 0 && index < _rows.Length &&
            RecordsLog.Entries[_rows[index].Key.Id].Location is { } location)
            LocateRecord(location);
    }
    private void RefreshSelection()
    {
        for (int slot = 0; slot < _rowControls.Count; slot++)
        {
            int index = State.Offset + slot;
            _rowControls[slot].Button.Selected = index < _rows.Length && State.SelectedId == _rows[index].Key.Id;
        }
    }
    private void BeginNote()
    {
        if (_note is not null) return;
        _note = new LineEdit { PlaceholderText = "记录一件大事（最多240字）", Position = new(228, 606), Size = new(450, 34), MaxLength = 240 };
        _content.AddChild(_note); _note.GrabFocus();
        _note.TextSubmitted += text => { if (!string.IsNullOrWhiteSpace(text)) { Session.AddChronicleNote(text); CancelNote(); RefreshData(); } };
    }
    public override void _Input(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); if (_note is not null) CancelNote(); else Back(); }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Back(); }
        else if (_note is null && @event is InputEventKey { Pressed: true } key)
        {
            int i = Math.Max(0, Array.FindIndex(_rows, r => r.Key.Id == State.SelectedId));
            if (key.Keycode == Key.Enter) { ActivateRecord(i); GetViewport().SetInputAsHandled(); return; }
            int target = key.Keycode switch { Key.Up => i - 1, Key.Down => i + 1, Key.Pageup => i - 15, Key.Pagedown => i + 15, Key.Home => 0, Key.End => _rows.Length - 1, _ => i };
            if (target != i && _rows.Length > 0)
            {
                target = Math.Clamp(target, 0, _rows.Length - 1); State.Select(_rows[target].Key.Id);
                int previousOffset = State.Offset;
                if (target < State.Offset || target >= State.Offset + InformationBrowser.PageSize)
                    State.Scroll(target < State.Offset ? target : target - InformationBrowser.PageSize + 1, _rows.Length);
                if (State.Offset != previousOffset) DrawRows(); else RefreshSelection();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode is Key.Up or Key.Down or Key.Pageup or Key.Pagedown or Key.Home or Key.End)
                GetViewport().SetInputAsHandled();
        }
    }
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } mouse)
        {
            if (mouse.ButtonIndex is MouseButton.WheelDown or MouseButton.WheelUp)
            { State.Scroll(State.Offset + (mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1), _rows.Length); if (_scroll is not null) _scroll.Range.Value = State.Offset; }
        }
        AcceptEvent();
    }
    private void Tile(string role, Rect2 rect)
    {
        SkinPainter.Tile(this, Textures[role], rect);
    }
    private void Strip(string prefix, Vector2 at, float width)
    {
        var left = Textures[prefix + "-left"]; var right = Textures[prefix + "-right"];
        DrawTexture(left, at); Tile(prefix + "-middle", new(at + new Vector2(left.GetWidth(), 0), new(width - left.GetWidth() - right.GetWidth(), left.GetHeight())));
        DrawTexture(right, at + new Vector2(width - right.GetWidth(), 0));
    }
    public override void _Draw()
    {
        if (!Visible) return;
        DrawTextureRectRegion(Textures["information-background"], _body, new(0, 0, _body.Size.X, _body.Size.Y));
        Tile("information-frame-top", new(264, 96, 344, 8)); Tile("information-frame-bottom", new(408, 664, 200, 24));
        Tile("information-frame-left", new(184, 320, 24, 144)); Tile("information-frame-right", new(840, 320, 16, 144));
        DrawTexture(Textures["information-frame-top-left"], new(144, 88)); DrawTexture(Textures["information-frame-top-right"], new(608, 88));
        DrawTexture(Textures["information-frame-bottom-left"], new(144, 464)); DrawTexture(Textures["information-frame-bottom-right"], new(608, 464));
        Strip("information-title", new(144, 88), 304);
        DrawTexture(Textures["information-frame-ornament"], new(144, 88));
        Strip("information-footer", new(648, 616), 224);
        DrawRect(new(224, 240, 590, 360), new Color(0, 0, 0, .52f));
    }
    public override void _ExitTree() => _text.Dispose();
}
