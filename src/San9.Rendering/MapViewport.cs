namespace San9.Rendering;

public readonly record struct MapViewport(double X, double Y, double Zoom)
{
    public MapViewport Clamp(IReadOnlyList<San9.Content.Maps.MapSquare> squares)
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Zoom) || Zoom <= 0)
            throw new ArgumentOutOfRangeException(nameof(Zoom));
        double zoom = Math.Clamp(Zoom, .25, 2);
        var center = MapCameraBoundary.Clamp(X, Y, squares);
        return new(center.X, center.Y, zoom);
    }

    public IEnumerable<(int X, int Y)> Chunks(int canvasWidth, int canvasHeight, int width, int height, int margin = 0)
    {
        double halfWidth = width / (2 * Zoom), halfHeight = height / (2 * Zoom);
        int size = TerrainRenderer.ChunkSize;
        int x0 = Math.Max(0, (int)Math.Floor((X - halfWidth) / size) - margin);
        int y0 = Math.Max(0, (int)Math.Floor((Y - halfHeight) / size) - margin);
        int x1 = Math.Min((canvasWidth - 1) / size, (int)Math.Ceiling((X + halfWidth) / size) - 1 + margin);
        int y1 = Math.Min((canvasHeight - 1) / size, (int)Math.Ceiling((Y + halfHeight) / size) - 1 + margin);
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) yield return (x, y);
    }
}

// Resource readiness belongs to the caller; only a complete view can become presented.
public sealed class MapViewportTransition(MapViewport initial)
{
    public MapViewport Requested { get; private set; } = initial;
    public MapViewport Presented { get; private set; } = initial;
    public bool HasPresented { get; private set; }
    public long Revision { get; private set; }
    public bool IsPending => !HasPresented || Requested != Presented;
    public void Request(MapViewport view) => Requested = view;
    public bool TryPresent(Func<MapViewport, bool> isReady)
    {
        if (!IsPending || !isReady(Requested)) return false;
        PresentPrepared(Requested);
        return true;
    }
    public void PresentPrepared(MapViewport view)
    {
        Presented = view;
        HasPresented = true;
        Revision++;
    }
}
