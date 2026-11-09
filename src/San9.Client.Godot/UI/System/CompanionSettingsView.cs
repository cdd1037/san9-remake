using Godot;
using San9.Client.Godot.Settings;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Edits a draft; closing saves and applies only an actual configuration change.</summary>
public partial class CompanionSettingsView : ModalView
{
    public CompanionConfiguration Initial { get; init; } = CompanionConfiguration.Default;
    public Action<CompanionConfiguration> SaveAndApply { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private CompanionSettings _settings = CompanionSettings.Default;
    private readonly List<(ToggleOption Option, Button Control)> _toggleEditors = [];
    private readonly List<(NumericOption Option, SpinBox Control)> _numericEditors = [];
    private OptionButton _playerAutomation = null!;
    private Label _automationHint = null!;
    private VBoxContainer _specializedOptions = null!;
    private Label _failure = null!;
    private Button _formationAppearance = null!;

    public override void _Ready()
    {
        _settings = Initial.Rules;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Theme = CompanionPanelStyle.Create(GetThemeFont("font"));
        var backdrop = new ColorRect { Color = new(0, 0, 0, .42f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(backdrop); backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var panel = new PanelContainer
        {
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0, AnchorBottom = 1,
            OffsetLeft = 64, OffsetRight = -64, OffsetTop = 20, OffsetBottom = -20,
        };
        AddChild(panel);
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 16);
        panel.AddChild(margin);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 8); margin.AddChild(column);
        var title = new Label { Text = "优化伴侣" };
        title.AddThemeFontSizeOverride("font_size", 24); column.AddChild(title);
        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; column.AddChild(tabs);
        foreach (var page in Pages)
        {
            var scroll = new ScrollContainer { Name = page.Title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            list.AddThemeConstantOverride("separation", 20); scroll.AddChild(list); tabs.AddChild(scroll);
            foreach (var section in page.Sections) AddSection(list, section);
        }
        _failure = Note(""); _failure.Visible = false;
        _failure.AddThemeColorOverride("font_color", new("ffb4ab")); column.AddChild(_failure);
        var divider = new ColorRect { Color = new("343e4b"), CustomMinimumSize = new(0, 1), MouseFilter = MouseFilterEnum.Ignore };
        column.AddChild(divider);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 12); column.AddChild(actions);
        var hint = Note("关闭时保存并生效 · Esc 关闭");
        hint.VerticalAlignment = VerticalAlignment.Center;
        actions.AddChild(hint);
        var preset = new Button { Text = "预设配置", CustomMinimumSize = new(104, 34),
            TooltipText = "直属军团选择内政／探索，允许玩家干预委任军团；开启普通功能开关（含无限寿命），阵型联动／阻击调整保持关闭；允许伪报、扰乱、激励、救援、交还、重视内政方针征兵及跨军团输送／救援，禁止离间、流言、烧夷、夺取、赠与、请求、劝降。\n资金与兵粮倍率200%，能力上限与免阵亡能力门槛125，无退路俘获加成0%，其余数值保留当前输入。\n战斗阵亡仍须在PK设定中开启。" };
        preset.Pressed += ApplyPreset; actions.AddChild(preset);
        var reset = new Button { Text = "原版配置", CustomMinimumSize = new(104, 34),
            TooltipText = "恢复原版对应配置。" };
        reset.Pressed += () => { RefreshEditors(CompanionSettings.Default); SetFormationAppearance(false); }; actions.AddChild(reset);
        var close = new Button { Text = "关闭", CustomMinimumSize = new(96, 34) };
        CompanionPanelStyle.Primary(close);
        close.Pressed += Back; actions.AddChild(close);
        RefreshAutomationAvailability();
        close.GrabFocus();
    }

    private void AddSection(VBoxContainer list, SectionDefinition section)
    {
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8); list.AddChild(body);
        body.AddChild(CompanionPanelStyle.SectionHeading(section.Title));
        foreach (var option in section.Options)
        {
            switch (option)
            {
                case PlayerAutomationOption automation:
                    AddPlayerAutomation(body, automation.Title);
                    break;
                case FormationAppearanceOption appearance:
                    _formationAppearance = CreateToggle(Initial.FormationAppearanceEnabled, 96, "开启", "关闭", _ => _failure.Hide());
                    _formationAppearance.Name = "FormationAppearance";
                    OptionRow(body, appearance.Title, appearance.Note).AddChild(_formationAppearance);
                    break;
                case ToggleOption toggleOption:
                {
                    var row = OptionRow(body, toggleOption.Title, toggleOption.Note);
                    if (toggleOption.Parameter is { } parameter)
                    {
                        row.AddChild(new Label { Text = parameter.Title, TooltipText = parameter.Note,
                            MouseFilter = MouseFilterEnum.Pass, VerticalAlignment = VerticalAlignment.Center });
                        row.AddChild(NumberEditor(parameter, 88));
                    }
                    row.AddChild(ToggleEditor(toggleOption));
                    break;
                }
                case NumericOption numericOption:
                    OptionRow(body, numericOption.Title, numericOption.Note).AddChild(NumberEditor(numericOption));
                    break;
                case NumberGroupOption numberGroup:
                    NumberGroup(body, numberGroup.Title, numberGroup.Note, numberGroup.Fields, numberGroup.Width);
                    break;
            }
        }
        if (section.Permissions is { } permissions) AutomaticPermissions(body, permissions);
    }

