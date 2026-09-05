using San9.Content.Maps;

namespace San9.Importer.San9PkTc101;

internal static class MapCloudCompiler
{
    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
    public static (int?[] Left, int?[] Right) Edges(MapSquare[] squares)
    {
        List<(int X, int Y)> left = [], right = [];
        (int, int) Point(int x, int y, int direction)
        {
            var center = MapTerrainCompiler.Center(x, y); var s = squares[y * 200 + x];
            var d = direction switch { 1 => (32, 0), 4 => (0, 16), 8 => (64, 16), _ => throw new InvalidDataException() };
            return (center.X - 32 + d.Item1, center.Y - 16 + d.Item2 + 10 - 10 * (s.Elevation + ((s.ElevationMask & direction) != 0 ? 1 : 0)));
        }
        for (int diagonal = 0; diagonal <= 398; diagonal++)
        {
            if (diagonal <= 199)
            {
                left.Add(Point(0, diagonal, 1)); right.Add(Point(diagonal, 0, 1));
                if (diagonal == 199) { left.Add(Point(0, 199, 4)); right.Add(Point(199, 0, 8)); }
            }
            else { left.Add(Point(diagonal - 199, 199, 4)); right.Add(Point(199, diagonal - 199, 8)); }
        }
        int?[] Raster(List<(int X, int Y)> points)
        {
            int?[] rows = new int?[6656];
            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1]; var b = points[i];
                if (a.Y == b.Y) { if (a.Y >= 0 && a.Y < rows.Length) rows[a.Y] = (a.X + b.X) / 2; continue; }
                for (int y = Math.Max(0, Math.Min(a.Y, b.Y)); y <= Math.Min(rows.Length - 1, Math.Max(a.Y, b.Y)); y++)
                    rows[y] = a.X + FloorDiv((y - a.Y) * (b.X - a.X), b.Y - a.Y);
            }
            return rows;
        }
        return (Raster(left), Raster(right));
    }
    public static MapSprite[] Foreground(MapSquare[] squares, MapBuilding[] buildings, IReadOnlyDictionary<string, MapAsset> assets, IEnumerable<(int Side, int Id, int X, int Y)> placements)
    {
        var bounds = buildings.Select(b => (Left: b.Sprites.Min(s => s.X), Top: b.Sprites.Min(s => s.Y), Right: b.Sprites.Max(s => s.X + assets[s.AssetId].Width), Bottom: b.Sprites.Max(s => s.Y + assets[s.AssetId].Height))).ToArray();
        List<MapSprite> output = [];
        foreach (int side in Enumerable.Range(0, 4)) for (int period = -1; period < 7; period++) foreach (var p in placements.Where(p => p.Side == side))
        {
            var origin = side switch { 0 => (6400 - 1536 * period, 768 * period), 1 => (6400 + 1536 * period, 768 * period), 2 => (1536 * period, 3200 + 768 * period), _ => (12800 - 1536 * period, 3200 + 768 * period) };
            int worldY = origin.Item2 + p.Y;
            int start = FloorDiv(worldY + (side < 2 ? 400 : 0), 16), end = Math.Min(start + 15, 398); start = Math.Max(start, 0);
            int n = Math.Max(1, end - start + 1), sum = 0;
            for (int i = start; i < end; i++)
            {
                bool left = side is 0 or 2;
                int index = i <= 199 ? (left ? i * 200 : i) : (left ? 199 * 200 + i - 199 : (i - 199) * 200 + 199);
                sum += squares[index].Elevation;
            }
            int x = 96 + origin.Item1 + p.X, y = 112 + worldY - FloorDiv(10 * (n + sum), n);
            string id = $"cloud-{p.Id}"; var image = assets[id];
            if (bounds.Any(b => x < b.Right && b.Left < x + image.Width && y < b.Bottom && b.Top < y + image.Height)) id = $"cloud-{p.Id - 11}";
            image = assets[id];
            if (x < 13056 && y < 6656 && x + image.Width > 0 && y + image.Height > 0) output.Add(new(id, x, y));
        }
        return output.ToArray();
    }
}
