using Godot;

namespace San9.Client.Godot;

/// <summary>Shared editor layout and fields; callers own draft state and callbacks.</summary>
internal static class ContentEditorControls
{
    internal static void ClearChildren(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    internal static VBoxContainer Sidebar(HSplitContainer split)
    {
        var panel = new PanelContainer { CustomMinimumSize = new(210, 0) };
        panel.AddThemeStyleboxOverride("panel", ContentEditorStyle.Surface("202833", "394758", 12)); split.AddChild(panel);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 10); panel.AddChild(column); return column;
    }

    internal static VBoxContainer PageBody(ScrollContainer scroll)
    {
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, 16);
        scroll.AddChild(margin);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 12); margin.AddChild(column); return column;
    }

    internal static void Caption(Node parent, string text, string tooltip = "")
    {
        var caption = new Label { Text = text, CustomMinimumSize = new(56, 0), VerticalAlignment = VerticalAlignment.Center };
        if (tooltip.Length > 0)
        {
            caption.TooltipText = tooltip;
            caption.MouseFilter = Control.MouseFilterEnum.Pass;
        }
        parent.AddChild(caption);
    }

    internal static VBoxContainer Section(Node parent, string title, Action<HBoxContainer>? actions = null, string tooltip = "")
    {
        var section = new VBoxContainer(); section.AddThemeConstantOverride("separation", 6); parent.AddChild(section);
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 12); section.AddChild(header);
        var heading = new Label { Text = title, CustomMinimumSize = new(56, 0), VerticalAlignment = VerticalAlignment.Center,
            TooltipText = tooltip, MouseFilter = Control.MouseFilterEnum.Pass };
        heading.AddThemeFontSizeOverride("font_size", 16);
        heading.AddThemeColorOverride("font_color", ContentEditorStyle.Accent); header.AddChild(heading);
        actions?.Invoke(header);
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        return section;
    }

    internal static GridContainer ProfileGrid(Node parent, string title, Action<HBoxContainer>? actions = null, string tooltip = "")
    {
        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12); grid.AddThemeConstantOverride("v_separation", 8);
        Section(parent, title, actions, tooltip).AddChild(grid); return grid;
    }

    internal static HFlowContainer ButtonGroup(Node parent, string title = "", string tooltip = "")
    {
        var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", 8); flow.AddThemeConstantOverride("v_separation", 8);
        (title.Length == 0 ? parent : Section(parent, title, tooltip: tooltip)).AddChild(flow); return flow;
    }

    internal static Button ActionButton(Node parent, string title, Action pressed, string name = "")
    {
        var button = new Button { Text = title, CustomMinimumSize = new(0, 34),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        if (name.Length > 0) button.Name = name;
        button.Pressed += pressed.Invoke; parent.AddChild(button); return button;
    }

    internal static LineEdit TextField(Node parent, string title, string value, Action<string> changed, string name = "")
    {
        Caption(parent, title);
        var field = new LineEdit { Text = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new(112, 34) };
        if (name.Length > 0) field.Name = name;
        field.TextChanged += changed.Invoke; parent.AddChild(field); return field;
    }

    internal static TextEdit TextArea(Node parent, string title, string value, Action<string> changed, string name = "")
    {
        var field = new TextEdit { Text = value, CustomMinimumSize = new(0, 96), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            WrapMode = TextEdit.LineWrappingMode.Boundary };
        if (name.Length > 0) field.Name = name;
        field.TextChanged += () => changed(field.Text); Section(parent, title).AddChild(field); return field;
    }

    internal static void AddText(Node parent, string title, string value, Action<string> changed, bool multiline = false, string name = "")
    {
        if (multiline) TextArea(parent, title, value, changed, name);
        else TextField(parent, title, value, changed, name);
    }

    internal static SpinBox NumericInput(Node parent, double value, double minimum, double maximum, Action<double> changed, string name = "", string tooltip = "")
    {
        var field = new SpinBox { MinValue = minimum, MaxValue = maximum, Value = value, Step = 1, Rounded = true,
            UpdateOnTextChanged = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new(112, 34), TooltipText = tooltip };
        if (name.Length > 0) field.Name = name;
        field.ValueChanged += changed.Invoke; parent.AddChild(field); return field;
    }

    internal static Button Toggle(Node parent, string title, bool value, Action<bool> changed, string name = "", string tooltip = "")
    {
        var field = new Button { Text = title, ToggleMode = true, ButtonPressed = value, CustomMinimumSize = new(64, 32), TooltipText = tooltip,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        CompanionPanelStyle.Toggle(field);
        if (name.Length > 0) field.Name = name;
        field.Toggled += changed.Invoke; parent.AddChild(field); return field;
    }

    internal static OptionButton Choices(Node parent, string title, IReadOnlyList<string> values, int selected, Action<int> changed, string name = "", string tooltip = "")
    {
        Caption(parent, title, tooltip);
        var field = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = tooltip };
        if (name.Length > 0) field.Name = name;
        foreach (string value in values) field.AddItem(value);
        field.Select(selected); field.ItemSelected += index => changed((int)index); parent.AddChild(field); return field;
    }
}
