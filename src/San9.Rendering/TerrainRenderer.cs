using San9.Content.Packages;
using San9.Content.Images;
using San9.Content.Maps;

namespace San9.Rendering;

public sealed class TerrainRenderer
{
    public const int ChunkSize = 512;
    private readonly MapPackage _map;
    private readonly Dictionary<string, PngImage> _common;
    private readonly Dictionary<int, PngImage> _land;
    private readonly Dictionary<int, MapMask> _masks;
    private readonly Dictionary<(int, int), List<TerrainStamp>> _buckets = [];

    public TerrainRenderer(MapPackage map, Dictionary<string, PngImage> common, Dictionary<int, PngImage> land)
    {
        _map = map; _common = common; _land = land; _masks = map.Masks.ToDictionary(m => m.Id);
        foreach (var mask in _masks.Values)
        {
            var weight = common[mask.WeightAsset]; var coverage = common[mask.CoverageAsset];
            if (weight.Channels != 1 || coverage.Channels != 1 || weight.Width != coverage.Width || weight.Height != coverage.Height || weight.Width > 64)
                throw new InvalidDataException("Invalid mask channels or dimensions.");
        }
        if (land.Values.Any(i => i.Width <= 64 || i.Channels != 3)) throw new InvalidDataException("Invalid terrain image channels or sampling width.");
        foreach (var stamp in map.Terrain)
        {
            var mask = common[_masks[stamp.MaskId].WeightAsset];
            int x0 = Math.Max(0, stamp.X) / ChunkSize, y0 = Math.Max(0, stamp.Y) / ChunkSize;
            int x1 = Math.Min(map.CanvasWidth - 1, stamp.X + mask.Width - 1) / ChunkSize;
            int y1 = Math.Min(map.CanvasHeight - 1, stamp.Y + mask.Height - 1) / ChunkSize;
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                if (!_buckets.TryGetValue((x, y), out var list)) _buckets[(x, y)] = list = [];
                list.Add(stamp);
            }
        }
    }

    public long SourceBytes => _common.Values.Concat(_land.Values).Sum(p => (long)p.Pixels.Length);

    public PngImage RenderChunk(int column, int row, CancellationToken token = default)
    {
        int x0 = column * ChunkSize, y0 = row * ChunkSize;
        if (x0 < 0 || y0 < 0 || x0 >= _map.CanvasWidth || y0 >= _map.CanvasHeight) throw new ArgumentOutOfRangeException(nameof(column));
        int width = Math.Min(ChunkSize, _map.CanvasWidth - x0), height = Math.Min(ChunkSize, _map.CanvasHeight - y0);
        byte[] output = new byte[width * height * 3];
        if (_buckets.TryGetValue((column, row), out var stamps)) foreach (var stamp in stamps)
        {
            token.ThrowIfCancellationRequested();
            var maskInfo = _masks[stamp.MaskId];
            var mask = _common[maskInfo.WeightAsset]; var coverage = _common[maskInfo.CoverageAsset]; var source = _land[stamp.ResourceId];
            for (int my = Math.Max(0, y0 - stamp.Y); my < Math.Min(mask.Height, y0 + height - stamp.Y); my++)
            {
                int first = 0, last = mask.Width - 1;
                if (stamp.IsCenter)
                {
                    while (first < mask.Width && coverage.Pixels[my * mask.Width + first] == 0) first++;
                    if (first == mask.Width) continue;
                    while (coverage.Pixels[my * mask.Width + last] == 0) last--;
                }
                int run = stamp.IsCenter ? first : 0;
                int sx = MapGeometry.Mod(stamp.SourceX + run, source.Width - 64) - run;
                int sy = MapGeometry.Mod(stamp.SourceY + my, source.Height);
                int start = Math.Max(first, x0 - stamp.X), end = Math.Min(last, x0 + width - 1 - stamp.X);
                if (start > end) continue;
                if (stamp.IsCenter)
                {
                    Buffer.BlockCopy(source.Pixels, (sy * source.Width + sx + start) * 3,
                        output, ((stamp.Y + my - y0) * width + stamp.X + start - x0) * 3, (end - start + 1) * 3);
                    continue;
                }
                for (int mx = start; mx <= end; mx++)
                {
                    int mi = my * mask.Width + mx;
                    if (coverage.Pixels[mi] == 0) continue;
                    int from = (sy * source.Width + sx + mx) * source.Channels;
                    int to = ((stamp.Y + my - y0) * width + stamp.X + mx - x0) * 3;
                    int weight = mask.Pixels[mi];
                    int inverse = 256 - weight;
                    output[to] = (byte)((source.Pixels[from] * inverse + output[to] * weight) >> 8);
                    output[to + 1] = (byte)((source.Pixels[from + 1] * inverse + output[to + 1] * weight) >> 8);
                    output[to + 2] = (byte)((source.Pixels[from + 2] * inverse + output[to + 2] * weight) >> 8);
                }
            }
        }
        var fill = _common["cloud-fill"];
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested(); int wy = y0 + y;
            for (int x = 0; x < width; x++)
            {
                int wx = x0 + x;
                if (_map.LeftEdge[wy] is int left && _map.RightEdge[wy] is int right && wx >= left && wx < right) continue;
                int from = (MapGeometry.Mod(wy - 112, fill.Height) * fill.Width + MapGeometry.Mod(wx - 96, fill.Width)) * fill.Channels;
                int to = (y * width + x) * 3;
                output[to] = fill.Pixels[from]; output[to + 1] = fill.Pixels[from + 1]; output[to + 2] = fill.Pixels[from + 2];
            }
        }
        return new(width, height, 3, output);
    }
}
