using Godot;
using San9.Application.Presentation;
using San9.Client.Godot.Diagnostics;
using San9.Content.Images;
using San9.Content.Packages;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class Main : Node
{
    public override async void _Ready()
    {
        var world = GetNode<MapWorld>("MapWorld");
        var ui = GetNode<Control>("UiLayer/UiRoot");
        var theme = new Theme { DefaultFont = GD.Load<Font>("res://assets/LXGWWenKaiMonoGBLite-Medium.ttf"), DefaultFontSize = 16 };
        ui.Theme = theme;
        try
        {
            var options = ClientOptions.Parse(OS.GetCmdlineUserArgs(), ProjectSettings.GlobalizePath("res://"));
            var loaded = await Task.Run(async () =>
            {
                var package = await ClientContentFiles.LoadAsync(options.PackageRoot);
                var images = ImageAssetFiles.Load(options.PackageRoot, package.Skin.Assets);
                if (images.Values.Any(p => p.Channels != 4)) throw new InvalidDataException("UI assets must be RGBA images.");
                return (Content: package, Images: images);
            });
            var content = loaded.Content;
            var selection = new StrategySelection(content.Scenario);
            world.BuildingPicked += selection.SelectBuilding;
            world.SquarePicked += selection.SelectSquare;
            world.SelectionCleared += selection.Clear;
            selection.Changed += () => world.Highlight(selection.Square);
            var center = MapGeometry.Center(options.CenterX, options.CenterY);
            world.FocusWorld(new(center.X, center.Y)); world.SetZoom(options.Zoom);
            world.LoadMap(content.Map, options.PackageRoot, options.Season, options.AnimationTime);
            ui.AddChild(new MapAnnotations { World = world, Selection = selection, Scenario = content.Scenario, Texts = content.Text, UiFont = theme.DefaultFont, Skin = content.Skin, SkinImages = loaded.Images });
            var hud = new StrategyHud { World = world, Selection = selection, Scenario = content.Scenario, Texts = content.Text, Skin = content.Skin, SkinImages = loaded.Images };
            ui.AddChild(hud);
            var debug = new DebugPanel { World = world }; ui.AddChild(debug);
            world.IsInputBlocked = p => (debug.Visible && debug.GetRect().HasPoint(p)) || hud.IsBlockedPoint(p);
            if (options.Smoke || options.Screenshot is not null)
                AddChild(new ClientVerification { World = world, Selection = selection, Hud = hud, Options = options });
        }
        catch (Exception e)
        {
            GD.PushError($"客户端内容加载失败：{e}");
            ui.AddChild(new Label { Text = $"内容加载失败：{e.Message}\n请运行 San9.Tools import-client。", Position = new(24, 120), Size = new(970, 300), AutowrapMode = TextServer.AutowrapMode.WordSmart });
            if (OS.GetCmdlineUserArgs().Contains("--map-quit")) GetTree().Quit(1);
        }
    }
}
