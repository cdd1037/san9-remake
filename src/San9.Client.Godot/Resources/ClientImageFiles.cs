using System.Collections.Concurrent;
using Godot;
using San9.Content.Images;
using San9.Content.Packages;

namespace San9.Client.Godot;

internal static class ClientImageFiles
{
    public static PngImage LoadFile(string root, ImageFile file, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        using var image = new Image();
        var error = image.Load(PackageFiles.Resolve(root, file.Path));
        if (error != Error.Ok)
            throw new InvalidDataException($"Image load failed ({error}): {file.Path}");
        token.ThrowIfCancellationRequested();
        if (image.GetWidth() != file.Width || image.GetHeight() != file.Height)
            throw new InvalidDataException($"Image size mismatch: {file.Path}");

        // The manifest defines the CPU pixel layout and cache byte budget, not the file encoding.
        image.Convert(file.Channels switch
        {
            1 => Image.Format.L8,
            3 => Image.Format.Rgb8,
            4 => Image.Format.Rgba8,
            _ => throw new InvalidDataException($"Invalid image channels: {file.Path}")
        });
        var pixels = new PngImage(file.Width, file.Height, file.Channels, image.GetData());
        token.ThrowIfCancellationRequested();
        return pixels;
    }

    public static Dictionary<string, PngImage> Load(string root, IEnumerable<ImageAsset> assets, CancellationToken token = default)
    {
        var output = new ConcurrentDictionary<string, PngImage>();
        Parallel.ForEachAsync(assets.GroupBy(a => a.File),
            new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = token }, (group, cancellation) =>
            {
                var image = LoadFile(root, group.Key, cancellation);
                foreach (var asset in group)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!output.TryAdd(asset.Id, ImageAssetFiles.Region(image, asset)))
                        throw new InvalidDataException($"Duplicate image: {asset.Id}");
                }
                return ValueTask.CompletedTask;
            }).GetAwaiter().GetResult();
        return output.ToDictionary();
    }
}
