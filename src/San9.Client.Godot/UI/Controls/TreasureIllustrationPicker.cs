using Godot;
using San9.Application.Authoring;

namespace San9.Client.Godot;

internal static class TreasureIllustrationPicker
{
    internal static void Show(Node owner, SharedDataEditorWorkspace workspace, TextureService? service,
        string? current, Action<string> selected, Action import)
    {
        var choices = workspace.TreasureImageChoices.Concat((workspace.Illustrations?.Pack.Images ?? [])
            .Select(i => (i.Key, i.Name))).DistinctBy(i => i.Key).ToArray();
        const int pageSize = 12;
        var dialog = new AcceptDialog { Title = "选择宝物插画", OkButtonText = "取消", Exclusive = true };
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 10); dialog.AddChild(column);
        var header = new HBoxContainer(); column.AddChild(header);
        var search = new LineEdit { PlaceholderText = "搜索插画名称", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Name = "TreasureIllustrationSearch" }; header.AddChild(search);
        ContentEditorControls.ActionButton(header, "导入图片", () => { dialog.QueueFree(); import(); }, "ImportTreasureIllustration");
        var scroll = new ScrollContainer { CustomMinimumSize = new(650, 410), SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; column.AddChild(scroll);
        var grid = new GridContainer { Columns = 4 }; grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10); scroll.AddChild(grid);
        var navigation = new PaginationBar { Name = "TreasureIllustrationPagination" }; column.AddChild(navigation);
        navigation.SetPage(Math.Max(0, Array.FindIndex(choices, i => i.Key == current)) / pageSize,
            (choices.Length + pageSize - 1) / pageSize);
        void Refresh()
        {
            foreach (var child in grid.GetChildren()) { grid.RemoveChild(child); child.QueueFree(); }
            var matching = choices.Where(i => i.Name.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) ||
                i.Key.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            navigation.SetPage(navigation.PageIndex, (matching.Length + pageSize - 1) / pageSize, $"{matching.Length} 张");
            foreach (var choice in matching.Skip(navigation.PageIndex * pageSize).Take(pageSize))
            {
                var button = new Button { CustomMinimumSize = new(150, 124), ToggleMode = true,
                    ButtonPressed = current == choice.Key, TooltipText = choice.Name + "\n" + choice.Key };
                CompanionPanelStyle.Toggle(button); grid.AddChild(button);
                var body = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                button.AddChild(body); body.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                body.OffsetLeft = body.OffsetTop = 6; body.OffsetRight = body.OffsetBottom = -6;
                body.AddChild(new TreasureImagePreview { Service = service, Illustrations = workspace.Illustrations,
                    ImageKey = choice.Key, CustomMinimumSize = new(96, 88) });
                body.AddChild(new Label { Text = choice.Name, HorizontalAlignment = HorizontalAlignment.Center,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = Control.MouseFilterEnum.Ignore });
                button.Pressed += () => { selected(choice.Key); dialog.QueueFree(); };
            }
            scroll.ScrollVertical = 0;
        }
        search.TextChanged += _ => { navigation.SetPage(0, navigation.PageCount); Refresh(); };
        navigation.PageChanged += _ => Refresh();
        dialog.Confirmed += () => dialog.QueueFree(); dialog.Canceled += () => dialog.QueueFree();
        owner.AddChild(dialog); Refresh(); dialog.PopupCentered(new(700, 540)); search.GrabFocus();
    }
}
