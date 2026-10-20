using Godot;

namespace San9.Client.Godot;

public partial class StrategyOverlayHost : Control
{
    public static StrategyOverlayHost Create() =>
        GD.Load<PackedScene>("res://UI/Strategy/StrategyOverlayHost.tscn").Instantiate<StrategyOverlayHost>();

    public InteractionLocks Workflows { get; } = new();

    public void AddWorkflow(Control view, InteractionBlock blocked = InteractionBlock.Orders)
    {
        var lease = Workflows.Acquire(blocked);
        void Release() { view.TreeExiting -= Release; lease.Dispose(); }
        view.TreeExiting += Release;
        try { AddChild(view); }
        catch { view.TreeExiting -= Release; lease.Dispose(); throw; }
    }

    public bool HasModal => GetChildren().OfType<Control>().Any(view => view.IsVisibleInTree());
}
