using Godot;
using San9.Client.Godot.Persistence;

namespace San9.Client.Godot;

public partial class SaveLoadView : ModalView
{
    public SaveLoadBrowser State { get; init; } = null!;
    public Settings.UiPreferencesSession Preferences { get; init; } = new();
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
    private float PageTabsLeft => 996 - (State.PageCount - 1) * 60 - 64;
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        // The modal's initial keyboard focus belongs to the window, not its first action.
        FocusMode = FocusModeEnum.All;
        LabelAt(State.Mode == SaveLoadMode.Save ? "储存" : "载入", new(56, 28), new(48, 40), 24);
        Button("help", "", new(948, 4), new(40, 32), ShowHelp);
        Button("close", "", new(980, 24), new(40, 32), Closed);
        for (int i = 0; i < State.PageCount; i++)
        {
            int page = i;
            int start = i * SaveLoadBrowser.PageSize + 1, end = Math.Min(State.SlotCount, start + SaveLoadBrowser.PageSize - 1);
            _pages.Add(Button("page", i >= State.FirstAutoSavePage ? $"自动{i - State.FirstAutoSavePage + 1}" : $"{start:00}-{end:00}", new(PageTabsLeft + i * 60, 634), new(64, 32), () => ChangePage(page)));
        }
        Button("no", "中止", new(862, 680), new(112, 48), Closed, 24);
        AddChild(new GameMessageBar { Textures = Textures, Text = State.Mode == SaveLoadMode.Save ? "选择欲储存资料的栏位。" : "请选择欲载入的资料。" });
        _notice = new GameDialog { Textures = Textures, Visible = false, Size = Size };
        Textures.RequestPrefixes("dialog-");
        _notice.Closed += _notice.Hide;
        AddChild(_notice);
        _rows = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_rows); RefreshRows();
    }
    private void ChangePage(int page)
    {
        if (State.ChangePage(page))
        {
            Preferences.Update(p => p with { SaveLoadPage = State.Page });
            RefreshRows();
        }
    }
    private void RefreshRows()
    {
        foreach (Node row in _rows.GetChildren()) { _rows.RemoveChild(row); row.QueueFree(); }
        for (int i = 0; i < SaveLoadBrowser.PageSize; i++)
        {
            int slot = State.SlotAt(i);
            if (!State.IsAutoSavePage && slot > State.SlotCount) break;
            var entry = State.Entry(slot);
            var content = new Control { MouseFilter = MouseFilterEnum.Ignore };
            var row = new SaveLoadButton { Textures = Textures, Skin = State.Mode == SaveLoadMode.Save ? "save-row" : "load-row",
                Position = new(8, 72 + i * 104), Size = new(1008, 104), DisabledFrame = 0,
                Content = content,
                Disabled = State.IsAutoSavePage && State.Mode == SaveLoadMode.Save ||
                    State.Mode == SaveLoadMode.Load && !State.Occupied(slot) };
            row.Pressed += () => SelectSlot(slot, row); _rows.AddChild(row);
            string edge = State.Mode == SaveLoadMode.Save ? "5828c8" : "005028";
            bool latest = State.IsAutoSavePage && slot - State.SlotCount == State.LatestAutoSaveSlot;
            void RowLabel(string text, Vector2 at, Vector2 size, int fontSize, string outline, Color? fixedColor = null,
                HorizontalAlignment alignment = HorizontalAlignment.Left)
            {
                var label = LabelAt(text, at, size, fontSize, content);
                label.HorizontalAlignment = alignment;
                row.StyleContentLabel(label, new(outline), fixedColor);
            }
            RowLabel(State.IsAutoSavePage ? $"A{slot - State.SlotCount}" : slot.ToString(), new(24, 16), new(56, 32), 24, edge,
                fixedColor: latest ? new Color("ffd86b") : null);
            if (entry is null)
            {
                if (State.Error(slot) is { } error) LabelAt(error, new(176, 24), new(650, 32), 24, content, new("f06850"));
                continue;
            }
            if (entry.PortraitAssetId is not null)
                content.AddChild(new AsyncPortrait { Service = Service, Role = entry.PortraitAssetId,
                    View = San9.Content.Images.PortraitView.Face, Position = new(80, 16), Size = new(64, 80), MouseFilter = MouseFilterEnum.Ignore });
            if (State.Flag(slot) is { } flag)
                content.AddChild(new FlagPreview { Service = Service, ColorCode = flag.Color, Glyph = flag.Glyph, Position = new(-7, 44) });
            RowLabel(entry.Ruler, new(176, 20), new(112, 32), 24, "5828c8");
            RowLabel($"{entry.Players}人游戏", new(296, 28), new(88, 24), 16, edge);
            if (entry.Historical is bool historical)
            {
                content.AddChild(new TextureRect { Texture = Textures.Get(historical ? "save-load-historical" : "save-load-fictional", this),
                    Position = new(384, 28), MouseFilter = MouseFilterEnum.Ignore });
                RowLabel(historical ? "史实" : "虚拟", new(392, 28), new(40, 24), 16, historical ? "5828c8" : "b82000");
            }
            content.AddChild(new TextureRect { Texture = Textures.Get("save-load-difficulty", this), Position = new(440, 28), MouseFilter = MouseFilterEnum.Ignore });
            RowLabel(entry.Difficulty, new(448, 28), new(40, 24), 16, "b82000");
            RowLabel(entry.CurrentDate, new(568, 20), new(260, 32), 24, "800090");
            // The timestamp frame spans x=848..992; keep equal inner margins with the current font.
            RowLabel(entry.SavedAt.ToLocalTime().ToString("yyyy/M/d H:mm"), new(852, 16), new(136, 24), 16, "5828c8",
                alignment: HorizontalAlignment.Center);
            RowLabel(entry.StartDate, new(176, 60), new(112, 32), 24, edge);
            RowLabel(entry.Scenario, new(296, 64), new(256, 24), 16, edge);
            RowLabel("备注", new(568, 64), new(64, 24), 16, "800090", fixedColor: new("d880f8"));
            RowLabel(entry.Note, new(648, 64), new(280, 24), 16, "800090");
        }
        for (int i = 0; i < _pages.Count; i++) _pages[i].Selected = i == State.Page;
    }
    private void SelectSlot(int slot, SaveLoadButton row)
    {
        if (!State.Select(slot)) return;
        if (State.Mode == SaveLoadMode.Load) { Confirm(""); return; }
        _selectedRow = row; row.Selected = true;
        _confirmation = new SaveSlotConfirmation { State = State, Textures = Textures, Cancelled = CancelSelection, Confirmed = Confirm };
        AddChild(_confirmation);
    }
    private void CancelSelection()
    {
        State.CancelSelection();
        if (_confirmation is not null) { RemoveChild(_confirmation); _confirmation.QueueFree(); _confirmation = null; }
        if (_selectedRow is not null) { _selectedRow.Selected = false; _selectedRow = null; }
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
        _notice.SetNotice(message);
        _notice.Show(); _notice.FocusDefaultButton();
    }
    private void ShowHelp() => ShowNotice(State.Mode == SaveLoadMode.Save ? "选择栏位，输入备注后确认储存。\n已有资料的栏位将被覆盖。自动页仅供载入。" : "选择已有资料的栏位后确认载入。\n载入将替换当前游戏进度，空栏位不可选择。\n自动档按游戏经过旬数循环覆盖A1～A10，每页五槽，位置固定。\n金色编号表示保存时间最近的自动档。");
    public void Back()
    {
        if (_notice.Visible) _notice.Hide();
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
        DrawTexture(Textures.Get("save-load-footer", this), new(0, 736));
        var background = Textures.Get("save-load-pages-background", this);
        var size = background.GetSize();
        float left = PageTabsLeft - 14;
        float width = 1010 - left;
        const float cap = 16;
        // Extend the flat center; preserve the slanted end caps and the right anchor.
        DrawTextureRectRegion(background, new(left, 631, cap, size.Y), new(0, 0, cap, size.Y));
        DrawTextureRectRegion(background, new(left + cap, 631, width - 2 * cap, size.Y), new(cap, 0, size.X - 2 * cap, size.Y));
        DrawTextureRectRegion(background, new(1010 - cap, 631, cap, size.Y), new(size.X - cap, 0, cap, size.Y));
    }
    public override void _ExitTree() => _text.Dispose();
}
