using San9.Content.Packages;
using San9.Content.Images;
using System.Security.Cryptography;
using System.Text.Json;

namespace San9.Content.Maps;

public sealed record MapSquare(int Terrain, int Elevation, int ElevationMask, int RegionId)
{
    [System.Text.Json.Serialization.JsonRequired]
    public bool HideRegionName { get; init; }
}
public sealed record MapSeason(string Id, ImageAsset[] Textures);
public sealed record MapMask(int Id, string WeightAsset, string CoverageAsset);
public sealed record TerrainStamp(int ResourceId, int MaskId, int X, int Y, int SourceX, int SourceY, bool IsCenter);
public sealed record MapSprite(string AssetId, int X, int Y);
public sealed record MapBuilding(int Id, int X, int Y, int FixedType, int CompositionId, MapSprite[] Sprites);
public sealed record MapComposition(int Id, MapCompositionPart[] Parts);
public sealed record MapCompositionPart(bool PaletteAlpha, bool DevelopmentVariant, int ResourceId, int X, int Y);
public sealed record MapAnimationClip(string Id, string[] Frames, int FrameMilliseconds, Dictionary<string, string[]> SeasonFrames);
public sealed record MapAnimatedSprite(string ClipId, int X, int Y, int Phase, int BuildingId);
public sealed record MapAnimations(MapAnimationClip[] Clips, MapAnimatedSprite[] Water, MapAnimatedSprite[] Buildings);
public sealed record MapPackage(
    int SchemaVersion, string SourceBuild, string ScenarioId, string InitialSeason,
    int Width, int Height, int CanvasWidth, int CanvasHeight,
    MapSquare[] Squares, MapSeason[] Seasons, ImageAsset[] Assets, MapMask[] Masks,
    TerrainStamp[] Terrain, MapSprite[] Bridges, MapBuilding[] Buildings,
    MapComposition[] Compositions, MapSprite[] Clouds, int?[] LeftEdge, int?[] RightEdge,
    ContentSource[] Sources, MapAnimations Animations, MapSprite[] Boundaries);

