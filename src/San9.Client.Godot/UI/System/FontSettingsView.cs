using Godot;
using San9.Client.Godot.Settings;

namespace San9.Client.Godot;

public partial class FontSettingsView : ModalView
{
    public UiPreferencesSession Preferences { get; init; } = new();
    public Action Closed { get; init; } = null!;
    private InstalledFont[] _families = [];
    private LineEdit _search = null!;
    private CheckBox _chineseOnly = null!;
    private string _selected = "";
    private ItemList _list = null!;
    private Label _preview = null!;
    private Label _selectionName = null!;
    private Label _count = null!;
    private Label _status = null!;
    private Font _embedded = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Theme = ContentEditorStyle.Create(GetThemeFont("font"));
        _embedded = GD.Load<Font>("res://assets/LXGWWenKaiMonoGBLite-Medium.ttf");
        _families = GameFonts.InstalledChoices();
        string saved = Preferences.Current.FontFamily;
        _selected = _families.FirstOrDefault(font => font.Family.Equals(saved, StringComparison.OrdinalIgnoreCase))?.Family ?? "";

        var column = CreatePanel();
        BuildHeader(column);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 20);
        column.AddChild(body);
        BuildBrowser(body);
        BuildPreview(body);
        BuildActions(column);

        Populate();
        UpdatePreview();
        if (saved.Length > 0 && _selected.Length == 0)
            _status.Text = $"已保存字体不可用：{saved}。当前使用内置字体。";
        _search.GrabFocus();
    }

    private VBoxContainer CreatePanel()
    {
        var backdrop = new ColorRect { Color = new(0, 0, 0, .6f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var panel = new PanelContainer { AnchorRight = 1, AnchorBottom = 1,
            OffsetLeft = 64, OffsetRight = -64, OffsetTop = 80, OffsetBottom = -80 };
        AddChild(panel);
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 24);
        panel.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 18);
        margin.AddChild(column);
        return column;
    }

    private void BuildHeader(VBoxContainer column)
    {
        var header = new HBoxContainer();
        column.AddChild(header);
        var title = new Label { Text = "字体设置", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 24);
        header.AddChild(title);
        _count = new Label { VerticalAlignment = VerticalAlignment.Center,
            TooltipText = $"本机字体 {_families.Length} 款，其中 {_families.Count(font => font.ChineseMarked == true)} 款声明中文字符集。" };
        _count.AddThemeColorOverride("font_color", ContentEditorStyle.Muted);
        header.AddChild(_count);
    }

    private void BuildBrowser(HBoxContainer body)
    {
        var browser = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.3f };
        browser.AddThemeConstantOverride("separation", 10);
        body.AddChild(browser);
        _search = new LineEdit { Name = "FontSearch", PlaceholderText = "搜索字体中文名或原名", ClearButtonEnabled = true };
        browser.AddChild(_search);
        _chineseOnly = new CheckBox { Name = "ChineseFontsOnly", Text = "筛选中文字体",
            ButtonPressed = true, TooltipText = "依据字体声明的汉字范围或简繁中文代码页初筛；无法识别标记的字体保留。标记不保证实际覆盖，缺字使用内置字体补齐。" };
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
            _chineseOnly.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        browser.AddChild(_chineseOnly);
        _list = new ItemList { Name = "FontList", SizeFlagsVertical = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        browser.AddChild(_list);
        _list.ItemSelected += index =>
        {
            _selected = _list.GetItemMetadata((int)index).AsString();
            UpdatePreview();
        };
        _search.TextChanged += _ => Populate();
        _chineseOnly.Toggled += _ => Populate();
    }

    private void BuildPreview(HBoxContainer body)
    {
        var previewCard = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        previewCard.AddThemeStyleboxOverride("panel", CompanionPanelStyle.Card());
        body.AddChild(previewCard);
        var samples = new VBoxContainer();
        samples.AddThemeConstantOverride("separation", 16);
        previewCard.AddChild(samples);
        var previewTitle = new Label { Text = "预览" };
        previewTitle.AddThemeColorOverride("font_color", ContentEditorStyle.Muted);
        samples.AddChild(previewTitle);
        _selectionName = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _selectionName.AddThemeFontSizeOverride("font_size", 18);
        samples.AddChild(_selectionName);
        samples.AddChild(new HSeparator());
        _preview = new Label { Name = "FontPreview", SizeFlagsVertical = SizeFlags.ExpandFill,
            Text = "三国志 曹操 刘备 孙权\n汉室复兴，天下归一。\nSan9 Remake 0123456789",
            VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _preview.AddThemeFontSizeOverride("font_size", 24);
        samples.AddChild(_preview);
        var fallbackHint = new Label { Text = "缺字使用内置字体补齐", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        fallbackHint.AddThemeFontSizeOverride("font_size", 14);
        fallbackHint.AddThemeColorOverride("font_color", ContentEditorStyle.Muted);
        samples.AddChild(fallbackHint);
    }

    private void BuildActions(VBoxContainer column)
    {
        column.AddChild(new HSeparator());
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 12);
        column.AddChild(actions);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        _status.AddThemeFontSizeOverride("font_size", 14);
        _status.AddThemeColorOverride("font_color", ContentEditorStyle.Muted);
        actions.AddChild(_status);
        var close = new Button { Name = "CloseFontSettings", Text = "关闭", CustomMinimumSize = new(96, 36) };
        close.Pressed += Closed;
        actions.AddChild(close);
        var save = new Button { Name = "SaveFont", Text = "保存选择", CustomMinimumSize = new(120, 36) };
        CompanionPanelStyle.Primary(save);
        save.Pressed += Save;
        actions.AddChild(save);
    }

    private void Populate()
    {
        _list.Clear();
        AddFamily("", "内置字体（霞鹜文楷等宽）");
        foreach (var font in _families)
            if ((!_chineseOnly.ButtonPressed || font.ChineseMarked != false) &&
                font.DisplayName.Contains(_search.Text, StringComparison.OrdinalIgnoreCase))
                AddFamily(font.Family, font.DisplayName, font.ChineseMarked is null ? "字符集标记未知，保留供预览。" : "");
        _count.Text = $"显示 {_list.ItemCount} 款";
    }

    private void AddFamily(string family, string title, string tooltip = "")
    {
        int index = _list.AddItem(title);
        _list.SetItemMetadata(index, family);
        _list.SetItemTooltip(index, tooltip.Length == 0 ? title : $"{title}\n{tooltip}");
        if (family == _selected) _list.Select(index);
    }

    private void UpdatePreview()
    {
        _preview.AddThemeFontOverride("font", GameFonts.Create(_selected, _embedded));
        string name = _families.FirstOrDefault(font => font.Family == _selected)?.DisplayName ?? _selected;
        _selectionName.Text = _selected.Length == 0 ? "霞鹜文楷等宽（内置）" : name;
        _status.Text = _selected == Preferences.Current.FontFamily ? "重启游戏后生效" : "尚未保存 · 重启游戏后生效";
    }

    private void Save()
    {
        Preferences.Update(p => p with { FontFamily = _selected });
        _status.Text = "选择已记录，正常退出后重启生效";
    }

    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            GetViewport().SetInputAsHandled(); Closed();
        }
    }

    public override void _GuiInput(InputEvent input) => AcceptEvent();
}
