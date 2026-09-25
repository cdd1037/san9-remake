using Godot;
using San9.Application.Presentation;
using San9.Rendering;
using static San9.Client.Godot.Diagnostics.VerificationSupport;

namespace San9.Client.Godot.Diagnostics;

public partial class ClientVerification
{
    private async Task RunInformationChecks()
    {
        var panel = Hud.Information;
        var before = World.Session.Snapshot;
        var selected = Selection.Square;
        string directory = Path.GetDirectoryName(Options.Screenshot ?? Path.GetFullPath(Path.Combine(Options.PackageRoot, "../../verification/information/information.png")))!;
        Task Frame() => Frames(this, 2);
        panel.OpenMenu(); await Frame();
        Check(Hud.IsBlockedPoint(new(512, 384)), "Information menu did not block map.");
        await Capture(Path.Combine(directory, "information-menu.png"));
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
            await Capture(Path.Combine(directory, $"information-{category.ToString().ToLowerInvariant()}.png"));
            if (category == InformationCategory.Officers)
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Down, Pressed = true }); await Frame();
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Down, Pressed = false });
                int? officer = panel.State.SelectedId;
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = true }); await Frame();
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = false });
                Check(panel.State.IsDetail, "Enter did not open officer detail.");
                await Capture(Path.Combine(directory, "information-officer-detail.png"));
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
    private async Task CaptureUiVariants(string path)
    {
        string Variant(string suffix) => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + suffix + ".png");
        bool boundaries = World.ShowBoundaries;
        World.ShowBoundaries = false;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Capture(Variant("-boundaries-off"));
        World.ShowBoundaries = boundaries;
        Hud.Overview.Open();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Capture(Variant("-overview"));
        Hud.Overview.Toggle(San9.Application.Presentation.OverviewLayer.Forces);
        Hud.Overview.Toggle(San9.Application.Presentation.OverviewLayer.Facilities);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Capture(Variant("-overview-terrain"));
        Hud.Overview.Toggle(San9.Application.Presentation.OverviewLayer.Forces);
        Hud.Overview.Toggle(San9.Application.Presentation.OverviewLayer.Facilities);
        Hud.Overview.Close();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Hud.FocusFacility(Hud.Session.View.Forces.Single(f => f.Id == Hud.Session.View.PlayerForceId).LeaderSettlementId!.Value);
        await WaitForView();
        foreach (var (suffix, point) in new[] { ("-hover", new Vector2(726, 28)), ("-disabled-hover", new Vector2(974, 698)) })
        {
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Capture(Variant(suffix));
        }
        await Hud.CaptureFacilityStatesAsync(async suffix =>
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Capture(Variant(suffix));
        });
        Selection.Clear();
        foreach (var (suffix, id) in new[] { ("-region-gate", 99), ("-region-port", 100) })
        {
            var building = World.Package!.Buildings.Single(b => b.Id == id);
            var ground = MapGeometry.Center(building.X, building.Y);
            World.FocusWorld(new(ground.X, ground.Y));
            await WaitForView();
            var pointer = World.WorldToViewport(new(ground.X, ground.Y));
            GetViewport().PushInput(new InputEventMouseMotion { Position = pointer, GlobalPosition = pointer }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Capture(Variant(suffix));
        }
        var initial = MapGeometry.Center(89, 64); World.FocusWorld(new(initial.X, initial.Y));
        await WaitForView();
        GetViewport().PushInput(new InputEventMouseMotion { Position = new(512, 400), GlobalPosition = new(512, 400) }, true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
