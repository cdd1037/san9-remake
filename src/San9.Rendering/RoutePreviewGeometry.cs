using San9.Content.Maps;
using San9.Core.Domain;

namespace San9.Rendering;

public static class RoutePreviewGeometry
{
    // Converted flags place the pole's foot at the bottom-center pixel.
    public static (int X, int Y) Waypoint(MapPackage map, MapCell cell, int width, int height)
    {
        var center = MapGeometry.Center(cell.X, cell.Y);
        return (center.X - width / 2, center.Y - 10 * (map.Squares[cell.Y * map.Width + cell.X].Elevation - 1) - (height - 1));
    }

    public static (int X, int Y) Glow(MapPackage map, MapCell cell)
    {
        var center = MapGeometry.Center(cell.X, cell.Y);
        return (center.X - 16, center.Y - 12 - 10 * map.Squares[cell.Y * map.Width + cell.X].Elevation);
    }

    // Stable source-to-target phase: the light pulse travels toward increasing route indices.
    public static int Frame(double elapsedMilliseconds, int index) =>
        MapGeometry.Mod((int)(elapsedMilliseconds / 200 % 8) - index % 8, 8);
}
