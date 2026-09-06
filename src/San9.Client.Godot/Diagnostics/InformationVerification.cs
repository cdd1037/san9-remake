using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot.Diagnostics;

public partial class ClientVerification
{
    private async Task RunInformationChecks()
    {
        var panel = Hud.Information;
        var before = World.Session.Snapshot;
        var selected = Selection.Square;
        string directory = Path.GetDirectoryName(Options.Screenshot ?? Path.GetFullPath("information.png"))!;
        async Task Frame() { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        panel.OpenMenu(); await Frame();
        Check(Hud.IsBlockedPoint(new(512, 384)), "Information menu did not block map.");
        await Capture(Path.Combine(directory, "information-menu.png"), false);
        var query = new InformationQuery(World.Session, Hud.Texts);
        foreach (var category in Enum.GetValues<InformationCategory>())
        {
            var point = new Vector2(894, 75 + (int)category * 24);
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            await Frame();
            var hovered = panel.HoverMenuButton;
            Check(hovered is not null && hovered.Title == InformationQuery.MenuTitle(category), "Menu hover did not reach the expected item.");
            Check(Hud.MessageText == Hud.Texts.Resolve(query.MenuHintKey(category)), "Menu explanation binding failed.");
            Check(hovered!.VisualFrame == (hovered.Disabled ? 3 : 1), "Menu hover frame failed.");
        }
        var outside = new Vector2(500, 400);
        GetViewport().PushInput(new InputEventMouseMotion { Position = outside, GlobalPosition = outside }, true);
        await Frame();
        Check(Hud.MessageText == "", "Menu explanation did not clear on leave.");
        GD.Print("INFORMATION_MENU_OK ten hover bindings, disabled reason/frame, leave clears explanation");
        foreach (var category in Enum.GetValues<InformationCategory>())
        {
            panel.Open(category); await Frame();
            Check(panel.Visible && panel.State.Category == category, "Information page failed to open.");
            await Capture(Path.Combine(directory, $"information-{category.ToString().ToLowerInvariant()}.png"), false);
            if (category == InformationCategory.Officers)
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Down, Pressed = true }); await Frame();
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Down, Pressed = false });
                int? officer = panel.State.SelectedId;
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = true }); await Frame();
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = false });
                Check(panel.State.IsDetail, "Enter did not open officer detail.");
                await Capture(Path.Combine(directory, "information-officer-detail.png"), false);
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true }); await Frame();
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = false });
                Check(!panel.State.IsDetail && panel.State.SelectedId == officer, "Detail return lost selection.");
            }
            panel.Back(); await Frame();
            Check(!panel.Visible, "Information page failed to close.");
        }
        Check(World.Session.Snapshot.StateHash == before.StateHash && selected == Selection.Square, "Information browsing changed simulation or map selection.");
        GD.Print("INFORMATION_SMOKE_OK ten pages, modal blocking, engine keyboard detail/return, unchanged simulation");
    }
}