    private void AddPlayerAutomation(VBoxContainer list, string title)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", CompanionPanelStyle.Card()); list.AddChild(card);
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 8); card.AddChild(body);
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 16); body.AddChild(header);
        var heading = new Label { Text = title, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center };
        heading.AddThemeFontSizeOverride("font_size", 18); header.AddChild(heading);
        _playerAutomation = new OptionButton { CustomMinimumSize = new(160, 32) };
        foreach (string mode in new[] { "关闭", "内政／探索", "完整安排" }) _playerAutomation.AddItem(mode);
        _playerAutomation.Select((int)_settings.PlayerCorpsAutomation);
        _playerAutomation.ItemSelected += index => Edit(_settings with { PlayerCorpsAutomation = (PlayerCorpsAutomationMode)index });
        header.AddChild(_playerAutomation);
        _automationHint = Note(""); body.AddChild(_automationHint);
        body.AddChild(new ColorRect { Color = new("3b495b"), CustomMinimumSize = new(0, 1),
            MouseFilter = MouseFilterEnum.Ignore });
        _specializedOptions = new VBoxContainer();
        _specializedOptions.AddThemeConstantOverride("separation", 10); body.AddChild(_specializedOptions);
        foreach (var option in DomesticAutomationOptions)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); _specializedOptions.AddChild(row);
            var description = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            description.AddThemeConstantOverride("separation", 2); row.AddChild(description);
            description.AddChild(new Label { Text = option.Title });
            description.AddChild(Note(option.Note));
            if (option.Parameter is { } parameter)
            {
                row.AddChild(new Label { Text = parameter.Title, TooltipText = parameter.Note,
                    MouseFilter = MouseFilterEnum.Pass, VerticalAlignment = VerticalAlignment.Center });
                var number = NumberEditor(parameter, 88); number.SizeFlagsVertical = SizeFlags.ShrinkCenter; row.AddChild(number);
            }
            var toggle = ToggleEditor(option); toggle.SizeFlagsVertical = SizeFlags.ShrinkCenter; row.AddChild(toggle);
        }
    }

    private void AutomaticPermissions(VBoxContainer list, Func<bool, IEnumerable<ToggleOption>> options)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        card.AddThemeStyleboxOverride("panel", CompanionPanelStyle.Card()); list.AddChild(card);
        var table = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        table.AddThemeConstantOverride("h_separation", 16);
        table.AddThemeConstantOverride("v_separation", 6); card.AddChild(table);
        table.AddChild(new Label { Text = "命令", CustomMinimumSize = new(64, 0) });
        foreach (string scope in new[] { "电脑势力", "玩家势力AI" })
        {
            var label = new Label { Text = scope, CustomMinimumSize = new(112, 0),
                HorizontalAlignment = HorizontalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", 14); table.AddChild(label);
        }
        foreach (var pair in options(false).Zip(options(true)))
        {
            var option = pair.First;
            table.AddChild(new Label { Text = option.Title, TooltipText = option.Note,
                CustomMinimumSize = new(64, 0), VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Pass });
            foreach (var (scope, optionToggle) in new[] { ("电脑势力", pair.First), ("玩家势力AI", pair.Second) })
            {
                var toggle = ToggleEditor(optionToggle, 112);
                toggle.TooltipText = $"{scope}：{option.Note}";
                table.AddChild(toggle);
            }
        }
    }

    private Button ToggleEditor(ToggleOption option, int width = 96)
    {
        var toggle = CreateToggle(option.Read(_settings), width, option.EnabledText, option.DisabledText,
            value => Edit(option.Write(_settings, value)));
        toggle.TooltipText = option.Note;
        _toggleEditors.Add((option, toggle));
        return toggle;
    }

    private static Button CreateToggle(bool enabled, int width, string enabledText, string disabledText, Action<bool> edit)
    {
        var toggle = new Button { Text = enabled ? enabledText : disabledText, ToggleMode = true,
            ButtonPressed = enabled, CustomMinimumSize = new(width, 32) };
        CompanionPanelStyle.Toggle(toggle);
        toggle.Toggled += value =>
        {
            edit(value);
            toggle.Text = value ? enabledText : disabledText;
        };
        return toggle;
    }

    private void NumberGroup(VBoxContainer list, string title, string note, NumericOption[] options, int width)
    {
        var row = OptionRow(list, title, note);
        var numbers = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        numbers.AddThemeConstantOverride("separation", 12); row.AddChild(numbers);
        foreach (var option in options)
        {
            var field = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            numbers.AddChild(field);
            var label = new Label { Text = option.Title, TooltipText = option.Note,
                MouseFilter = MouseFilterEnum.Pass, HorizontalAlignment = HorizontalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", 14); field.AddChild(label);
            field.AddChild(NumberEditor(option, width));
        }
    }

    private SpinBox NumberEditor(NumericOption option, int width = 112)
    {
        var number = new SpinBox { MinValue = option.Minimum, MaxValue = option.Maximum, Step = 1,
            Value = option.Read(_settings), CustomMinimumSize = new(width, 32), Suffix = option.Suffix,
            TooltipText = option.Note };
        number.GetLineEdit().TooltipText = option.Note;
        number.ValueChanged += value => Edit(option.Write(_settings, (int)value));
        _numericEditors.Add((option, number));
        return number;
    }

    private static HBoxContainer OptionRow(VBoxContainer list, string title, string description)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", CompanionPanelStyle.Card()); list.AddChild(card);
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 4); card.AddChild(body);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 16); body.AddChild(row);
        var label = new Label { Text = title, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 18); row.AddChild(label);
        body.AddChild(Note(description));
        return row;
    }

    private static Label Note(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", new("aab6c5"));
        return label;
    }

    private void ApplyPreset()
    {
        SetFormationAppearance(true);
        foreach (var (_, control) in _numericEditors) control.Apply();
        var settings = _settings with { PlayerCorpsAutomation = PlayerCorpsAutomationMode.DomesticAndSearch };
        foreach (var (option, _) in _toggleEditors) settings = option.Write(settings, option.PresetValue);
        foreach (var (option, _) in _numericEditors)
            if (option.PresetValue is { } value) settings = option.Write(settings, value);
        RefreshEditors(settings);
    }

    private void Edit(CompanionSettings settings)
    {
        _settings = settings;
        _failure.Hide();
        RefreshAutomationAvailability();
    }

    private void RefreshAutomationAvailability()
    {
        bool enabled = _settings.PlayerCorpsAutomation == PlayerCorpsAutomationMode.DomesticAndSearch;
        _automationHint.Text = _settings.PlayerCorpsAutomation switch
        {
            PlayerCorpsAutomationMode.Disabled => "未启用自动安排。下方专项配置保留，选择“内政／探索”后可用。",
            PlayerCorpsAutomationMode.DomesticAndSearch => "结束玩家操作后，按下方设置先做内政、再探索，仅使用剩余行动资格。",
            PlayerCorpsAutomationMode.Full => "AI统一安排并遵守“玩家势力AI”许可；待答由玩家选择，保留本旬已下令部队的命令。下方专项配置不参与。",
            _ => throw new InvalidOperationException("Unknown player automation mode.")
        };
        _specializedOptions.Modulate = enabled ? Colors.White : new Color(1, 1, 1, .55f);
        foreach (var (option, control) in _toggleEditors)
            if (option.DomesticOnly) control.Disabled = !enabled;
        foreach (var (option, control) in _numericEditors)
            if (option.DomesticOnly) control.Editable = enabled;
    }

    private void SetFormationAppearance(bool enabled)
    {
        _formationAppearance.SetPressedNoSignal(enabled);
        _formationAppearance.Text = enabled ? "开启" : "关闭";
    }

    private void RefreshEditors(CompanionSettings settings)
    {
        Edit(settings);
        _playerAutomation.Select((int)settings.PlayerCorpsAutomation);
        foreach (var (option, control) in _toggleEditors)
        {
            bool enabled = option.Read(_settings);
            control.SetPressedNoSignal(enabled);
            control.Text = enabled ? option.EnabledText : option.DisabledText;
        }
        foreach (var (option, control) in _numericEditors)
        {
            // Hidden tabs do not redraw SpinBox text after a Range value change.
            // Apply the replacement text so a later close cannot restore the old input.
            control.GetLineEdit().Text = option.Read(settings).ToString(System.Globalization.CultureInfo.InvariantCulture);
            control.Apply();
        }
    }

    public void Back()
    {
        try
        {
            foreach (var (_, control) in _numericEditors) control.Apply();
            var configuration = new CompanionConfiguration(_settings, _formationAppearance.ButtonPressed);
            if (configuration != Initial) SaveAndApply(configuration);
            Closed();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or
            InvalidDataException or InvalidOperationException)
        {
            _failure.Text = $"配置未能保存或应用：{error.Message}\n修改仍保留在面板中，请重试关闭。";
            _failure.Show();
        }
    }

    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            GetViewport().SetInputAsHandled(); Back();
        }
    }

    public override void _GuiInput(InputEvent input) => AcceptEvent();
}
