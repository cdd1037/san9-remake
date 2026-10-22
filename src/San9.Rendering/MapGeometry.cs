using San9.Content.Maps;
using static San9.Content.Maps.MapDisplayMetrics;

namespace San9.Rendering;

public static class MapGeometry
{
    public static (int X, int Y) Center(int x, int y) => (HalfCellWidth * (x + Height - y) + OriginX, HalfCellHeight * (x + y + 1) + OriginY);
    public static bool TrySquare(double px, double py, out int x, out int y)
    {
        double difference = (px - OriginX) / HalfCellWidth - Height;
        double sum = (py - OriginY) / HalfCellHeight - 1;
        x = (int)Math.Floor((sum + difference) / 2 + 0.5);
        y = (int)Math.Floor((sum - difference) / 2 + 0.5);
        return x is >= 0 and < Width && y is >= 0 and < Height;
    }
    public static int Mod(int value, int period) => (value % period + period) % period;

    // Display height is continuous between cell centers. Logical terrain lookup
    // still uses TrySquare; rounding a moving foot position would create steps.
    public static double SampleElevation(double px, double py, int width, int height, IReadOnlyList<MapSquare> squares)
    {
        double difference = (px - OriginX) / HalfCellWidth - Height;
        double sum = (py - OriginY) / HalfCellHeight - 1;
        double x = Math.Clamp((sum + difference) / 2, 0, width - 1);
        double y = Math.Clamp((sum - difference) / 2, 0, height - 1);
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        int x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
        double top = squares[y0 * width + x0].Elevation * (1 - (x - x0)) + squares[y0 * width + x1].Elevation * (x - x0);
        double bottom = squares[y1 * width + x0].Elevation * (1 - (x - x0)) + squares[y1 * width + x1].Elevation * (x - x0);
        return top * (1 - (y - y0)) + bottom * (y - y0);
    }
}
