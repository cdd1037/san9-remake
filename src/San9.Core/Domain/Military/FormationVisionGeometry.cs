namespace San9.Core.Domain;

/// <summary>Directional awareness in sixteen map sectors, measured from north clockwise.</summary>
internal static class FormationVisionGeometry
{
    internal static bool Contains(WorldPosition origin, WorldDirection facing, WorldPosition target,
        FormationVision vision, int additionalRadius)
    {
        if (vision.ArcDegrees == 0) return false;
        long dx = (long)target.X - origin.X, dy = (long)target.Y - origin.Y;
        long radius = (long)vision.Radius + additionalRadius;
        // A discrete map distance is the integer square root, so its inclusive boundary
        // extends up to, but excludes, the next integer distance.
        if ((Int128)dx * dx + (Int128)dy * dy >= (Int128)(radius + 1) * (radius + 1)) return false;
        if (vision.ArcDegrees == 360) return true;
        int relative = (Bearing(dx, dy) - 2 * (int)facing + 16) % 16;
        int separation = Math.Min(relative, 16 - relative);
        return separation * 45 <= vision.ArcDegrees;
    }

    internal static int Bearing(long dx, long dy)
    {
        if (dx == 0 && dy == 0) return 0;
        if (dx == 0) return dy > 0 ? 8 : 0;
        if (dy == 0) return dx > 0 ? 4 : 12;
        long x = Math.Abs(dx), y = Math.Abs(dy);
        // The map partitions each quadrant at slopes 1/2, 1, and 2.
        // Boundary ownership follows the clockwise sweep of that quadrant.
        if (dx >= 0 && dy < 0)
            return x >= 2 * y ? 3 : x >= y ? 2 : 2 * x >= y ? 1 : 0;
        if (dx >= 0)
            return x > 2 * y ? 4 : x > y ? 5 : 2 * x > y ? 6 : 7;
        if (dy < 0)
            return x > 2 * y ? 12 : x > y ? 13 : 2 * x > y ? 14 : 15;
        return x >= 2 * y ? 11 : x >= y ? 10 : 2 * x >= y ? 9 : 8;
    }
}
