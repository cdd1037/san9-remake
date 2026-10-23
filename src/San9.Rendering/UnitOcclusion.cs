using San9.Core.Domain;

namespace San9.Rendering;

public readonly record struct UnitOccluder(double Left, double Top, double Right, double Bottom, int Reach, bool HasCourtyard)
{
    public static UnitOccluder FromGeographicArea(MapOccupancy area, int reach, bool hasCourtyard)
    {
        double cell = MapCoordinates.UnitsPerCell;
        return new(Math.Floor(area.Left / cell), Math.Floor(area.Top / cell),
            Math.Ceiling(area.Right / cell), Math.Ceiling(area.Bottom / cell), reach, hasCourtyard);
    }
}

public static class UnitOcclusion
{
    // Screen ground coordinates are independent of sprite anchors and terrain-height displacement.
    public static bool IsOccluded(double groundX, double groundY, IEnumerable<UnitOccluder> buildings, int width, int height)
    {
        if (!MapGeometry.TrySquare(groundX, groundY, out int x, out int y, width, height)) return false;
        foreach (var b in buildings)
            for (int step = 1; step <= b.Reach; step++)
            {
                double px = x + step, py = y + step;
                if (px < b.Left || px >= b.Right || py < b.Top || py >= b.Bottom) continue;
                if (b.HasCourtyard && x + 1 >= b.Left + 1 && x + 1 < b.Right - 1 &&
                    y + 1 >= b.Top + 1 && y + 1 < b.Bottom - 1) continue;
                return true;
            }
        return false;
    }
}
