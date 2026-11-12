using San9.Content.Maps;
using static San9.Content.Maps.MapDisplayMetrics;

namespace San9.Rendering;

/// <summary>The projected ground surface of one cell, including its four raised corners.</summary>
public readonly record struct MapCellSurface(double CenterX, double CenterY, int ElevationMask)
{
    public double LeftX => CenterX - HalfCellWidth;
    public double RightX => CenterX + HalfCellWidth;
    public double TopY => CenterY - HalfCellHeight - ((ElevationMask & 1) != 0 ? ElevationStep : 0);
    public double RightY => CenterY - ((ElevationMask & 8) != 0 ? ElevationStep : 0);
    public double BottomY => CenterY + HalfCellHeight - ((ElevationMask & 2) != 0 ? ElevationStep : 0);
    public double LeftY => CenterY - ((ElevationMask & 4) != 0 ? ElevationStep : 0);

    public static MapCellSurface Create(int x, int y, int mapHeight, MapSquare square)
    {
        var center = MapGeometry.Center(x, y, mapHeight);
        return new(center.X, center.Y - (double)ElevationStep * (square.Elevation - 1), square.ElevationMask);
    }

    public bool Contains(double x, double y)
    {
        double dx = x - CenterX;
        if (Math.Abs(dx) > HalfCellWidth) return false;
        double side = dx < 0 ? LeftY : RightY;
        double t = Math.Abs(dx) / HalfCellWidth;
        return y >= TopY + (side - TopY) * t && y < BottomY + (side - BottomY) * t;
    }
}
