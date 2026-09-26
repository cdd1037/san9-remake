namespace San9.Client.Godot;

public enum EdgeScrollMode { HoldLeftButton, Hover }

public readonly record struct EdgeScrollDirection(int X, int Y)
{
    public bool IsNone => X == 0 && Y == 0;
}

/// <summary>Owns the edge-scroll gesture; cursor appearance is an output, never an input.</summary>
public sealed class EdgeScrollController
{
    public const double EdgeWidth = 32;
    public const double Speed = 1200;
    public EdgeScrollMode Mode { get; private set; } = EdgeScrollMode.Hover;
    public bool OwnsLeftButton { get; private set; }
    private bool _armed;

    public void ToggleMode()
    {
        Mode = Mode == EdgeScrollMode.HoldLeftButton ? EdgeScrollMode.Hover : EdgeScrollMode.HoldLeftButton;
        CancelGesture();
    }

    public static EdgeScrollDirection Direction(CanvasPoint point, bool available)
    {
        if (!available || !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            point.X < 0 || point.Y < 0 || point.X >= ClientLayout.LogicalWidth || point.Y >= ClientLayout.LogicalHeight)
            return default;
        return new(point.X < EdgeWidth ? -1 : point.X >= ClientLayout.LogicalWidth - EdgeWidth ? 1 : 0,
            point.Y < EdgeWidth ? -1 : point.Y >= ClientLayout.LogicalHeight - EdgeWidth ? 1 : 0);
    }

    public bool PressLeft(CanvasPoint point, bool available)
    {
        OwnsLeftButton = !Direction(point, available).IsNone;
        _armed = OwnsLeftButton;
        return OwnsLeftButton;
    }

    public bool ReleaseLeft()
    {
        bool consumed = OwnsLeftButton;
        OwnsLeftButton = false;
        _armed = false;
        return consumed;
    }

    // Keep ownership of the release even after a modal or another gesture interrupts scrolling.
    public void CancelGesture() => _armed = false;

    public bool IsActive(bool available, bool leftPressed)
    {
        if (!available) CancelGesture();
        if (!leftPressed) ReleaseLeft();
        // Hover must not steal an ongoing UI drag or an object-click gesture.
        return available && (Mode == EdgeScrollMode.Hover && !leftPressed || _armed);
    }

    public static CanvasPoint Displacement(EdgeScrollDirection direction, double delta, double zoom, bool viewReady)
    {
        if (!viewReady || direction.IsNone) return default;
        // A stalled frame must not accumulate a large jump; pending views accrue no movement debt.
        double distance = Speed * Math.Clamp(delta, 0, .05) / zoom;
        double length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        return new(direction.X / length * distance, direction.Y / length * distance);
    }
}
