using San9.Content.Packages;
using San9.Content.Images;
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
public sealed record MapBuildingPlacement(int Id, int X, int Y, int FixedType);
public sealed record MapBuildingType(int Id, int MaxDurability);
public sealed record MapGuard(int X, int Y, int Direction);
public sealed record MapFlag(int X, int Y);
public sealed record MapBuilding(int Id, int X, int Y, int FixedType, int CompositionId, MapSprite[] Sprites);
public sealed record MapComposition(int Id, MapCompositionPart[] Parts)
{
    [System.Text.Json.Serialization.JsonRequired] public MapGuard[] Guards { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired] public MapFlag[] Flags { get; init; } = [];
}
public sealed record MapCompositionPart(bool PaletteAlpha, bool DevelopmentVariant, int ResourceId, int X, int Y);
public sealed record MapAnimationClip(string Id, string[] Frames, int FrameMilliseconds, Dictionary<string, string[]> SeasonFrames);
public sealed record MapAnimatedSprite(string ClipId, int X, int Y, int Phase, int BuildingId);
public sealed record MapAnimations(MapAnimationClip[] Clips, MapAnimatedSprite[] Water);
public sealed record MapPackage(
    int SchemaVersion, string SourceBuild, string ScenarioId, string InitialSeason,
    int Width, int Height, int CanvasWidth, int CanvasHeight,
    MapSquare[] Squares, MapSeason[] Seasons, ImageAsset[] Assets, MapMask[] Masks,
    TerrainStamp[] Terrain, MapSprite[] Bridges, MapBuildingPlacement[] Buildings,
    MapComposition[] Compositions, MapSprite[] Clouds, int?[] LeftEdge, int?[] RightEdge,
    ContentSource[] Sources, MapAnimations Animations, MapSprite[] Boundaries)
{
    public byte[] MinimapColors { get; init; } = new byte[32];
    [System.Text.Json.Serialization.JsonRequired] public MapBuildingType[] BuildingTypes { get; init; } = [];
}

public static class MapPackageFiles
{
    public static void Validate(MapPackage map)
    {
        if (map.SchemaVersion != 9 || map.Width != 200 || map.Height != 200 || map.Squares.Length != 40000 ||
            map.CanvasWidth != 13056 || map.CanvasHeight != 6656) throw new InvalidDataException("Unsupported map dimensions or schema.");
        if (!map.Seasons.Select(s => s.Id).Order().SequenceEqual(new[] { "autumn", "spring", "summer", "winter" }) ||
            !map.Seasons.Any(s => s.Id == map.InitialSeason)) throw new InvalidDataException("Invalid seasons.");
        ImageAssetFiles.ValidateCatalog(map.Assets.Concat(map.Seasons.SelectMany(s => s.Textures)));
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
        foreach (var sprite in map.Boundaries.Concat(map.Bridges).Concat(map.Clouds))
            if (!common.ContainsKey(sprite.AssetId)) throw new InvalidDataException("Missing sprite reference.");
        if (map.MinimapColors.Length != 32) throw new InvalidDataException("Invalid minimap colors.");
        if (!common.ContainsKey("cloud-fill") || map.LeftEdge.Length != map.CanvasHeight || map.RightEdge.Length != map.CanvasHeight)
            throw new InvalidDataException("Invalid cloud boundaries.");
        foreach (var square in map.Squares)
            if (square.Terrain is < 0 or > 16 || square.Elevation is < 0 or > 7 || square.ElevationMask is < 0 or > 15)
                throw new InvalidDataException("Invalid square.");
        foreach (var building in map.Buildings)
            if (building.X is < 0 or >= 200 || building.Y is < 0 or >= 200)
                throw new InvalidDataException("Invalid building.");
        var types = map.BuildingTypes.ToDictionary(t => t.Id);
        for (int resource = 5663; resource <= 5974; resource++)
            if (!common.ContainsKey($"building-{resource}-alpha") || !common.ContainsKey($"building-{resource}-key"))
                throw new InvalidDataException("Incomplete building resource catalog.");
        if (map.Buildings.Select(b => b.Id).Distinct().Count() != map.Buildings.Length ||
            map.Buildings.Any(b => !types.ContainsKey(b.FixedType)) || types.Values.Any(t => t.Id is < 0 or > 16 || t.MaxDurability <= 0))
            throw new InvalidDataException("Invalid building type catalog.");
        if (map.Compositions.Select(c => c.Id).Distinct().Count() != map.Compositions.Length)
            throw new InvalidDataException("Duplicate building composition.");
        foreach (var composition in map.Compositions)
        {
            if (composition.Parts.Length == 0 || composition.Guards.Any(g => g.Direction is < 0 or > 7))
                throw new InvalidDataException("Invalid building composition.");
            foreach (var part in composition.Parts)
                for (int variant = 0; variant < (composition.Id < 15 && part.DevelopmentVariant ? 3 : 1); variant++)
                    if (!common.ContainsKey($"building-{part.ResourceId+variant}-{(part.PaletteAlpha?"alpha":"key")}"))
                        throw new InvalidDataException("Incomplete building development catalog.");
        }
        var clipIds = map.Animations.Clips.Select(c => c.Id).ToHashSet();
        var requiredClips = Enumerable.Range(0,51).Select(c => $"flag-cloth-{c}")
            .Concat(Enumerable.Range(0,2).SelectMany(v => Enumerable.Range(0,186).Select(g => $"flag-glyph-{v}-{g}")))
            .Concat(Enumerable.Range(0,8).Select(d => $"soldier-{d}"))
            .Concat(["flag-pole-back","flag-pole-front"]);
        if (requiredClips.Any(id => !clipIds.Contains(id))) throw new InvalidDataException("Incomplete runtime flag or guard catalog.");
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
        foreach (var sprite in animations.Water)
            if (!clips.TryGetValue(sprite.ClipId, out var clip) || sprite.Phase < 0 || sprite.Phase >= clip.Frames.Length)
                throw new InvalidDataException("Invalid animation reference or phase.");
        if (animations.Water.Any(s => s.BuildingId != -1))
            throw new InvalidDataException("Invalid animation owner.");
    }
}
