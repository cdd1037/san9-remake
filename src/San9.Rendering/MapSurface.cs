using San9.Content.Maps;

namespace San9.Rendering;

/// <summary>Immutable cell surfaces and their spatial index for one map generation.</summary>
public sealed class MapSurface
{
    private const int BucketSize = 128;
    private readonly MapCellSurface[] _cells;
    private readonly Dictionary<(int X, int Y), List<int>> _buckets = [];
    private readonly int _canvasWidth, _canvasHeight;
    public MapCellSurface this[int index] => _cells[index];

    public MapSurface(int width, int height, IReadOnlyList<MapSquare> squares)
    {
        if (width <= 0 || height <= 0 || (long)width * height != squares.Count ||
            squares.Any(s => s.Elevation < 0 || s.ElevationMask is < 0 or > 15))
            throw new ArgumentException("Invalid map surface inputs.");
        (_canvasWidth, _canvasHeight) = MapDisplayMetrics.CanvasSize(width, height);
        _cells = new MapCellSurface[squares.Count];
        // Forward ground cells own overlapping projections. Texture composition order has its own inputs.
        foreach (int index in Enumerable.Range(0, squares.Count).OrderBy(i => i % width + i / width).ThenBy(i => i % width))
        {
            var cell = MapCellSurface.Create(index % width, index / width, height, squares[index]);
            _cells[index] = cell;
            for (int y = Bucket(cell.TopY); y <= Bucket(cell.BottomY); y++)
            for (int x = Bucket(cell.LeftX); x <= Bucket(cell.RightX); x++)
            {
                if (!_buckets.TryGetValue((x, y), out var bucket)) _buckets[(x, y)] = bucket = [];
                bucket.Add(index);
            }
        }
    }

    private static int Bucket(double coordinate) => (int)Math.Floor(coordinate / BucketSize);

    public int? PickCell(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= _canvasWidth || y >= _canvasHeight ||
            !_buckets.TryGetValue((Bucket(x), Bucket(y)), out var bucket)) return null;
        for (int i = bucket.Count - 1; i >= 0; i--)
            if (_cells[bucket[i]].Contains(x, y)) return bucket[i];
        return null;
    }
}
