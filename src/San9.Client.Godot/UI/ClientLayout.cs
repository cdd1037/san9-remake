namespace San9.Client.Godot;

public static class ClientLayout
{
    // Design content size. The engine owns window scaling and the actual logical viewport.
    public const int ContentWidth = 1024;
    public const int ContentHeight = 768;

    // A retained map frame stays centered while a new viewport size is being prepared.
    // Both the temporary cover and map picking use this same rectangle.
    public static CanvasRectangle PresentedMapBounds(CanvasPoint viewportSize, CanvasPoint frameSize)
    {
        double width = Math.Min(viewportSize.X, frameSize.X);
        double height = Math.Min(viewportSize.Y, frameSize.Y);
        return new((viewportSize.X - width) / 2, (viewportSize.Y - height) / 2, width, height);
    }
}

public readonly record struct CanvasPoint(double X, double Y);

public readonly record struct CanvasRectangle(double X, double Y, double Width, double Height)
{
    public bool Contains(CanvasPoint point) =>
        point.X >= X && point.Y >= Y && point.X < X + Width && point.Y < Y + Height;
}
