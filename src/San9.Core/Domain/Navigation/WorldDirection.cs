namespace San9.Core.Domain;

// Clockwise in map coordinates: north is negative Y, east is positive X.
public enum WorldDirection { North, NorthEast, East, SouthEast, South, SouthWest, West, NorthWest }

public static class WorldDirections
{
    public static WorldDirection? Between(WorldPosition from, WorldPosition to)
    {
        long dx = (long)to.X - from.X, dy = (long)to.Y - from.Y;
        if (dx == 0 && dy == 0) return null;
        long x = Math.Abs(dx), y = Math.Abs(dy);
        // Compare against tan(22.5 degrees) exactly, without floating-point sector boundaries.
        if (x > y && (Int128)(x - y) * (x - y) > 2 * (Int128)y * y)
            return dx > 0 ? WorldDirection.East : WorldDirection.West;
        if (y > x && (Int128)(y - x) * (y - x) > 2 * (Int128)x * x)
            return dy > 0 ? WorldDirection.South : WorldDirection.North;
        return dx > 0
            ? dy > 0 ? WorldDirection.SouthEast : WorldDirection.NorthEast
            : dy > 0 ? WorldDirection.SouthWest : WorldDirection.NorthWest;
    }
}
