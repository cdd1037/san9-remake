using Godot;
using San9.Content.Maps;

namespace San9.Client.Godot;

public partial class MapEditor
{
    private static readonly string[] ToolNames = ["地貌", "地域", "通行与影响", "建设地表", "桥", "据点布局"];
    private static readonly string[] Seasons = ["spring", "summer", "autumn", "winter"];
    private readonly List<Button> _toolButtons = [];
    private ConstructionSurface _constructionView = ConstructionSurface.General;
    private VBoxContainer _mapArea = null!;
    private HFlowContainer _viewControls = null!;
    private HBoxContainer _brushControls = null!;
    private SpinBox _brushSize = null!;
    private OptionButton _season = null!;
    private Button _gridToggle = null!, _regionToggle = null!, _buildingToggle = null!, _constructionToggle = null!;
    private Label _propertyTitle = null!, _zoomLabel = null!, _mapSize = null!;

    private void LayoutViewport()
    {
        GlobalPosition = Vector2.Zero;
        Size = GetViewportRect().Size;
    }

    private void BuildLayout()
    {
        MouseFilter = MouseFilterEnum.Stop;
        LayoutViewport();
        GetViewport().SizeChanged += LayoutViewport;
        var surface = new PanelContainer();
        AddChild(surface);
        surface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        surface.AddThemeStyleboxOverride("panel", ContentEditorStyle.Surface("1b222c", "1b222c", 12));
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 10);
        surface.AddChild(layout);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        layout.AddChild(header);
        _title = new Label { Text = "地图编辑器", CustomMinimumSize = new(124, 0), MouseFilter = MouseFilterEnum.Pass };
        _title.AddThemeFontSizeOverride("font_size", 20);
        header.AddChild(_title);
        header.AddChild(new Label { Text = "共享地图", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _undo = ContentEditorControls.ActionButton(header, "撤销", () => History(false));
        _undo.TooltipText = "Ctrl+Z";
        _redo = ContentEditorControls.ActionButton(header, "重做", () => History(true));
        _redo.TooltipText = "Ctrl+Shift+Z / Ctrl+Y";
        _save = ContentEditorControls.ActionButton(header, "保存", () => _ = Save(), "SaveMap");
        _save.TooltipText = "Ctrl+S";
        CompanionPanelStyle.Primary(_save);
        _close = ContentEditorControls.ActionButton(header, "关闭", RequestClose);

        var body = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = [280] };
        layout.AddChild(body);
        var sidebar = ContentEditorControls.Sidebar(body);
        sidebar.CustomMinimumSize = new(256, 0);
        var tools = new GridContainer { Columns = 2, Name = "MapEditorTools" };
        tools.AddThemeConstantOverride("h_separation", 6);
        tools.AddThemeConstantOverride("v_separation", 6);
        sidebar.AddChild(tools);
        var group = new ButtonGroup();
        for (int i = 0; i < ToolNames.Length; i++)
        {
            int tool = i;
            var button = new Button { Text = ToolNames[i], ToggleMode = true, ButtonGroup = group,
                ButtonPressed = i == _tool, CustomMinimumSize = new(112, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = $"快捷键 {i + 1}" };
            CompanionPanelStyle.Toggle(button);
            button.Pressed += () => SelectTool(tool);
            tools.AddChild(button);
            _toolButtons.Add(button);
        }
        _brushControls = FieldRow(sidebar);
        ContentEditorControls.Caption(_brushControls, "笔刷半径");
        _brushSize = ContentEditorControls.NumericInput(_brushControls, _radius, 0, 12, value =>
        {
            FinishStroke();
            _radius = (int)value;
            if (_canvas is not null) _canvas.BrushRadius = _radius;
        }, "MapEditorBrushRadius", "0 为单格，半径以格为单位");
        _propertyTitle = new Label();
        _propertyTitle.AddThemeColorOverride("font_color", ContentEditorStyle.Accent);
        sidebar.AddChild(new HSeparator());
        sidebar.AddChild(_propertyTitle);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        sidebar.AddChild(scroll);
        _properties = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _properties.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_properties);
        sidebar.AddChild(new HSeparator());
        _position = new Label { Text = "将鼠标移到地图上查看格数据", CustomMinimumSize = new(0, 92),
            AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _position.AddThemeFontSizeOverride("font_size", 14);
        _position.AddThemeColorOverride("font_color", ContentEditorStyle.Muted);
        sidebar.AddChild(_position);

        _mapArea = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _mapArea.AddThemeConstantOverride("separation", 8);
        body.AddChild(_mapArea);
        BuildViewControls();
        var footer = new HBoxContainer();
        layout.AddChild(footer);
        _status = new Label { Name = "MapEditorStatus", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Pass };
        _status.AddThemeFontSizeOverride("font_size", 13);
        footer.AddChild(_status);
        _mapSize = new Label();
        _mapSize.AddThemeFontSizeOverride("font_size", 13);
        _mapSize.AddThemeColorOverride("font_color", ContentEditorStyle.Muted);
        footer.AddChild(_mapSize);
    }

    private void BuildViewControls()
    {
        _viewControls = new HFlowContainer();
        _viewControls.AddThemeConstantOverride("h_separation", 6);
        _viewControls.AddThemeConstantOverride("v_separation", 6);
        _mapArea.AddChild(_viewControls);
        _season = new OptionButton { TooltipText = "预览季节", CustomMinimumSize = new(64, 32) };
        foreach (string season in new[] { "春", "夏", "秋", "冬" }) _season.AddItem(season);
        _season.ItemSelected += index => { FinishStroke(); if (_canvas is not null) _canvas.Season = Seasons[index]; };
        _viewControls.AddChild(_season);
        _gridToggle = ContentEditorControls.Toggle(_viewControls, "网格", false, value => { if (_canvas is not null) _canvas.ShowGrid = value; });
        _regionToggle = ContentEditorControls.Toggle(_viewControls, "地域", false, value => { if (_canvas is not null) _canvas.ShowRegions = value; });
        _buildingToggle = ContentEditorControls.Toggle(_viewControls, "据点", true, value => { if (_canvas is not null) _canvas.ShowBuildings = value; });
        _constructionToggle = ContentEditorControls.Toggle(_viewControls, "建设", false, visible =>
        {
            FinishStroke();
            if (_canvas is not null) _canvas.ConstructionOverlay = visible ? _constructionView : ConstructionSurface.None;
        }, tooltip: "按左侧建设地表面板所选类型展示建设资格");
        ContentEditorControls.ActionButton(_viewControls, "全图", () => _canvas?.Fit()).TooltipText = "F";
        ContentEditorControls.ActionButton(_viewControls, "−", () => _canvas?.ZoomBy(1 / 1.2f));
        _zoomLabel = new Label { Text = "80%", CustomMinimumSize = new(48, 32), VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center };
        _viewControls.AddChild(_zoomLabel);
        ContentEditorControls.ActionButton(_viewControls, "+", () => _canvas?.ZoomBy(1.2f));
        ContentEditorControls.ActionButton(_viewControls, "100%", () => _canvas?.SetZoom(1));
        ContentEditorControls.ActionButton(_viewControls, "重试", () => _canvas?.Retry()).TooltipText = "重新准备预览资源";
        var hint = new Label { Text = "左键涂刷 · Ctrl+左键吸取 · 空格+左键／右键拖图 · 滚轮缩放",
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        hint.AddThemeFontSizeOverride("font_size", 13);
        hint.AddThemeColorOverride("font_color", ContentEditorStyle.Muted);
        _mapArea.AddChild(hint);
    }

    private void SelectTool(int tool)
    {
        if (_busy) return;
        FinishStroke();
        _tool = tool;
        _toolButtons[tool].SetPressedNoSignal(true);
        if (tool == 3) SetConstructionView(_constructionView);
        BuildProperties();
    }

    private void SetConstructionView(ConstructionSurface surface)
    {
        FinishStroke();
        _constructionView = surface;
        _constructionToggle.SetPressedNoSignal(true);
        if (_canvas is not null) _canvas.ConstructionOverlay = surface;
    }

    private static HBoxContainer FieldRow(Node parent)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        parent.AddChild(row);
        return row;
    }

    private HBoxContainer PaintField(string title, bool enabled, Action<bool> changed)
    {
        var row = FieldRow(_properties);
        var toggle = new CheckBox { Text = title, ButtonPressed = enabled, CustomMinimumSize = new(84, 0),
            TooltipText = "勾选后涂刷此字段" };
        toggle.Toggled += changed.Invoke;
        row.AddChild(toggle);
        return row;
    }

    private void NumberChoices(IEnumerable<int> values, int selected, Action<int> changed, string name)
    {
        var choices = new HFlowContainer { Name = name };
        choices.AddThemeConstantOverride("h_separation", 2);
        choices.AddThemeConstantOverride("v_separation", 2);
        _properties.AddChild(choices);
        var group = new ButtonGroup();
        foreach (int value in values)
        {
            var button = new Button { Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ToggleMode = true, ButtonGroup = group, ButtonPressed = value == selected };
            choices.AddChild(button);
            CompanionPanelStyle.Toggle(button);
            ContentEditorStyle.Compact(button);
            button.CustomMinimumSize = new(24, 24);
            button.Pressed += () => changed(value);
        }
    }
}
