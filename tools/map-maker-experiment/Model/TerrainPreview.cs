using System.Numerics;
using San9.Content.Images;
using San9.Content.Maps;
using San9.Content.Packages;

namespace San9.TerrainEditor.Model;

public sealed record PreviewStamp(TerrainStamp Stamp, int Cell);
public sealed record PreviewFrame(int Width, int Height, byte[] Pixels, int[] Owners, double Milliseconds);

public static class TerrainCompiler
{
    private static readonly int[] EdgeShapes = [0, 5, 8, 0, 7, 2, 4, 7, 6, 1, 3, 6, 0, 5, 8, 0];
    private static readonly (int X, int Y, int Part)[] Directions =
        [(1, 0, 3), (-1, 0, 2), (0, 1, 4), (0, -1, 1), (-1, -1, 5), (1, -1, 6), (-1, 1, 8), (1, 1, 7)];

    public static (int X, int Y) Center(int x, int y, int height) => (32 * (x + height - y) + 96, 16 * (x + y + 1) + 112);

    public static int[] ElevationMasks(TerrainDocument doc)
    {
        int[] result = new int[doc.Cells.Length];
        int H(int x, int y) => x < 0 || y < 0 || x >= doc.Width || y >= doc.Height ? -1 : doc.Cells[y * doc.Width + x].Height;
        for (int y = 0; y < doc.Height; y++) for (int x = 0; x < doc.Width; x++)
        {
            int h = H(x, y), mask = 0;
            if (Math.Max(Math.Max(H(x, y - 1), H(x - 1, y)), H(x - 1, y - 1)) > h) mask |= 1;
            if (Math.Max(Math.Max(H(x, y - 1), H(x + 1, y)), H(x + 1, y - 1)) > h) mask |= 8;
            if (Math.Max(Math.Max(H(x, y + 1), H(x + 1, y)), H(x + 1, y + 1)) > h) mask |= 2;
            if (Math.Max(Math.Max(H(x, y + 1), H(x - 1, y)), H(x - 1, y + 1)) > h) mask |= 4;
            result[y * doc.Width + x] = mask;
        }
        return result;
    }

    public static int Atlas(TerrainCell cell, int mask)
    {
        TerrainRecipe rule = TerrainRecipe.All[cell.Type];
        int edge = rule.Transitions ? BitOperations.PopCount((uint)mask) switch { 1 or 3 => 4, 2 => mask is 5 or 9 ? 12 : 8, _ => 0 } : 0;
        int variant = cell.Type is 9 or 10 or 15 or 16 ? cell.Variant switch { 0 => 3, 3 => 0, 4 => 1, 5 => 2, _ => cell.Variant } : cell.Variant;
        return rule.Atlas * 16 + cell.Height * 272 + edge + variant;
    }

    public static int Resource(TerrainCell cell, int mask)
    {
        TerrainRecipe r = TerrainRecipe.All[cell.Type];
        int variant = cell.Variant switch { 4 => 2, 5 => 1, _ => cell.Variant };
        return r.Base + (r.Transitions && cell.Height != r.MaxHeight ? EdgeShapes[mask] : 0) +
            variant * (cell.Height == r.MaxHeight ? 1 : 9) + r.Variants * (r.Transitions ? 9 : 1) * (cell.Height - r.MinHeight);
    }

    public static PreviewStamp[] Compile(TerrainDocument doc, MapResources resources)
    {
        doc.Validate();
        int[] masks = ElevationMasks(doc), atlas = doc.Cells.Select((c, i) => Atlas(c, masks[i])).ToArray();
        var groups = new SortedDictionary<long, LinkedList<PreviewStamp>>();
        long minimum = long.MaxValue, maximum = long.MinValue;
        int Key(int x, int y) => x < 0 || y < 0 || x >= doc.Width || y >= doc.Height ? -1 : atlas[y * doc.Width + x];
        void Add(int x, int y, int part, int flags)
        {
            int index = y * doc.Width + x, e = masks[index]; TerrainCell cell = doc.Cells[index];
            var center = Center(x, y, doc.Height);
            int px = center.X + resources.MaskPositions[(e * 9 + part) * 2] - 64;
            int py = center.Y + resources.MaskPositions[(e * 9 + part) * 2 + 1] - resources.MaskHeightAdjustments[e] - 10 * cell.Height - 54;
            int maskId = e != 0 ? 10806 + 9 * e + part : flags == 1 ? 10950 + part : flags == 2 ? 10959 + part : 10806 + part;
            var stamp = new PreviewStamp(new(Resource(cell, e), maskId, px, py, px - 96, py - 112, part == 0), index);
            long order = x + doc.Width * (y + (long)atlas[index] * doc.Height);
            if (!groups.TryGetValue(order, out var list)) groups[order] = list = new();
            // 与共享地貌合成约定保持同键稳定顺序，防止半透明接缝顺序漂移。
            if (minimum == long.MaxValue || order >= (minimum + maximum) / 2) list.AddLast(stamp); else list.AddFirst(stamp);
            minimum = Math.Min(minimum, order); maximum = Math.Max(maximum, order);
        }
        for (int y = 0; y < doc.Height; y++) for (int x = 0; x < doc.Width; x++)
        {
            TerrainCell cell = doc.Cells[y * doc.Width + x];
            foreach (var d in Directions)
            {
                int nx = x + d.X, ny = y + d.Y, neighbor = Key(nx, ny);
                if (neighbor <= Key(x, y) || d.X != 0 && d.Y != 0 && (neighbor <= Key(nx, y) || neighbor <= Key(x, ny))) continue;
                TerrainCell next = doc.Cells[ny * doc.Width + nx];
                int flags = cell.Type is 9 or 10 or 11 or 12 or 15 or 16 ? 1 : next.Height == 1 && next.Type is 0 or 5 ? 2 : 0;
                Add(nx, ny, d.Part, flags);
            }
            Add(x, y, 0, 0);
        }
        return groups.Values.SelectMany(g => g).ToArray();
    }
}

