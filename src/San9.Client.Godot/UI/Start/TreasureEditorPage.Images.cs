using Godot;
using San9.Content.Images;
using San9.Content.Scenarios;

namespace San9.Client.Godot;

public partial class TreasureEditorPage
{
    private void BuildIllustration(Node parent, int id, TreasureDefinition treasure)
    {
        var column = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkBegin };
        column.AddThemeConstantOverride("separation", 8); parent.AddChild(column);
        var image = new PanelContainer();
        image.AddThemeStyleboxOverride("panel", ContentEditorStyle.Surface("151c24", "465260", 6)); column.AddChild(image);
        image.AddChild(new TreasureImagePreview { Service = Service, Illustrations = Workspace.Illustrations,
            ImageKey = treasure.ImageKey, Name = "TreasureImage", CustomMinimumSize = new(148, 148) });
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 6); column.AddChild(buttons);
        var change = Guard<string?>(key => { Edit(id, t => t with { ImageKey = key }); Rebuild(); });
        var choose = ContentEditorControls.ActionButton(buttons, "更换", () =>
            TreasureIllustrationPicker.Show(this, Workspace, Service, treasure.ImageKey, key => change(key), () => ImportImage(id)), "ChooseTreasureImage");
        var clear = ContentEditorControls.ActionButton(buttons, "清除", () => change(null), "ClearTreasureImage");
        foreach (var button in new[] { choose, clear }) ContentEditorStyle.Compact(button);
        choose.Disabled = Workspace.Illustrations is null;
        clear.Disabled = treasure.ImageKey is null;
        choose.TooltipText = "选择已有插画或导入图片。";
        clear.TooltipText = "清除当前宝物的插画引用。";
    }

    private void ImportImage(int id)
    {
        var dialog = new FileDialog { Title = "导入宝物插画", FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.png,*.jpg,*.jpeg,*.webp; 图片"], UseNativeDialog = true };
        var imported = Guard<string>(path =>
        {
            try
            {
                using var image = new Image();
                if (image.Load(path) != Error.Ok) throw new InvalidDataException("无法读取所选图片。");
                new ImageAsset("import", "import.png", image.GetWidth(), image.GetHeight(), 4).Validate();
                image.Convert(Image.Format.Rgba8);
                var pixels = new PngImage(image.GetWidth(), image.GetHeight(), 4, image.GetData());
                string name = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(name)) name = "宝物插画";
                var entry = Workspace.Illustrations!.Add(name, pixels);
                Edit(id, t => t with { ImageKey = entry.Key }); Rebuild();
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            { ShowImageError(error.Message); }
        });
        dialog.FileSelected += path => { dialog.QueueFree(); imported(path); };
        dialog.Canceled += () => dialog.QueueFree(); AddChild(dialog); dialog.PopupCentered(new(800, 560));
    }

    private void ShowImageError(string message)
    {
        var dialog = new AcceptDialog { Title = "插画操作失败", DialogText = message, OkButtonText = "关闭", Exclusive = true };
        dialog.Confirmed += () => dialog.QueueFree(); dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog); dialog.PopupCentered(new(560, 180));
    }
}
