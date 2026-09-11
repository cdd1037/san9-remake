using Godot;

namespace San9.Client.Godot;

public partial class ScrollRange : VScrollBar
{
    public Action<int>? Scrolled { get; init; }
    public override void _Input(InputEvent @event)
    {
        if (IsVisibleInTree() && @event is InputEventMouseButton { Pressed: true } mouse &&
            GetGlobalRect().HasPoint(mouse.Position) && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            Scrolled?.Invoke(mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1);
            GetViewport().SetInputAsHandled();
        }
    }
}