// 仅加载转换包。预览是无建筑/云/动画的地貌层；同一帧的像素和拾取归属一起发布。
public sealed class TerrainPreview
{
    private readonly MapResources resources;
    private readonly Dictionary<string, PngImage> masks;
    private readonly Dictionary<int, MapMask> maskCatalog;
    private readonly Dictionary<int, PngImage> textures;
    public TerrainPreview(string root)
    {
        TexturePackage package = TexturePackageFiles.LoadAsync(root).GetAwaiter().GetResult();
        resources = package.Map;
        maskCatalog = resources.Masks.ToDictionary(m => m.Id);
        var ids = resources.Masks.SelectMany(m => new[] { m.WeightAsset, m.CoverageAsset }).ToHashSet();
        masks = ImageAssetFiles.Load(root, resources.Assets.Where(a => ids.Contains(a.Id)));
        // 三字段实验固定春季；四季属于素材主题，不加入每格工程数据。
        textures = ImageAssetFiles.Load(root, resources.Seasons.Single(s => s.Id == "spring").Textures).ToDictionary(p => int.Parse(p.Key), p => p.Value);
    }
    public PreviewFrame Render(TerrainDocument doc)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        PreviewStamp[] stamps = TerrainCompiler.Compile(doc, resources);
        int width = (doc.Width + doc.Height) * 32 + 256, height = (doc.Width + doc.Height) * 16 + 256;
        byte[] output = new byte[width * height * 4]; int[] owners = Enumerable.Repeat(-1, width * height).ToArray();
        foreach (var draw in stamps)
        {
            TerrainStamp s = draw.Stamp;
            MapMask definition = maskCatalog[s.MaskId];
            PngImage mask = masks[definition.WeightAsset], coverage = masks[definition.CoverageAsset], source = textures[s.ResourceId];
            for (int my = 0; my < mask.Height; my++)
            {
                int wy = s.Y + my; if (wy < 0 || wy >= height) continue;
                int first = 0, last = mask.Width - 1;
                if (s.IsCenter)
                {
                    while (first < mask.Width && coverage.Pixels[my * mask.Width + first] == 0) first++;
                    if (first == mask.Width) continue;
                    while (coverage.Pixels[my * mask.Width + last] == 0) last--;
                }
                int run = s.IsCenter ? first : 0;
                int sx = Mod(s.SourceX + run, source.Width - 64) - run, sy = Mod(s.SourceY + my, source.Height);
                for (int mx = Math.Max(first, -s.X); mx <= Math.Min(last, width - 1 - s.X); mx++)
                {
                    int mi = my * mask.Width + mx;
                    if (!s.IsCenter && coverage.Pixels[mi] == 0) continue;
                    int pixel = wy * width + s.X + mx, to = pixel * 4, from = (sy * source.Width + sx + mx) * 3;
                    int weight = s.IsCenter ? 0 : mask.Pixels[mi];
                    // 合成时使用不透明底色，避免边缘首笔落在透明底上时出现黑边。
                    for (int channel = 0; channel < 3; channel++)
                    {
                        int previous = output[to + 3] == 0 ? source.Pixels[from + channel] : output[to + channel];
                        output[to + channel] = (byte)((source.Pixels[from + channel] * (256 - weight) + previous * weight) >> 8);
                    }
                    output[to + 3] = 255;
                    if (s.IsCenter || owners[pixel] == -1 || weight < 128) owners[pixel] = draw.Cell;
                }
            }
        }
        return new(width, height, output, owners, watch.Elapsed.TotalMilliseconds);
    }
    private static int Mod(int value, int period) => (value % period + period) % period;
}
