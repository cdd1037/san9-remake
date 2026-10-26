using Godot;

namespace San9.Client.Godot;

public partial class StrategyOverlayHost : Control
{
    private ModalStack _modals = null!;
    public override void _EnterTree() => _modals = ModalStack.Attach(this);
    public void AddBackground(Node root) => _modals.AddBackground(root);
    public static StrategyOverlayHost Create() =>
        GD.Load<PackedScene>("res://UI/Strategy/StrategyOverlayHost.tscn").Instantiate<StrategyOverlayHost>();

    public InteractionLocks Workflows { get; } = new();

    public void AddWorkflow(Control view, InteractionBlock blocked = InteractionBlock.Orders)
    {
        var lease = Workflows.Acquire(blocked);
        void Release() { view.TreeExiting -= Release; lease.Dispose(); }
        view.TreeExiting += Release;
        try { AddChild(view); _modals.Register(view); }
        catch { view.TreeExiting -= Release; lease.Dispose(); throw; }
    }

    public bool HasModal => _modals.HasModal;
}
