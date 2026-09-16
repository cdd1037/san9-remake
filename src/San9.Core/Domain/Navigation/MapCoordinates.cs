using System.Text.Json.Serialization;

namespace San9.Core.Domain;

/// <summary>A terrain cell, independent of map dimensions and display pixels.</summary>
public readonly record struct MapCell(
    [property: JsonRequired] int X,
    [property: JsonRequired] int Y);

/// <summary>A position measured in fine map units, not terrain cells or pixels.</summary>
public readonly record struct WorldPosition(
    [property: JsonRequired] int X,
    [property: JsonRequired] int Y);

public static class MapCoordinates
{
    public const int UnitsPerCell = 5;

    public static WorldPosition Center(MapCell cell)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cell.X);
        ArgumentOutOfRangeException.ThrowIfNegative(cell.Y);
        return new(checked(cell.X * UnitsPerCell + UnitsPerCell / 2), checked(cell.Y * UnitsPerCell + UnitsPerCell / 2));
    }

    public static MapCell Cell(WorldPosition position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position.X);
        ArgumentOutOfRangeException.ThrowIfNegative(position.Y);
        return new(position.X / UnitsPerCell, position.Y / UnitsPerCell);
    }

    public static long Distance(WorldPosition first, WorldPosition second)
    {
        long dx = (long)first.X - second.X, dy = (long)first.Y - second.Y;
        ulong square = checked((ulong)(dx * dx) + (ulong)(dy * dy));
        // Integer square root: deterministic even at floating-point rounding boundaries.
        ulong root = 0, bit = 1UL << 62;
        while (bit > square) bit >>= 2;
        while (bit != 0)
        {
            if (square >= root + bit) { square -= root + bit; root = (root >> 1) + bit; }
            else root >>= 1;
            bit >>= 2;
        }
        return (long)root;
    }
}
