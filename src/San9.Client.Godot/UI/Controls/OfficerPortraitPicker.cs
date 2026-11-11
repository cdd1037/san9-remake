using Godot;
using San9.Content.Images;

namespace San9.Client.Godot;

internal static class OfficerPortraitPicker
{
    internal static Control Preview(TextureService? service, string? key, Vector2 size, PortraitView view = PortraitView.Full)
    {
        if (key is not null && service is not null && service.Portraits.ContainsKey(key))
            return new AsyncPortrait { Service = service, Role = key, View = view,
                CustomMinimumSize = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        var blank = new ColorRect { Color = new("303d50"), CustomMinimumSize = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        var label = new Label { Text = key is null ? "未选择头像" : "头像不可用",
            TooltipText = key ?? "", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); blank.AddChild(label); return blank;
    }

    internal static void Show(Node owner, TextureService service, string? current, Action<string> selected)
    {
        var portraits = service.Portraits.Values.OrderBy(p => p.Id).ToArray();
        if (portraits.Length == 0) return;
        const int pageSize = 24;
        var dialog = new AcceptDialog { Title = "选择头像", OkButtonText = "取消", Exclusive = true };
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 10); dialog.AddChild(column);
        var filter = new LineEdit { PlaceholderText = "搜索头像编号或名称", Name = "PortraitSearch" }; column.AddChild(filter);
        var scroll = new ScrollContainer { CustomMinimumSize = new(790, 430), SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; column.AddChild(scroll);
        var grid = new GridContainer { Columns = 8 }; grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10); scroll.AddChild(grid);
        var navigation = new PaginationBar { Name = "PortraitPagination" }; column.AddChild(navigation);
        navigation.SetPage(Math.Max(0, Array.FindIndex(portraits, p => p.Image.Id == current)) / pageSize,
            (portraits.Length + pageSize - 1) / pageSize);
        void Refresh()
        {
            foreach (var child in grid.GetChildren()) { grid.RemoveChild(child); child.QueueFree(); }
            var matching = portraits.Where(p => (p.DisplayName ?? p.Id.ToString()).Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            navigation.SetPage(navigation.PageIndex, (matching.Length + pageSize - 1) / pageSize, $"{matching.Length} 个头像");
            foreach (var portrait in matching.Skip(navigation.PageIndex * pageSize).Take(pageSize))
            {
                var button = new Button { CustomMinimumSize = new(88, 136), ToggleMode = true,
                    ButtonPressed = portrait.Image.Id == current, TooltipText = portrait.DisplayName ?? portrait.Id.ToString() };
                CompanionPanelStyle.Toggle(button);
                grid.AddChild(button);
                var content = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                content.OffsetLeft = content.OffsetTop = 4; content.OffsetRight = content.OffsetBottom = -4;
                button.AddChild(content);
                content.AddChild(Preview(service, portrait.Image.Id, new(80, 100), PortraitView.Face));
                content.AddChild(new Label { Text = portrait.DisplayName ?? portrait.Id.ToString(), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, HorizontalAlignment = HorizontalAlignment.Center,
                    MouseFilter = Control.MouseFilterEnum.Ignore });
                button.Pressed += () =>
                {
                    selected(portrait.Image.Id); dialog.QueueFree();
                };
            }
            scroll.ScrollVertical = 0;
        }
        navigation.PageChanged += _ => Refresh();
        filter.TextChanged += _ => { navigation.SetPage(0, navigation.PageCount); Refresh(); };
        dialog.Confirmed += () => dialog.QueueFree(); dialog.Canceled += () => dialog.QueueFree();
        owner.AddChild(dialog); Refresh(); dialog.PopupCentered(new(850, 620));
    }
}
