using San9.Content.Images;
using San9.Content.Packages;

namespace San9.Importer.San9PkTc101.Images;

public static class TextureAtlasCompiler
{
    public const int PageSize = 2048;
    public const int Padding = 2;
    public sealed record Source(ImageAsset Asset, string? Group, string? Category = null, int? FileId = null);
    private sealed record Item(Source Source, string Id, string? Group, string Category)
    {
        public int Width => Source.Asset.Width;
        public int Height => Source.Asset.Height;
        public int Channels => Source.Asset.File.Channels;
    }

    private readonly record struct Rect(int X, int Y, int W, int H)
    {
        public int Right => X + W;
        public int Bottom => Y + H;
        public bool Contains(Rect r) => X <= r.X && Y <= r.Y && Right >= r.Right && Bottom >= r.Bottom;
    }

    public static ImageAsset[] Compile(string root, IEnumerable<Source> sources, string directory, CancellationToken token = default,
        Action<string, byte[]>? write = null, Func<ImageAsset, PngImage>? decode = null)
    {
        var input = sources.ToArray();
        if (input.Any(s => s.FileId is < 0 || s.FileId is not null && s.Group is not null) ||
            input.Where(s => s.FileId is not null).GroupBy(s => (s.Category, s.FileId)).Any(g => g.Count() > 1))
            throw new InvalidDataException("Numbered images require unique nonnegative IDs within a standalone category.");
        var items = input.Select(s => new Item(s, s.Asset.Id, s.Group,
            s.Category ?? s.Group ?? "standalone")).OrderBy(i => i.Id, StringComparer.Ordinal).ToArray();
        var unread = input.GroupBy(s => s.Asset.File).ToDictionary(g => g.Key, g => g.Count());
        var sourcePaths = unread.Keys.ToDictionary(file => PackageFiles.Resolve(root, file.Path),
            StringComparer.OrdinalIgnoreCase);
        PngImage Read(Item item, Dictionary<ImageFile, PngImage> files)
        {
            token.ThrowIfCancellationRequested();
            var asset = item.Source.Asset;
            PngImage image;
            if (decode is not null && asset.Path.StartsWith(".sources/", StringComparison.Ordinal)) image = decode(asset);
            else
            {
                if (!files.TryGetValue(asset.File, out var file))
                    files[asset.File] = file = ImageAssetFiles.LoadFile(root, asset.File);
                image = ImageAssetFiles.Region(file, asset);
                if (ReferenceEquals(image.Pixels, file.Pixels)) image = image with { Pixels = image.Pixels.ToArray() };
            }
            if (image.Width != item.Width || image.Height != item.Height || image.Channels != item.Channels)
                throw new InvalidDataException($"Texture source dimensions differ: {item.Id}.");
            unread[asset.File]--;
            // Extend colors at the final logical-image boundary, after cropping/compositing.
            if (image.Channels == 4) IndexedImageArchive.ExtendTransparentColors(image.Pixels, image.Width, image.Height);
            return image;
        }
        Dictionary<(string Category, string Name), int> numbers = [];
        Dictionary<string, ImageAsset> result = [];
        ImageFile Save(string category, string name, PngImage image, int? fileId = null)
        {
            token.ThrowIfCancellationRequested();
            var bytes = image.Encode();
            var key = (category, name);
            int number = numbers.GetValueOrDefault(key); numbers[key] = number + 1;
            string filename = fileId is int id ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{name}-{number}";
            string path = $"{directory}/{category}/{filename}.png";
            string target = PackageFiles.Resolve(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (sourcePaths.TryGetValue(target, out var original) && unread[original] > 0)
                throw new InvalidOperationException("Texture output would overwrite an unread source; use an independent output directory.");
            (write ?? File.WriteAllBytes)(target, bytes);
            return new(path, image.Width, image.Height, image.Channels);
        }
        void Register(Item item, ImageFile file, int x, int y)
        {
            result.Add(item.Id, new(item.Id, file, x, y, item.Width, item.Height));
        }
        bool Standalone(Item i) => i.Group is null || i.Width + 2 * Padding > PageSize || i.Height + 2 * Padding > PageSize;
        foreach (var item in items.Where(Standalone))
        {
            Register(item, Save(item.Category, "image", Read(item, []), item.Source.FileId), 0, 0);
        }
        foreach (var group in items.Where(i => !Standalone(i)).GroupBy(i => (i.Group, i.Category, i.Channels)).OrderBy(g => g.Key.Group, StringComparer.Ordinal).ThenBy(g => g.Key.Category, StringComparer.Ordinal).ThenBy(g => g.Key.Channels))
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
                Dictionary<ImageFile, PngImage> sourceFiles = [];
                foreach (var entry in page)
                {
                    var source = Read(entry.Item, sourceFiles);
                    for (int dy = -Padding; dy < source.Height + Padding; dy++)
                        for (int dx = -Padding; dx < source.Width + Padding; dx++)
                        {
                            int from = (Math.Clamp(dy, 0, source.Height - 1) * source.Width + Math.Clamp(dx, 0, source.Width - 1)) * channels;
                            int to = ((entry.Y + dy) * width + entry.X + dx) * channels;
                            source.Pixels.AsSpan(from, channels).CopyTo(image.Pixels.AsSpan(to, channels));
                        }
                }
                var file = Save(group.Key.Category, "atlas", image);
                foreach (var entry in page) Register(entry.Item, file, entry.X, entry.Y);
                page.Clear(); free = [new(0, 0, PageSize, PageSize)]; width = height = 0;
            }
            var remaining = group.OrderByDescending(i => (long)i.Width * i.Height)
                .ThenByDescending(i => Math.Max(i.Width, i.Height)).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
            while (remaining.Count != 0)
            {
                token.ThrowIfCancellationRequested();
                (int Index, long Area, int Side, int Short, int Long, int Y, int X) best = (int.MaxValue, long.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, 0, 0);
                Rect? placement = null;
                for (int index = 0; index < remaining.Count; index++)
                {
                    var item = remaining[index];
                    int w = item.Width + 2 * Padding, h = item.Height + 2 * Padding;
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
