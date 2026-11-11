using Godot;
using San9.Content.Authoring;
using San9.Rendering;

namespace San9.Client.Godot;

internal static class OfficerFlagField
{
    public static string Name(OfficerFlagCatalog? flags, int glyph) => glyph < 0 ? "无字" :
        flags?.Surnames.FirstOrDefault(p => p.Value == glyph).Key ?? (glyph == 0 ? "帥" : "自定旗字");

    public static CenterContainer Add(Node parent, TextureService? service, OfficerFlagCatalog? flags,
        int glyph, Action choose, Action reset)
    {
        var section = ContentEditorControls.Section(parent, "旗号");
        var image = new CenterContainer { CustomMinimumSize = new(0, 112), Name = "OfficerFlagGlyph",
            TooltipText = Name(flags, glyph) };
        section.AddChild(image);
        Refresh(image, service, flags, glyph);
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 6); section.AddChild(actions);
        var select = ContentEditorControls.ActionButton(actions, "选择", choose, "ChooseFlagGlyph");
        select.Disabled = flags is null || service is null; select.TooltipText = "选择旗字";
        select.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var automatic = ContentEditorControls.ActionButton(actions, "自动", reset, "ResetFlagGlyph");
        automatic.Disabled = flags is null; automatic.TooltipText = "按当前姓氏重新关联旗字。";
        automatic.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return image;
    }

    public static void Refresh(CenterContainer image, TextureService? service, OfficerFlagCatalog? flags, int glyph)
    {
        image.TooltipText = Name(flags, glyph);
        foreach (var child in image.GetChildren()) { image.RemoveChild(child); child.QueueFree(); }
        if (service is not null && FlagVisuals.Layers(5, 0, glyph).All(service.AnimationClips.ContainsKey))
            image.AddChild(new FlagPreview { Service = service, Glyph = glyph, PreviewScale = 2 });
        else image.AddChild(new Label { Text = service is null ? "旗帜资源未就绪" : "旗字资源不可用" });
    }

    public static void Show(Node owner, TextureService service, OfficerFlagCatalog flags, int current, Action<int> selected)
    {
        int[] glyphs = [-1, ..flags.Glyphs.Order()];
        const int pageSize = 24;
        var dialog = new AcceptDialog { Title = "选择旗字", OkButtonText = "取消", Exclusive = true };
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 10); dialog.AddChild(column);
        var grid = new GridContainer { Columns = 8, CustomMinimumSize = new(704, 222) };
        grid.AddThemeConstantOverride("h_separation", 6); grid.AddThemeConstantOverride("v_separation", 6); column.AddChild(grid);
        var navigation = new PaginationBar { Name = "FlagPagination" }; column.AddChild(navigation);
        navigation.SetPage(Math.Max(0, Array.IndexOf(glyphs, current)) / pageSize, (glyphs.Length + pageSize - 1) / pageSize,
            $"{glyphs.Length} 个旗字");
        void Refresh()
        {
            foreach (var child in grid.GetChildren()) { grid.RemoveChild(child); child.QueueFree(); }
            foreach (int glyph in glyphs.Skip(navigation.PageIndex * pageSize).Take(pageSize))
            {
                var button = new Button { CustomMinimumSize = new(82, 68), TooltipText = Name(flags, glyph) };
                if (glyph == current) button.AddThemeStyleboxOverride("normal", ContentEditorStyle.Surface("33495c", "b3a36c", 4));
                button.AddChild(new FlagPreview { Service = service, Glyph = glyph, Position = new(7, 8) });
                button.Pressed += () =>
                {
                    selected(glyph);
                    dialog.QueueFree();
                };
                grid.AddChild(button);
            }
        }
        navigation.PageChanged += _ => Refresh();
        dialog.Confirmed += () => dialog.QueueFree(); dialog.Canceled += () => dialog.QueueFree();
        owner.AddChild(dialog); Refresh(); dialog.PopupCentered(new(750, 310));
    }
}
