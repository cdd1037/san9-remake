using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Maps;

namespace San9.Content.Packages;

public sealed record CloudPlacement(int Side, int A, int B, int C);

public sealed record MapResources(MapSeason[] Seasons, ImageAsset[] Assets, MapMask[] Masks,
    MapComposition[] Compositions, MapAnimationClip[] Clips, int[] MaskPositions,
    int[] MaskHeightAdjustments, CloudPlacement[] CloudPlacements, byte[] MinimapColors);

/// <summary>Source-version resources. No scenario state or scenario identifier belongs here.</summary>
public sealed record TexturePackage(int SchemaVersion, int ConversionVersion, string SourceBuild, ContentSource[] Sources,
    MapResources Map, InterfaceSkin Skin, TextureLibrary Library, PortraitDefinition[] Portraits)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<ImageAsset> Assets => Map.Assets.Concat(Map.Seasons.SelectMany(s => s.Textures))
        .Concat(Skin.Assets).Concat(Library.Assets).Concat(Portraits.Select(p => p.Image));

    public void Validate()
    {
        if (SchemaVersion != 2 || Map.MinimapColors.Length != 32 || Map.MaskPositions.Length != 288 ||
            Map.MaskHeightAdjustments.Length != 16) throw new InvalidDataException("Invalid texture package.");
        if (Portraits.Select(p => p.Id).Distinct().Count() != Portraits.Length) throw new InvalidDataException("Duplicate portrait ID.");
        foreach (var portrait in Portraits) portrait.Validate();
        Skin.Validate(); Library.Validate(); ImageAssetFiles.ValidateCatalog(Assets);
        MapPackageFiles.ValidateAnimations(new(Map.Clips, []), Map.Assets.ToDictionary(a => a.Id), new HashSet<int>());
    }
}

public static class TexturePackageFiles
{
    public static async Task<TexturePackage> LoadAsync(string root, CancellationToken token = default)
    {
        var package = await PackageFiles.ReadAsync<TexturePackage>(Path.Combine(root, "textures.json"), token);
        package.Validate(); return package;
    }
}
