using Godot;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class MapWorld
{
    private readonly EdgeScrollController _edgeScroll = new();
    private readonly EdgeScrollCursor _edgeCursor = new();
    private Vector2 _pointer = new(-1, -1);
    public event Action<EdgeScrollMode>? EdgeScrollModeChanged;
    public Func<Vector2, bool>? IsNavigationBlocked { get; set; }
    public Func<bool>? CanNavigate { get; set; }

    private bool NavigationAvailable => _map is not null && HasCompleteViewport && !IsLoading && LoadError is null &&
        GetWindow().HasFocus() && CanNavigate?.Invoke() != false;

    private bool EdgeInputAvailable => NavigationAvailable && !_mapDrag.IsHeld &&
        !Input.IsMouseButtonPressed(MouseButton.Middle) && IsNavigationBlocked?.Invoke(_pointer) != true;

    private CanvasPoint PointerPoint => new(_pointer.X, _pointer.Y);
    private CanvasPoint ViewportExtent => new(ViewportSize.X, ViewportSize.Y);

    private void ResetNavigation()
    {
        _pointer = new(-1, -1);
        _mapDrag.Cancel();
        _edgeScroll.ReleaseLeft();
        _edgeCursor.Show(default, false);
    }

    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseMotion motion) _pointer = motion.Position;
        if (input is not InputEventMouseButton button) return;
        _pointer = button.Position;
        if (button.ButtonIndex == MouseButton.Middle && !button.Pressed) _mapDrag.Cancel();
        if (button.ButtonIndex == MouseButton.Middle && button.Pressed)
        {
            _edgeScroll.CancelGesture();
            _edgeCursor.Show(default, false);
        }
        if (button.ButtonIndex != MouseButton.Left) return;
        bool consumed = button.Pressed
            ? _edgeScroll.PressLeft(PointerPoint, ViewportExtent, EdgeInputAvailable)
            : _edgeScroll.ReleaseLeft();
        if (consumed) GetViewport().SetInputAsHandled();
    }

    private void UpdateEdgeScrolling(double delta)
    {
        bool available = EdgeInputAvailable;
        var direction = EdgeScrollController.Direction(PointerPoint, ViewportExtent, available);
        bool active = _edgeScroll.IsActive(available && !direction.IsNone, Input.IsMouseButtonPressed(MouseButton.Left));
        if (!direction.IsNone && _map is not null)
        {
            // Cursor availability follows the same movement rule as drag and edge scrolling.
            var probe = MapCameraBoundary.Move(_requested.X, _requested.Y, direction.X, direction.Y, _map.Squares);
            if (Math.Abs(probe.X - _requested.X) + Math.Abs(probe.Y - _requested.Y) < .001) direction = default;
        }
        _edgeCursor.Show(direction, active);
        if (!active) return;
        var movement = EdgeScrollController.Displacement(direction, delta, RequestedZoom, IsVisibleReady);
        if (movement != default)
            MoveWorld(movement.X, movement.Y);
    }

    private bool HandleNavigationKey(InputEvent input)
    {
        if (input is not InputEventKey { Keycode: Key.F8, Pressed: true, Echo: false } || !NavigationAvailable) return false;
        _edgeScroll.ToggleMode();
        _edgeCursor.Show(default, false);
        EdgeScrollModeChanged?.Invoke(_edgeScroll.Mode);
        GetViewport().SetInputAsHandled();
        return true;
    }
}
