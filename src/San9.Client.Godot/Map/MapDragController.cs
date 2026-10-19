namespace San9.Client.Godot;

/// <summary>Separates a held middle button from an intentional map drag.</summary>
public sealed class MapDragController
{
    private const double Threshold = 5;
    private CanvasPoint _origin, _previous;
    public bool IsHeld { get; private set; }
    public bool IsDragging { get; private set; }

    public void Press(CanvasPoint point)
    {
        _origin = _previous = point;
        IsHeld = true;
        IsDragging = false;
    }

    public void Cancel()
    {
        IsHeld = false;
        IsDragging = false;
    }

    public CanvasPoint Move(CanvasPoint point)
    {
        if (!IsHeld) return default;
        var step = new CanvasPoint(point.X - _previous.X, point.Y - _previous.Y);
        _previous = point;
        double x = point.X - _origin.X, y = point.Y - _origin.Y;
        if (x * x + y * y > Threshold * Threshold) IsDragging = true;
        // Discard motion before activation instead of applying it as a delayed jump.
        return IsDragging ? step : default;
    }
}
