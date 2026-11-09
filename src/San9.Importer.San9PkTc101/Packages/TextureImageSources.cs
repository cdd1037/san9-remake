using San9.Content.Images;
using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101.Packages;

/// <summary>One conversion's source identities and pixel recipes. No intermediate images are written.</summary>
internal sealed class TextureImageSources(CancellationToken token)
{
    private readonly Dictionary<string, ImageAsset> identities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextureSourceImage> pending = new(StringComparer.Ordinal);
    internal int Count => identities.Count;

    internal ImageAsset Add(string id, string sourcePath, TextureSourceImage image)
    {
        token.ThrowIfCancellationRequested();
        if (identities.TryGetValue(sourcePath, out var existing)) return existing with { Id = id };
        var asset = new ImageAsset(id, $".sources/{identities.Count}", image.Width, image.Height, image.Channels);
        asset.Validate();
        identities.Add(sourcePath, asset);
        pending.Add(asset.Path, image);
        return asset;
    }

    internal PngImage Load(ImageAsset asset)
    {
        token.ThrowIfCancellationRequested();
        if (pending.Remove(asset.Path, out var source))
        {
            var image = source.Decode();
            if (image.Width != asset.Width || image.Height != asset.Height || image.Channels != asset.File.Channels ||
                image.Pixels.Length != checked(image.Width * image.Height * image.Channels))
                throw new InvalidDataException($"Texture recipe dimensions differ: {asset.Id}.");
            return image;
        }
        if (asset.Path.StartsWith(".sources/", StringComparison.Ordinal))
            throw new InvalidOperationException($"Texture source already consumed or absent: {asset.Id}.");
        throw new InvalidOperationException($"Texture recipe absent: {asset.Id}.");
    }
}
