using San9.Content.Maps;

namespace San9.Importer.San9PkTc101;

/// <summary>Serialized boundary artwork defined by BEH-RENDER-0001.</summary>
public static class MapBoundaryCompiler
{
    public static MapSprite[] Compile(IReadOnlyList<San9ScenarioSquareRecord> squares)
    {
        (int Bit, int X, int Y, int Primary, int Secondary, int Orientation)[] directions =
            [(1, 32, 0, 1, 8, 3), (2, 32, 16, 8, 2, 0), (4, 0, 16, 4, 2, 3), (8, 0, 0, 1, 4, 0)];
        List<MapSprite> sprites = [];
        foreach (var square in squares)
        foreach (var direction in directions)
        {
            bool city = (square.OpaqueField0DHighEdgeMask & direction.Bit) != 0;
            if (!city && (square.OpaqueField0DLowEdgeMask & direction.Bit) == 0) continue;
            bool primary = (square.ElevationTransitionMask & direction.Primary) != 0;
            bool secondary = (square.ElevationTransitionMask & direction.Secondary) != 0;
            int shape = primary == secondary ? 0 : primary ? 1 : 2;
            int resource = (city ? 10800 : 10794) + direction.Orientation + shape;
            var center = MapTerrainCompiler.Center(square.GridX, square.GridY);
            sprites.Add(new($"boundary-{resource}", center.X - 32 + direction.X,
                center.Y - 16 + direction.Y + 10 * ((primary ? 0 : 1) - square.OffsetCode)));
        }
        return sprites.ToArray();
    }
}
