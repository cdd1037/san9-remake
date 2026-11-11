using System.Collections.Immutable;
using System.Text.Json;
using San9.Content.Images;
using San9.Content.Packages;

namespace San9.Application.Authoring;

/// <summary>Imported images have independent keys and publish with the shared treasure draft.</summary>
public sealed class TreasureIllustrationWorkspace
{
    private TreasureIllustrationPack _saved;
    private byte[]? _expected;
    private ImmutableDictionary<string, byte[]> _pending = ImmutableDictionary<string, byte[]>.Empty;
    public string Root { get; }
    public string ManifestPath => Path.Combine(Root, "illustrations.json");
    public TreasureIllustrationPack Pack { get; private set; }
    public bool HasChanges => !Pack.Images.SequenceEqual(_saved.Images);
    public TreasureIllustrationCatalog Catalog => Pack.Resolve(Root);

    private TreasureIllustrationWorkspace(string root, TreasureIllustrationPack pack, byte[]? expected)
    { Root = root; Pack = _saved = pack; _expected = expected; }

    public static async Task<TreasureIllustrationWorkspace> OpenAsync(string root, CancellationToken token = default)
    {
        root = Path.GetFullPath(root);
        byte[]? expected = await PlayerDefinitionFiles.ReadOptionalAsync(Path.Combine(root, "illustrations.json"), token);
        var pack = expected is null ? TreasureIllustrationPack.Empty
            : JsonSerializer.Deserialize<TreasureIllustrationPack>(expected, PackageFiles.JsonOptions)
                ?? throw new InvalidDataException("宝物插画目录为空。");
        pack.Validate();
        foreach (var image in pack.Images.Select(i => i.Image).Distinct())
            if (!File.Exists(PackageFiles.Resolve(root, image.Path))) throw new InvalidDataException($"找不到插画 {image.Path}。");
        return new(root, pack, expected);
    }

    public TreasureIllustration Add(string name, PngImage image)
    {
        string id = Guid.NewGuid().ToString("N");
        var file = new ImageFile($"images/{id}.png", image.Width, image.Height, image.Channels);
        var entry = new TreasureIllustration(id, name.Trim(), file);
        var next = Pack with { Images = [.. Pack.Images, entry] };
        next.Validate();
        byte[] bytes = image.Encode();
        _pending = _pending.Add(file.Path, bytes); Pack = next;
        return entry;
    }

    public PngImage ReadImage(string key) => ReadImage(Pack.Images.Single(i => i.Key == key).Image);

    private PngImage ReadImage(ImageFile image)
    {
        var decoded = PngImage.Decode(_pending.TryGetValue(image.Path, out var bytes) ? bytes
            : File.ReadAllBytes(PackageFiles.Resolve(Root, image.Path)));
        if (decoded.Width != image.Width || decoded.Height != image.Height || decoded.Channels != image.Channels)
            throw new InvalidDataException("宝物插画尺寸或像素格式与目录不符。");
        return decoded;
    }

    internal ScenarioDataFiles.PlayerDocument Document => new(ManifestPath, Pack, _expected);

    internal void PrepareImages(List<string> created, CancellationToken token)
    {
        foreach (var file in Pack.Images.Select(i => i.Image).Distinct())
        {
            token.ThrowIfCancellationRequested();
            string path = PackageFiles.Resolve(Root, file.Path);
            if (_pending.TryGetValue(file.Path, out var bytes))
            {
                if (File.Exists(path))
                {
                    if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                        throw new InvalidDataException("宝物插画文件已被其他操作修改。");
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    created.Add(path); output.Write(bytes); output.Flush(true);
                }
            }
            _ = ReadImage(file);
        }
    }

    internal void Saved(byte[] expected)
    { _expected = expected; _saved = Pack; _pending = _pending.Clear(); }
}
