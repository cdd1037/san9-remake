namespace San9.Content.Images;

// Catalog entries reference final textures; source indices and palettes belong to the importer only.
public sealed record TextureVariant(int ImageIndex, int PaletteIndex, ImageAsset Image);
public sealed record TextureArchive(string Id, string Category, TextureVariant[] Variants)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<ImageAsset> Assets => Variants.Select(v => v.Image);
}

public sealed record TextureLibrary(int SchemaVersion, TextureArchive[] Archives)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<ImageAsset> Assets => Archives.SelectMany(a => a.Assets);

    public void Validate()
    {
        if (SchemaVersion != 3 || Archives.Select(a => a.Id).Distinct().Count() != Archives.Length)
            throw new InvalidDataException("Invalid texture library.");
        foreach (var archive in Archives)
        {
            if (string.IsNullOrWhiteSpace(archive.Id) || string.IsNullOrWhiteSpace(archive.Category) ||
                archive.Variants.Length == 0 ||
                archive.Variants.Select(v => (v.ImageIndex, v.PaletteIndex)).Distinct().Count() != archive.Variants.Length)
                throw new InvalidDataException($"Invalid texture archive: {archive.Id}");
            foreach (var variant in archive.Variants)
                if (variant.ImageIndex < 0 || variant.PaletteIndex < -1 || variant.Image.File.Channels is not (3 or 4))
                    throw new InvalidDataException($"Invalid texture variant: {archive.Id}");
        }
        ImageAssetFiles.ValidateCatalog(Assets);
    }
}