public static class MapPackageFiles
{
    public static async Task<MapPackage> LoadAsync(string root, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(Path.Combine(root, "map.json"));
        var map = await JsonSerializer.DeserializeAsync<MapPackage>(file, PackageFiles.JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Empty map package.");
        Validate(map);
        foreach (var asset in map.Assets.Concat(map.Seasons.SelectMany(s => s.Textures)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var data = File.OpenRead(PackageFiles.Resolve(root, asset.Path));
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(data, cancellationToken));
            if (!hash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Asset checksum mismatch: {asset.Id}");
        }
        return map;
    }

    public static void Validate(MapPackage map)
    {
        if (map.SchemaVersion != 7 || map.Width != 200 || map.Height != 200 || map.Squares.Length != 40000 ||
            map.CanvasWidth != 13056 || map.CanvasHeight != 6656) throw new InvalidDataException("Unsupported map dimensions or schema.");
        if (!map.Seasons.Select(s => s.Id).Order().SequenceEqual(new[] { "autumn", "spring", "summer", "winter" }) ||
            !map.Seasons.Any(s => s.Id == map.InitialSeason)) throw new InvalidDataException("Invalid seasons.");
        var common = map.Assets.ToDictionary(a => a.Id);
        var terrainIds = map.Seasons[0].Textures.Select(a => a.Id).Order().ToArray();
        if (terrainIds.Length != 771) throw new InvalidDataException("Expected 771 terrain textures per season.");
        foreach (var season in map.Seasons)
        {
            if (!season.Textures.Select(a => a.Id).Order().SequenceEqual(terrainIds) || season.Textures.Select(a => a.Id).Distinct().Count() != season.Textures.Length)
                throw new InvalidDataException("Season texture mappings differ.");
        }
        foreach (var asset in map.Assets.Concat(map.Seasons.SelectMany(s => s.Textures)))
            if (asset.Width is <= 0 or > 4096 || asset.Height is <= 0 or > 4096 || asset.Sha256.Length != 64)
                throw new InvalidDataException("Invalid image metadata.");
        var masks = map.Masks.ToDictionary(m => m.Id);
        foreach (var mask in masks.Values)
            if (!common.ContainsKey(mask.WeightAsset) || !common.ContainsKey(mask.CoverageAsset)) throw new InvalidDataException("Missing mask image.");
        var terrainSet = terrainIds.ToHashSet();
        foreach (var stamp in map.Terrain)
            if (!terrainSet.Contains(stamp.ResourceId.ToString(System.Globalization.CultureInfo.InvariantCulture)) || !masks.ContainsKey(stamp.MaskId))
                throw new InvalidDataException("Missing terrain or mask reference.");
        foreach (var sprite in map.Boundaries.Concat(map.Bridges).Concat(map.Buildings.SelectMany(b => b.Sprites)).Concat(map.Clouds))
            if (!common.ContainsKey(sprite.AssetId)) throw new InvalidDataException("Missing sprite reference.");
        if (!common.TryGetValue("minimap-terrain", out var minimap) || minimap.Width != 320 || minimap.Height != 160)
            throw new InvalidDataException("Invalid minimap terrain image.");
        if (!common.ContainsKey("cloud-fill") || map.LeftEdge.Length != map.CanvasHeight || map.RightEdge.Length != map.CanvasHeight)
            throw new InvalidDataException("Invalid cloud boundaries.");
        foreach (var square in map.Squares)
            if (square.Terrain is < 0 or > 16 || square.Elevation is < 0 or > 7 || square.ElevationMask is < 0 or > 15)
                throw new InvalidDataException("Invalid square.");
        foreach (var building in map.Buildings)
            if (building.X is < 0 or >= 200 || building.Y is < 0 or >= 200 || building.Sprites.Length == 0)
                throw new InvalidDataException("Invalid building.");
        ValidateAnimations(map.Animations, common, map.Buildings.Select(b => b.Id).ToHashSet());
    }

    public static void ValidateAnimations(MapAnimations animations, IReadOnlyDictionary<string, ImageAsset> assets, ISet<int> buildings)
    {
        var clips = animations.Clips.ToDictionary(c => c.Id);
        foreach (var clip in clips.Values)
        {
            if (clip.Frames.Length == 0 || clip.FrameMilliseconds <= 0 || clip.Frames.Any(f => !assets.ContainsKey(f)))
                throw new InvalidDataException("Invalid animation clip.");
            if (clip.SeasonFrames.Count != 0 && !clip.SeasonFrames.Keys.Order().SequenceEqual(new[] { "autumn", "spring", "summer", "winter" }))
                throw new InvalidDataException("Seasonal animation must define all four seasons.");
            if (clip.SeasonFrames.Values.Any(frames => frames.Length != clip.Frames.Length || frames.Any(f => !assets.ContainsKey(f))))
                throw new InvalidDataException("Invalid seasonal animation frames.");
            var first = assets[clip.Frames[0]];
            if (clip.Frames.Concat(clip.SeasonFrames.Values.SelectMany(f => f)).Any(f => assets[f].Width != first.Width || assets[f].Height != first.Height))
                throw new InvalidDataException("Animation frame dimensions differ.");
        }
        foreach (var sprite in animations.Water.Concat(animations.Buildings))
            if (!clips.TryGetValue(sprite.ClipId, out var clip) || sprite.Phase < 0 || sprite.Phase >= clip.Frames.Length)
                throw new InvalidDataException("Invalid animation reference or phase.");
        if (animations.Water.Any(s => s.BuildingId != -1) || animations.Buildings.Any(s => !buildings.Contains(s.BuildingId)))
            throw new InvalidDataException("Invalid animation owner.");
    }
}
