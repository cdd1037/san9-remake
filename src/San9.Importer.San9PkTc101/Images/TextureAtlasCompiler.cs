using System.Security.Cryptography;
using San9.Content.Images;
using San9.Content.Packages;

namespace San9.Importer.San9PkTc101.Images;

public static class TextureAtlasCompiler
{
    public const int PageSize = 2048;
    public const int Padding = 2;
    public const int PackingVersion = 3;
    public sealed record Source(ImageAsset Asset, string? Group, string? Category = null, int? FileId = null);
    private sealed record Item(PngImage Image, Source[] Sources, string Hash, string? Group, string Category);

    private readonly record struct Rect(int X, int Y, int W, int H)
    {
        public int Right => X + W;
        public int Bottom => Y + H;
        public bool Contains(Rect r) => X <= r.X && Y <= r.Y && Right >= r.Right && Bottom >= r.Bottom;
    }

    public static ImageAsset[] Compile(string root, IEnumerable<Source> sources, string directory, CancellationToken token = default)
    {
        var input = sources.ToArray();
        if (input.Any(s => s.FileId is < 0 || s.FileId is not null && s.Group is not null) ||
            input.Where(s => s.FileId is not null).GroupBy(s => (s.Category, s.FileId)).Any(g => g.Count() > 1))
            throw new InvalidDataException("Numbered images require unique nonnegative IDs within a standalone category.");
        var pixels = ImageAssetFiles.Load(root, input.Select(s => s.Asset), token);
        // The final logical image is the boundary for alpha-border preparation. This also covers
        // derived/composited images, before deduplication and before neighboring atlas regions exist.
        foreach (var image in pixels.Values)
            if (image.Channels == 4) IndexedImageArchive.ExtendTransparentColors(image.Pixels, image.Width, image.Height);
        var items = input.GroupBy(s =>
        {
            var p = pixels[s.Asset.Id];
            if (p.Channels == 3)
            {
                byte[] rgba = new byte[p.Width * p.Height * 4];
                for (int i = 0; i < p.Width * p.Height; i++)
                { p.Pixels.AsSpan(i * 3, 3).CopyTo(rgba.AsSpan(i * 4, 3)); rgba[i * 4 + 3] = 255; }
                return $"{p.Width}x{p.Height}x4-" + Convert.ToHexString(SHA256.HashData(rgba));
            }
            return $"{p.Width}x{p.Height}x{p.Channels}-" + Convert.ToHexString(SHA256.HashData(p.Pixels));
        }).Select(g =>
        {
            var entries = g.OrderBy(s => pixels[s.Asset.Id].Channels).ThenBy(s => s.Asset.Id, StringComparer.Ordinal).ToArray();
            var groups = entries.Select(s => s.Group).Distinct().ToArray();
            var categories = entries.Select(s => s.Category ?? s.Group ?? "standalone").Distinct().ToArray();
            return new Item(pixels[entries[0].Asset.Id], entries, g.Key, groups.Length == 1 ? groups[0] : null,
                categories.Length == 1 ? categories[0] : "shared");
        }).OrderBy(i => i.Hash, StringComparer.Ordinal).ToArray();
        Dictionary<string, ImageAsset> result = [];
        ImageFile Save(string category, string name, PngImage image, int? fileId = null)
        {
            token.ThrowIfCancellationRequested();
            var bytes = image.Encode();
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            string filename = fileId is int id ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{name}-{hash[..16]}";
            string path = $"{directory}/{category}/{filename}.png";
            string target = PackageFiles.Resolve(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) File.WriteAllBytes(target, bytes);
            return new(path, hash, image.Width, image.Height, image.Channels);
        }
        void Register(Item item, ImageFile file, int x, int y)
        {
            foreach (var source in item.Sources) result.Add(source.Asset.Id, new(source.Asset.Id, file, x, y, item.Image.Width, item.Image.Height));
        }
        bool Standalone(Item i) => i.Group is null || i.Image.Width + 2 * Padding > PageSize || i.Image.Height + 2 * Padding > PageSize;
        foreach (var item in items.Where(Standalone))
        {
            // Equal images within one numbered category share the lowest stable resource ID.
            int? fileId = item.Category != "shared" ? item.Sources.Where(s => s.FileId is not null).Select(s => s.FileId).Min() : null;
            Register(item, Save(item.Category, "image", item.Image, fileId), 0, 0);
        }
        foreach (var group in items.Where(i => !Standalone(i)).GroupBy(i => (i.Group, i.Category, i.Image.Channels)).OrderBy(g => g.Key.Group, StringComparer.Ordinal).ThenBy(g => g.Key.Category, StringComparer.Ordinal).ThenBy(g => g.Key.Channels))
        {
            // Maximal free rectangles fill gaps left by differently sized sheets. No rotation.
            var page = new List<(Item Item, int X, int Y)>();
            List<Rect> free = [new(0, 0, PageSize, PageSize)];
            int width = 0, height = 0;
            void Flush()
            {
                if (page.Count == 0) return;
                int channels = group.Key.Channels;
                var image = new PngImage(width, height, channels, new byte[checked(width * height * channels)]);
                foreach (var entry in page)
                    for (int dy = -Padding; dy < entry.Item.Image.Height + Padding; dy++)
                        for (int dx = -Padding; dx < entry.Item.Image.Width + Padding; dx++)
                        {
                            var source = entry.Item.Image;
                            int from = (Math.Clamp(dy, 0, source.Height - 1) * source.Width + Math.Clamp(dx, 0, source.Width - 1)) * channels;
                            int to = ((entry.Y + dy) * width + entry.X + dx) * channels;
                            source.Pixels.AsSpan(from, channels).CopyTo(image.Pixels.AsSpan(to, channels));
                        }
                var file = Save(group.Key.Category, "atlas", image);
                foreach (var entry in page) Register(entry.Item, file, entry.X, entry.Y);
                page.Clear(); free = [new(0, 0, PageSize, PageSize)]; width = height = 0;
            }
            var remaining = group.OrderByDescending(i => (long)i.Image.Width * i.Image.Height)
                .ThenByDescending(i => Math.Max(i.Image.Width, i.Image.Height)).ThenBy(i => i.Hash, StringComparer.Ordinal).ToList();
            while (remaining.Count != 0)
            {
                token.ThrowIfCancellationRequested();
                (int Index, long Area, int Side, int Short, int Long, int Y, int X) best = (int.MaxValue, long.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, 0, 0);
                Rect? placement = null;
                for (int index = 0; index < remaining.Count; index++)
                {
                    var item = remaining[index];
                    int w = item.Image.Width + 2 * Padding, h = item.Image.Height + 2 * Padding;
                    foreach (var rect in free)
                    {
                        if (w > rect.W || h > rect.H) continue;
                        var score = (index, (long)Math.Max(width, rect.X + w) * Math.Max(height, rect.Y + h),
                            Math.Max(Math.Max(width, rect.X + w), Math.Max(height, rect.Y + h)),
                            Math.Min(rect.W - w, rect.H - h), Math.Max(rect.W - w, rect.H - h), rect.Y, rect.X);
                        if (score.CompareTo(best) >= 0) continue;
                        best = score; placement = new(rect.X, rect.Y, w, h);
                    }
                    // Item order is the primary score; later items cannot improve a fitting item's placement.
                    if (placement is not null) break;
                }
                if (placement is not { } used) { Flush(); continue; }
                var selected = remaining[best.Index]; remaining.RemoveAt(best.Index);
                page.Add((selected, used.X + Padding, used.Y + Padding));
                width = Math.Max(width, used.Right); height = Math.Max(height, used.Bottom);
                List<Rect> split = [];
                foreach (var r in free)
                {
                    if (used.X >= r.Right || used.Right <= r.X || used.Y >= r.Bottom || used.Bottom <= r.Y) { split.Add(r); continue; }
                    if (used.X > r.X) split.Add(new(r.X, r.Y, used.X - r.X, r.H));
                    if (used.Right < r.Right) split.Add(new(used.Right, r.Y, r.Right - used.Right, r.H));
                    if (used.Y > r.Y) split.Add(new(r.X, r.Y, r.W, used.Y - r.Y));
                    if (used.Bottom < r.Bottom) split.Add(new(r.X, used.Bottom, r.W, r.Bottom - used.Bottom));
                }
                free = split.Distinct().Where(r => !split.Any(other => other != r && other.Contains(r))).ToList();
            }
            Flush();
        }
        return input.Select(s => result[s.Asset.Id]).ToArray();
    }
}
