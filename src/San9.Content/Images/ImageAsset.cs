using System.Security.Cryptography;
using San9.Content.Packages;

namespace San9.Content.Images;

public sealed record ImageAsset(string Id, string Path, string Sha256, int Width, int Height);

public static class ImageAssetFiles
{
    public static async Task VerifyAsync(string root, IEnumerable<ImageAsset> assets, CancellationToken token = default)
    {
        foreach (var asset in assets)
        {
            token.ThrowIfCancellationRequested();
            await using var file = File.OpenRead(PackageFiles.Resolve(root, asset.Path));
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
            if (!hash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Image checksum mismatch: {asset.Id}");
        }
    }

    public static Dictionary<string, PngImage> Load(string root, IEnumerable<ImageAsset> assets, CancellationToken token = default)
    {
        Dictionary<string, PngImage> result = [];
        foreach (var asset in assets)
        {
            token.ThrowIfCancellationRequested();
            byte[] bytes = File.ReadAllBytes(PackageFiles.Resolve(root, asset.Path));
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Image checksum mismatch: {asset.Id}");
            var image = PngImage.Decode(bytes);
            if (image.Width != asset.Width || image.Height != asset.Height) throw new InvalidDataException($"Image size mismatch: {asset.Id}");
            result.Add(asset.Id, image);
        }
        return result;
    }
}
