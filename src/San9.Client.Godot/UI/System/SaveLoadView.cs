using Godot;
using San9.Client.Godot.Persistence;

namespace San9.Client.Godot;

public partial class SaveLoadView : Control
{
    public SaveLoadBrowser State { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action<SaveSlotRequest> Requested { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private readonly List<SaveLoadButton> _pages = [];
    private Control _rows = null!;
    private SaveSlotConfirmation? _confirmation;
    private SaveLoadButton? _selectedRow;
    private GameDialog _notice = null!;
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        LabelAt(State.Mode == SaveLoadMode.Save ? "储存" : "载入", new(56, 28), new(48, 40), 24);
        Button("help", "", new(948, 4), new(40, 32), ShowHelp);
        Button("close", "", new(980, 24), new(40, 32), Closed);
        for (int i = 0; i < State.PageCount; i++)
        {
            int page = i;
            int start = i * SaveLoadBrowser.PageSize + 1, end = Math.Min(State.SlotCount, start + SaveLoadBrowser.PageSize - 1);
            _pages.Add(Button("page", $"{start:00}-{end:00}", new(512 + i * 60, 634), new(64, 32), () => ChangePage(page)));
        }
        Button("no", "中止", new(862, 680), new(112, 48), Closed, 24);
        AddChild(new GameMessageBar { Textures = Textures, Text = State.Mode == SaveLoadMode.Save ? "选择欲储存资料的栏位。" : "请选择欲载入的资料。" });
        _notice = new GameDialog { Textures = Textures, Visible = false, Size = Size, ZIndex = 2,
            FocusBehaviorRecursive = FocusBehaviorRecursiveEnum.Enabled };
        Textures.RequestPrefixes("dialog-");
        _notice.Closed += CloseNotice;
        AddChild(_notice);
        _rows = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_rows); RefreshRows();
        // Confirmation and notice always sit above the row and pagination controls.
        MoveChild(_notice, -1);
    }
    private void ChangePage(int page)
    {
        if (State.ChangePage(page)) RefreshRows();
    }
    private void RefreshRows()
    {
        foreach (Node row in _rows.GetChildren()) { _rows.RemoveChild(row); row.QueueFree(); }
        for (int i = 0; i < SaveLoadBrowser.PageSize; i++)
        {
            int slot = State.Page * SaveLoadBrowser.PageSize + i + 1;
            if (slot > State.SlotCount) break;
            var entry = State.Entry(slot);
            var content = new Control { MouseFilter = MouseFilterEnum.Ignore };
            var row = new SaveLoadButton { Textures = Textures, Skin = State.Mode == SaveLoadMode.Save ? "save-row" : "load-row",
                Position = new(8, 72 + i * 104), Size = new(1008, 104), DisabledFrame = 0,
                Content = content,
                Disabled = State.Mode == SaveLoadMode.Load && !State.Occupied(slot) };
            row.Pressed += () => SelectSlot(slot, row); _rows.AddChild(row);
            string edge = State.Mode == SaveLoadMode.Save ? "5828c8" : "005028";
            void RowLabel(string text, Vector2 at, Vector2 size, int fontSize, string outline, bool fixedColor = false,
                HorizontalAlignment alignment = HorizontalAlignment.Left)
            {
                var label = LabelAt(text, at, size, fontSize, content);
                label.HorizontalAlignment = alignment;
                row.StyleContentLabel(label, new(outline), fixedColor);
            }
            RowLabel(slot.ToString(), new(24, 16), new(40, 32), 24, edge);
            if (entry is null)
            {
                if (State.Error(slot) is { } error) LabelAt(error, new(176, 24), new(650, 32), 24, content, new("f06850"));
                continue;
            }
            if (entry.PortraitRole is not null)
                content.AddChild(new AsyncPortrait { Service = Service, Role = entry.PortraitRole,
                    View = San9.Content.Images.PortraitView.Face, Position = new(80, 16), Size = new(64, 80), MouseFilter = MouseFilterEnum.Ignore });
            if (State.Flag(slot) is { } flag)
                content.AddChild(new SaveSlotFlagView { Service = Service, Flag = flag, Position = new(-7, 44) });
            RowLabel(entry.Ruler, new(176, 20), new(112, 32), 24, "5828c8");
            RowLabel($"{entry.Players}人游戏", new(296, 28), new(88, 24), 16, edge);
            if (entry.Historical is bool historical)
            {
                content.AddChild(new TextureRect { Texture = Textures[historical ? "save-load-historical" : "save-load-fictional"],
                    Position = new(384, 28), MouseFilter = MouseFilterEnum.Ignore });
                RowLabel(historical ? "史实" : "虚拟", new(392, 28), new(40, 24), 16, historical ? "5828c8" : "b82000");
            }
            content.AddChild(new TextureRect { Texture = Textures["save-load-difficulty"], Position = new(440, 28), MouseFilter = MouseFilterEnum.Ignore });
            RowLabel(entry.Difficulty, new(448, 28), new(40, 24), 16, "b82000");
            RowLabel(entry.CurrentDate, new(568, 20), new(260, 32), 24, "800090");
            // The timestamp frame spans x=848..992; keep equal inner margins with the current font.
            RowLabel(entry.SavedAt.ToLocalTime().ToString("yyyy/M/d H:mm"), new(852, 16), new(136, 24), 16, "5828c8",
                alignment: HorizontalAlignment.Center);
            RowLabel(entry.StartDate, new(176, 60), new(112, 32), 24, edge);
            RowLabel(entry.Scenario, new(296, 64), new(256, 24), 16, edge);
            RowLabel("备注", new(568, 64), new(64, 24), 16, "800090", fixedColor: true);
            RowLabel(entry.Note, new(648, 64), new(280, 24), 16, "800090");
        }
        for (int i = 0; i < _pages.Count; i++) _pages[i].Selected = i == State.Page;
    }
    private void SelectSlot(int slot, SaveLoadButton row)
    {
        if (!State.Select(slot)) return;
        if (State.Mode == SaveLoadMode.Load) { Confirm(""); return; }
        _selectedRow = row; row.Selected = true;
        FocusBehaviorRecursive = FocusBehaviorRecursiveEnum.Disabled;
        _confirmation = new SaveSlotConfirmation { State = State, Textures = Textures, Cancelled = CancelSelection, Confirmed = Confirm,
            FocusBehaviorRecursive = FocusBehaviorRecursiveEnum.Enabled };
        AddChild(_confirmation);
    }
    private void CancelSelection()
    {
        State.CancelSelection();
        if (_confirmation is not null) { RemoveChild(_confirmation); _confirmation.QueueFree(); _confirmation = null; }
        FocusBehaviorRecursive = FocusBehaviorRecursiveEnum.Inherited;
        if (_selectedRow is not null) { _selectedRow.Selected = false; _selectedRow.GrabFocus(); _selectedRow = null; }
    }
    private void Confirm(string note)
    {
        var request = State.Confirm(note);
        CancelSelection();
        if (request is null) return;
        Requested(request);
    }
    public void ShowNotice(string message)
    {
        FocusBehaviorRecursive = FocusBehaviorRecursiveEnum.Disabled;
        _notice.SetNotice(message);
        _notice.Show(); _notice.FocusDefaultButton();
    }
    private void CloseNotice()
    {
        _notice.Hide();
        FocusBehaviorRecursive = FocusBehaviorRecursiveEnum.Inherited;
        _pages[State.Page].GrabFocus();
    }
    private void ShowHelp() => ShowNotice(State.Mode == SaveLoadMode.Save ? "选择栏位，输入备注后确认储存。\n已有资料的栏位将被覆盖。" : "选择已有资料的栏位后确认载入。\n载入将替换当前游戏进度，空栏位不可选择。");
    public void Back()
    {
        if (_notice.Visible) CloseNotice();
        else if (_confirmation is not null) CancelSelection();
        else Closed();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (_notice.Visible || _confirmation is not null || input is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.Pageup: ChangePage(State.Page - 1); break;
            case Key.Pagedown: ChangePage(State.Page + 1); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }
    private Label LabelAt(string title, Vector2 position, Vector2 size, int fontSize, Node? parent = null, Color? color = null)
    {
        var label = new Label { Text = title, Position = position, Size = size, ClipText = true,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        (parent ?? this).AddChild(label);
        _text.Apply(label, new(fontSize, color ?? Colors.White, GameTextEffect.Outline, Colors.Black)); return label;
    }
    private SaveLoadButton Button(string skin, string title, Vector2 at, Vector2 size, Action action, int fontSize = 16)
    {
        var button = new SaveLoadButton { Textures = Textures, Skin = skin, Title = title, Position = at, Size = size,
            FontSize = fontSize, TextColor = skin == "page" ? new("f0b0f0") : Colors.White };
        button.Pressed += action; AddChild(button); return button;
    }
    public override void _Draw()
    {
        var painter = new SaveLoadSkinPainter(this, Textures);
        painter.Background(new(0, 0, 1024, 768)); painter.Frame(new(0, 0, 1024, 736)); painter.Title(new(24, 24), 112);
        DrawTexture(Textures["save-load-footer"], new(0, 736));
        DrawTexture(Textures["save-load-pages-background"], new(498, 631));
    }
    public override void _ExitTree() => _text.Dispose();
}
