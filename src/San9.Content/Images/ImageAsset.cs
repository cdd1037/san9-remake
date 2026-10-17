using San9.Content.Packages;

namespace San9.Content.Images;

public sealed record ImageFile(string Path, int Width, int Height, int Channels);

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record ImageAsset(string Id, ImageFile File, int X, int Y, int Width, int Height)
{
    public ImageAsset(string id, string path, int width, int height, int channels = 4)
        : this(id, new(path, width, height, channels), 0, 0, width, height) { }
    [System.Text.Json.Serialization.JsonIgnore] public string Path => File.Path;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || File is null || Width <= 0 || Height <= 0 || X < 0 || Y < 0 || (long)X + Width > File.Width || (long)Y + Height > File.Height ||
            File.Width is <= 0 or > 16384 || File.Height is <= 0 or > 16384 || File.Channels is not (1 or 3 or 4) ||
            string.IsNullOrWhiteSpace(File.Path))
            throw new InvalidDataException($"Invalid image region: {Id}");
    }
}

public static class ImageAssetFiles
{
    public static void ValidateCatalog(IEnumerable<ImageAsset> assets)
    {
        var entries = assets.ToArray();
        foreach (var asset in entries) asset.Validate();
        foreach (var group in entries.GroupBy(a => a.Path))
        {
            if (group.Select(a => a.File).Distinct().Count() != 1) throw new InvalidDataException($"Conflicting physical image metadata: {group.Key}");
            var regions = group.Select(a => (a.X, a.Y, a.Width, a.Height)).Distinct().ToArray();
            for (int i = 0; i < regions.Length; i++) for (int j = i + 1; j < regions.Length; j++)
            {
                var a = regions[i]; var b = regions[j];
                if (a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height)
                    throw new InvalidDataException($"Overlapping image regions: {group.Key}");
            }
        }
    }

    public static async Task VerifyAsync(string root, IEnumerable<ImageAsset> assets, CancellationToken token = default)
    {
        await Parallel.ForEachAsync(assets.DistinctBy(a => a.File), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, (asset, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested();
            _ = LoadFile(root, asset.File);
            return ValueTask.CompletedTask;
        });
    }

    public static Dictionary<string, PngImage> Load(string root, IEnumerable<ImageAsset> assets, CancellationToken token = default)
    {
        var groups = assets.GroupBy(a => a.File).ToArray();
        var output = new System.Collections.Concurrent.ConcurrentDictionary<string, PngImage>();
        Parallel.ForEachAsync(groups, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = token }, (group, cancellation) =>
        {
            var image = LoadFile(root, group.Key);
            foreach (var asset in group)
            {
                token.ThrowIfCancellationRequested();
                if (!output.TryAdd(asset.Id, Region(image, asset))) throw new InvalidDataException($"Duplicate image: {asset.Id}");
            }
            return ValueTask.CompletedTask;
        }).GetAwaiter().GetResult();
        return output.ToDictionary();
    }

    public static PngImage LoadFile(string root, ImageFile file)
    {
        var image = PngImage.Decode(File.ReadAllBytes(PackageFiles.Resolve(root, file.Path)));
        if (image.Width != file.Width || image.Height != file.Height || image.Channels != file.Channels)
            throw new InvalidDataException($"Image format or size mismatch: {file.Path}");
        return image;
    }

    public static PngImage Region(PngImage image, ImageAsset asset)
    {
        asset.Validate();
        if (asset.X == 0 && asset.Y == 0 && asset.Width == image.Width && asset.Height == image.Height) return image;
        byte[] pixels = new byte[checked(asset.Width * asset.Height * image.Channels)];
        for (int y = 0; y < asset.Height; y++)
            Buffer.BlockCopy(image.Pixels, ((asset.Y + y) * image.Width + asset.X) * image.Channels,
                pixels, y * asset.Width * image.Channels, asset.Width * image.Channels);
        return new(asset.Width, asset.Height, image.Channels, pixels);
    }
}
