using Godot;
using San9.Application.Presentation;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Asset-free engine checks for ownership, tree teardown and map callback replacement.</summary>
public partial class WorkflowLifecycleVerification : Node
{
    public override void _Ready() => Callable.From(Run).CallDeferred();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private async void Run()
    {
        try
        {
            CheckWindows();
            CheckMapInputs();
            await CheckModals();
            if (System.Environment.GetEnvironmentVariable("SAN9_MODAL_CONTENT") is { Length: > 0 } content) await CheckContentModals(content);
            GD.Print("WORKFLOW_LIFECYCLE_OK windows=normal,hidden,owner-first,host-first,failure inputs=replaced,repeated-release,unowned-exit modals=reused,nested,focus,raw-input,dynamic,teardown");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private void CheckWindows()
    {
        var host = new StrategyOverlayHost(); AddChild(host);
        var locks = host.Workflows;
        using (var windows = new WorkflowWindows(host))
        {
            int released = 0;
            var first = new Control(); var second = new Control();
            windows.Show(first, () => released++, InteractionBlock.Orders | InteractionBlock.Playback);
            windows.Show(second, () => released++, InteractionBlock.Continuation);
            first.Hide();
            Check(locks.Blocked == (InteractionBlock.Orders | InteractionBlock.Playback | InteractionBlock.Continuation), "Hidden window lost its lock.");
            windows.Close(first); windows.Close(first);
            Check(released == 1 && locks.Blocked == InteractionBlock.Continuation, "Closing one window released another owner or completed twice.");
            windows.Dispose(); windows.Dispose();
            Check(released == 2 && windows.IsEmpty && locks.Blocked == InteractionBlock.None, "Owner-first teardown leaked a window or lock.");
        }
        host.Free();
        host = new(); AddChild(host); locks = host.Workflows;
        using (var windows = new WorkflowWindows(host))
        {
            int released = 0;
            windows.Show(new Control(), () => released++);
            host.Free();
            Check(released == 1 && windows.IsEmpty && locks.Blocked == InteractionBlock.None, "Host-first teardown retained an owner.");
        }
        host = new(); AddChild(host); locks = host.Workflows;
        try
        {
            using var windows = new WorkflowWindows(host);
            windows.Show(new Control());
            throw new InvalidDataException("injected preparation failure");
        }
        catch (InvalidDataException) { }
        Check(locks.Blocked == InteractionBlock.None && host.GetChildCount() == 0, "Failure cleanup leaked input restrictions.");
        host.Free();
    }
    private void CheckMapInputs()
    {
        var world = new MapWorld();
        Action<MapObjectTarget?> firstTarget = _ => { }, secondTarget = _ => { };
        Action<MapCell> firstCell = _ => { }, secondCell = _ => { };
        using var first = new MapTargetInputLease(world, firstTarget, firstCell, firstCell);
        using var second = new MapTargetInputLease(world, secondTarget, secondCell, secondCell);
        first.Dispose();
        Check(world.TargetPicked == secondTarget && world.WaypointPicked == secondCell && world.SitePicked == secondCell,
            "Old workflow erased replacement callbacks.");
        var military = new MilitaryCommandFlow { World = world }; AddChild(military); military.Free();
        var targets = new CommandTargetFlow { World = world }; AddChild(targets); targets.Free();
        Check(world.TargetPicked == secondTarget && world.WaypointPicked == secondCell && world.SitePicked == secondCell,
            "Inactive workflow exit erased another workflow's input.");
        second.Dispose(); second.Dispose();
        Check(world.TargetPicked is null && world.WaypointPicked is null && world.SitePicked is null, "Active input lease did not release.");
        world.Free();
    }
}
