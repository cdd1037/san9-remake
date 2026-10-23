using San9.Content.Maps;
using San9.Core.Domain;

namespace San9.Rendering;

public sealed record TargetMesh(int ElevationMask, int X, int Y, float Opacity);

public static class TargetOverlayGeometry
{
    public const float DimOpacity = 0.5f;
    public const float InfluenceOpacity = 51f / 256;
    public const float StrongInfluenceOpacity = 1 - (1 - InfluenceOpacity) * (1 - InfluenceOpacity);

    public static IEnumerable<TargetMesh> Meshes(MapPackage map, IReadOnlySet<MapCell> clear, IReadOnlySet<MapCell> influence)
    {
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var cell = new MapCell(x, y);
            bool red = influence.Contains(cell);
            if (!red && clear.Contains(cell)) continue;
            var square = map.Squares[y * map.Width + x];
            var center = MapGeometry.Center(x, y, map.Height);
            // The 64x44 mesh's flat diamond begins at row 12, not at the texture origin.
            yield return new(square.ElevationMask,
                center.X - 32, center.Y - 16 - 10 * square.Elevation, red ? (clear.Contains(cell) ? InfluenceOpacity : StrongInfluenceOpacity) : DimOpacity);
        }
    }

    // Integer scan conversion of the four terrain corners, sampled at (x + 0.5, y).
    // The shared half-open vertical interval gives adjacent tiles one owner per pixel.
    public static byte[] Coverage(int elevationMask)
    {
        if (elevationMask is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(elevationMask));
        var pixels = new byte[64 * 44];
        int top = 11 - ((elevationMask & 1) != 0 ? 10 : 0);
        int bottom = 43 - ((elevationMask & 2) != 0 ? 10 : 0);
        for (int x = 0; x < 64; x++)
        {
            int side = 27 - ((elevationMask & (x < 32 ? 4 : 8)) != 0 ? 10 : 0);
            int distance = Math.Abs(2 * x + 1 - 64);
            int upper = top * 64 + (side - top) * distance;
            int lower = bottom * 64 + (side - bottom) * distance;
            for (int y = 0; y < 44; y++)
                if (y * 64 >= upper && y * 64 < lower) pixels[y * 64 + x] = 255;
        }
        return pixels;
    }

    public static (int X, int Y) BuildingMarker(MapPackage map, MapBuilding building, int offsetX, int offsetY)
    {
        var center = MapGeometry.Center(building.X, building.Y, map.Height);
        int x = center.X + offsetX, y = center.Y + offsetY;
        int height = MapGeometry.TrySquare(x, y, out int cellX, out int cellY, map.Width, map.Height)
            ? 10 * (map.Squares[cellY * map.Width + cellX].Elevation - 1) : 0;
        return (x, y - height);
    }
}
