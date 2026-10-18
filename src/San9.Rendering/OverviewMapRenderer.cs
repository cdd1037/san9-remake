using San9.Content.Images;
using San9.Content.Maps;

namespace San9.Rendering;

public readonly record struct OverviewColor(byte R, byte G, byte B);
public readonly record struct OverviewPoint(double X, double Y);
public sealed record OverviewBorderPath(IReadOnlyList<OverviewPoint> Points);

/// <summary>Administrative coverage and shared boundaries prepared once from map geography.</summary>
public sealed class OverviewMapRenderer
{
    private readonly int?[] _cities;
    private readonly int?[] _pixelCities;
    public MinimapGeometry Geometry { get; }
    public IReadOnlyList<OverviewBorderPath> Borders { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }

    public OverviewMapRenderer(int width, int height, IReadOnlyList<MapSquare> squares,
        IReadOnlyDictionary<int, int> regionCities, int pixelWidth = 800, int pixelHeight = 400)
    {
        if (width <= 0 || height <= 0 || (long)width * height != squares.Count || pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentException("Invalid overview map inputs.");
        Geometry = new(width, height);
        PixelWidth = pixelWidth; PixelHeight = pixelHeight;
        _cities = new int?[squares.Count];
        _pixelCities = new int?[checked(pixelWidth * pixelHeight)];
        for (int i = 0; i < squares.Count; i++)
            // A region reference can remain on excluded sea/perimeter cells. Match region presentation,
            // rather than treating every reference as an administrative area to color or pick.
            _cities[i] = !squares[i].HideRegionName && regionCities.TryGetValue(squares[i].RegionId, out int city) ? city : null;
        var edges = new List<((int X, int Y) A, (int X, int Y) B)>();
        var neighbors = new Dictionary<(int X, int Y), List<int>>();
        void Edge(int first, int second, (int X, int Y) a, (int X, int Y) b)
        {
            if (_cities[first] == _cities[second]) return;
            foreach (var vertex in new[] { a, b })
            {
                if (!neighbors.TryGetValue(vertex, out var adjacent)) neighbors.Add(vertex, adjacent = []);
                adjacent.Add(edges.Count);
            }
            edges.Add((a, b));
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            if (x + 1 < width) Edge(i, i + 1, (x + 1, y), (x + 1, y + 1));
            if (y + 1 < height) Edge(i, i + width, (x, y + 1), (x + 1, y + 1));
        }
        var borders = new List<OverviewBorderPath>();
        var visited = new bool[edges.Count];
        void Trace((int X, int Y) start, int edge)
        {
            var vertices = new List<(int X, int Y)> { start };
            var vertex = start;
            while (!visited[edge])
            {
                visited[edge] = true;
                vertex = edges[edge].A == vertex ? edges[edge].B : edges[edge].A;
                vertices.Add(vertex);
                var adjacent = neighbors[vertex];
                if (adjacent.Count != 2) break;
                edge = adjacent[0] == edge ? adjacent[1] : adjacent[0];
            }
            var points = vertices.Select(v => {
                var p = Geometry.FromGrid(v.X - .5, v.Y - .5);
                return new OverviewPoint(p.X, p.Y);
            }).ToArray();
            borders.Add(new(RoundCorners(points, neighbors[start].Count == 2 && vertex == start)));
        }
        // Junctions anchor each shared border. The remaining degree-two components are closed loops.
        foreach (var (vertex, adjacent) in neighbors.Where(p => p.Value.Count != 2))
            foreach (int edge in adjacent)
                if (!visited[edge]) Trace(vertex, edge);
        for (int edge = 0; edge < edges.Count; edge++)
            if (!visited[edge]) Trace(edges[edge].A, edge);
        Borders = borders.AsReadOnly();
        for (int py = 0; py < pixelHeight; py++)
        for (int px = 0; px < pixelWidth; px++)
        {
            int? index = CellAt((px + .5) / pixelWidth, (py + .5) / pixelHeight);
            if (index is not int cell) continue;
            _pixelCities[py * pixelWidth + px] = _cities[cell];
        }
    }

    private int? CellAt(double u, double v) => Geometry.TryGrid(u, v, out double x, out double y)
        ? (int)Math.Floor(y + .5) * Geometry.Width + (int)Math.Floor(x + .5) : null;

    public int? CityAt(double u, double v) => CellAt(u, v) is int cell ? _cities[cell] : null;

    private IReadOnlyList<OverviewPoint> RoundCorners(OverviewPoint[] points, bool closed)
    {
        // A path returning to a junction is still anchored at both ends. Only an
        // isolated degree-two loop may round its closing vertex.
        int count = closed ? points.Length - 1 : points.Length;
        var rounded = new List<OverviewPoint>();
        for (int i = 0; i < count; i++)
        {
            var point = points[i];
            if (!closed && (i == 0 || i == count - 1)) { rounded.Add(point); continue; }
            var previous = points[(i + count - 1) % count];
            var next = points[(i + 1) % count];
            double ax = (previous.X - point.X) * PixelWidth, ay = (previous.Y - point.Y) * PixelHeight;
            double bx = (next.X - point.X) * PixelWidth, by = (next.Y - point.Y) * PixelHeight;
            if (Math.Abs(ax * by - ay * bx) < 1e-8) continue;
            double a = Math.Sqrt(ax * ax + ay * ay), b = Math.Sqrt(bx * bx + by * by);
            // The curve stays within the stroke covering the unchanged cell boundary.
            double cut = Math.Min(.8, Math.Min(a, b) / 3);
            var entry = new OverviewPoint(point.X + ax / a * cut / PixelWidth, point.Y + ay / a * cut / PixelHeight);
            var exit = new OverviewPoint(point.X + bx / b * cut / PixelWidth, point.Y + by / b * cut / PixelHeight);
            for (int step = 0; step <= 4; step++)
            {
                double t = step / 4d, s = 1 - t;
                rounded.Add(new(s * s * entry.X + 2 * s * t * point.X + t * t * exit.X,
                    s * s * entry.Y + 2 * s * t * point.Y + t * t * exit.Y));
            }
        }
        if (closed) rounded.Add(rounded[0]);
        return rounded.AsReadOnly();
    }

    public PngImage Territories(IReadOnlyDictionary<int, OverviewColor> colors)
    {
        byte[] pixels = new byte[checked(PixelWidth * PixelHeight * 4)];
        for (int pixel = 0; pixel < _pixelCities.Length; pixel++)
        {
            if (_pixelCities[pixel] is not int city || !colors.TryGetValue(city, out var color)) continue;
            int i = pixel * 4;
            pixels[i] = color.R; pixels[i + 1] = color.G; pixels[i + 2] = color.B; pixels[i + 3] = 143;
        }
        return new(PixelWidth, PixelHeight, 4, pixels);
    }
}
