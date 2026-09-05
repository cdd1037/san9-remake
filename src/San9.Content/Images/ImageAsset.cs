using System.Security.Cryptography;
using San9.Content.Packages;

namespace San9.Content.Images;

public sealed record ImageAsset(string Id, string Path, string Sha256, int Width, int Height);

public static class ImageAssetFiles
{
    public static async Task VerifyAsync(string root, IEnumerable<ImageAsset> assets, CancellationToken token = default)
    {
        await Parallel.ForEachAsync(assets, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (asset, cancellation) =>
        {
            await using var file = File.OpenRead(PackageFiles.Resolve(root, asset.Path));
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellation));
            if (!hash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Image checksum mismatch: {asset.Id}");
        });
    }

    public static Dictionary<string, PngImage> Load(string root, IEnumerable<ImageAsset> assets, CancellationToken token = default)
    {
        var entries = assets.ToArray();
        var images = new PngImage[entries.Length];
        Parallel.ForEachAsync(Enumerable.Range(0, entries.Length),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount), CancellationToken = token }, (index, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested();
            var asset = entries[index];
            byte[] bytes = File.ReadAllBytes(PackageFiles.Resolve(root, asset.Path));
            var image = PngImage.Decode(bytes);
            if (image.Width != asset.Width || image.Height != asset.Height) throw new InvalidDataException($"Image size mismatch: {asset.Id}");
            images[index] = image;
            return ValueTask.CompletedTask;
        }).GetAwaiter().GetResult();
        return entries.Select((asset, index) => (asset.Id, Image: images[index])).ToDictionary(p => p.Id, p => p.Image);
    }
}
