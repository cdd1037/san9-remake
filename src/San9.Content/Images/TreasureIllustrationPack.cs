using System.Text.Json.Serialization;
using San9.Content.Packages;

namespace San9.Content.Images;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TreasureIllustration(string Id, string Name, ImageFile Image)
{
    [JsonIgnore] public string Key => "user/treasure-illustrations/" + Id;
}

public sealed record TreasureIllustrationCatalog(ImageAsset[] Assets,
    IReadOnlyDictionary<string, string> ImageSources)
{
    public static TreasureIllustrationCatalog Empty { get; } = new([], new Dictionary<string, string>());
}

/// <summary>User-owned complete images. Keys are independent of treasure identities.</summary>
public sealed record TreasureIllustrationPack(int SchemaVersion, TreasureIllustration[] Images)
{
    public const int CurrentSchemaVersion = 1;
    public static TreasureIllustrationPack Empty { get; } = new(CurrentSchemaVersion, []);

    public void Validate()
    {
        ContentStructure.Validate(this);
        if (SchemaVersion != CurrentSchemaVersion || Images.Select(i => i.Id).Distinct().Count() != Images.Length)
            throw new InvalidDataException("宝物插画目录版本或身份无效。");
        foreach (var entry in Images)
        {
            if (string.IsNullOrEmpty(entry.Id) || entry.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') ||
                string.IsNullOrWhiteSpace(entry.Name) || entry.Image.Channels is not (3 or 4))
                throw new InvalidDataException("宝物插画名称、身份或图片格式无效。");
            _ = PackageFiles.Resolve(".", entry.Image.Path);
            new ImageAsset(entry.Key, entry.Image, 0, 0, entry.Image.Width, entry.Image.Height).Validate();
        }
    }

    public TreasureIllustrationCatalog Resolve(string root)
    {
        Validate();
        var assets = Images.Select(entry => new ImageAsset(entry.Key,
            entry.Image with { Path = "treasure-illustrations/" + entry.Image.Path.Replace('\\', '/') },
            0, 0, entry.Image.Width, entry.Image.Height)).ToArray();
        var sources = Images.Select((entry, index) => KeyValuePair.Create(assets[index].Path, PackageFiles.Resolve(root, entry.Image.Path)))
            .Distinct().ToDictionary();
        ImageAssetFiles.ValidateCatalog(assets);
        return new(assets, sources);
    }
}

public static class TreasureIllustrationPackFiles
{
    public static async Task<TreasureIllustrationPack> LoadAsync(string root, CancellationToken token = default)
    {
        string path = Path.Combine(root, "illustrations.json");
        var pack = File.Exists(path) ? await PackageFiles.ReadAsync<TreasureIllustrationPack>(path, token) : TreasureIllustrationPack.Empty;
        pack.Validate();
        foreach (var image in pack.Images.Select(i => i.Image).Distinct())
            if (!File.Exists(PackageFiles.Resolve(root, image.Path))) throw new InvalidDataException($"找不到宝物插画文件 {image.Path}。");
        return pack;
    }
}
