using Godot;
using San9.Application.Presentation;
using San9.Content.Maps;
using San9.Core.Domain;
using San9.Rendering;

namespace San9.Client.Godot;

// Display inputs shared by cameras; sampling caches and the playback clock are explicit dependencies.
internal sealed partial class MapScene
{
    public MapPackage Map { get; }
    public TerrainRenderer Terrain { get; }
    public string Season { get; }
    public StrategySnapshot State { get; }
    public IReadOnlyDictionary<int, SettlementView> Settlements { get; }
    public MapBuilding[] Buildings { get; }
    public UnitOccluder[] Occluders { get; }
    public UnitVisual[] Units { get; }
    public IReadOnlyDictionary<int, MapAnimatedSprite[]> Animations { get; }
    private readonly IReadOnlyDictionary<int, MapAnimatedSprite[]>? _combatBuildingAnimations;
    public IEnumerable<MapAnimatedSprite> BuildingAnimations(int id, bool preload = false)
    {
        var baseline = Animations.GetValueOrDefault(id, []);
        if (_combatBuildingAnimations is null) return baseline;
        var combat = _combatBuildingAnimations.GetValueOrDefault(id, []);
        return preload ? baseline.Concat(combat).Distinct() : Day?.CombatMilliseconds >= 0 ? combat : baseline;
    }
    public IReadOnlyDictionary<string, MapAnimationClip> Clips { get; }
    public IReadOnlySet<string> HighlightableBuildingAssets { get; }
    private readonly IReadOnlyDictionary<string, San9.Content.Images.ImageAsset> _assets;
    private readonly Dictionary<(int X, int Y), int[]> _waterBuckets;

    public MapScene(MapPackage map, TerrainRenderer terrain, string season, StrategySnapshot state, IReadOnlyDictionary<string, San9.Content.Images.ImageAsset> assets, Func<double> positionTime, MapScene? previous = null, TurnDayPresentation? day = null)
    {
        Map = map; Terrain = terrain; Season = season; State = state; _assets = assets; _positionTime = positionTime;
        Settlements = state.Settlements.ToDictionary(s => s.Id);
        Clips = previous is not null && ReferenceEquals(previous.Terrain, terrain) ? previous.Clips : map.Animations.Clips.ToDictionary(c => c.Id);
        if (previous is not null && ReferenceEquals(previous.Terrain, terrain)) _waterBuckets = previous._waterBuckets;
        else
        {
            var buckets = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < map.Animations.Water.Length; i++)
                foreach (var cell in Cells(Bounds(map.Animations.Water[i])))
                {
                    if (!buckets.TryGetValue(cell, out var entries)) buckets.Add(cell, entries = []);
                    entries.Add(i);
                }
            _waterBuckets = buckets.ToDictionary(p => p.Key, p => p.Value.ToArray());
        }
        var projection = MapSceneProjection.Create(state, map);
        var buildingStates = projection.BuildingStates;
        if (day is not null)
        {
            _combatBuildingAnimations = BuildingVisuals.Resolve(map, buildingStates.Select(s => s with
                { IsEngaged = day.After.IsSettlementEngaged(s.Id) }).ToArray())
                .Animations.GroupBy(s => s.BuildingId).ToDictionary(g => g.Key, g => g.ToArray());
        }
        _unitStates = projection.Units;
        Units = _unitStates.Values.Select(s => UnitVisuals.Resolve(s, map.Units, Clips, elevationAt: ElevationAt)).ToArray();
        Buildings = projection.Buildings;
        Occluders = Buildings.Select(b => (Building: b, Area: state.SettlementGeographicArea(b.Id)))
            .Where(p => p.Area is not null).Select(p => UnitOccluder.FromGeographicArea(p.Area!.Value,
                p.Building.FixedType is 0 or 2 or 3 or 4 or 5 ? 2 : 1, p.Building.FixedType == 0)).ToArray();
        var compositions = map.Compositions.ToDictionary(c => c.Id);
        HighlightableBuildingAssets = Buildings.SelectMany(b => b.Sprites.Zip(compositions[b.CompositionId].Parts)
            .Where(p => !p.Second.PaletteAlpha).Select(p => p.First.AssetId)).ToHashSet();
        Animations = projection.Animations.GroupBy(s => s.BuildingId).ToDictionary(g => g.Key, g => g.ToArray());
        Day = day;
        _motions = day?.Plan?.Motions ?? new();
        _attacks = day?.Plan?.Attacks ?? [];
        PrepareHistory(previous);
    }
    public Rect2 Bounds(MapSprite sprite)
    {
        var asset = _assets[sprite.AssetId];
        return new(sprite.X, sprite.Y, asset.Width, asset.Height);
    }
    public IEnumerable<string> Frames(MapAnimatedSprite sprite) => Clips[sprite.ClipId].SeasonFrames.GetValueOrDefault(Season, Clips[sprite.ClipId].Frames);
    public Rect2 Bounds(MapAnimatedSprite sprite)
    {
        var size = Vector2.Zero;
        foreach (string id in Frames(sprite))
        { var asset = _assets[id]; size = size.Max(new Vector2(asset.Width, asset.Height)); }
        return new(new(sprite.X, sprite.Y), size);
    }
    public Vector2 Anchor(MapBuilding building)
    {
        var p = BuildingInfoGeometry.Anchor(building.X, building.Y, building.FixedType,
            Map.Squares[building.Y * Map.Width + building.X].Elevation);
        return new(p.X, p.Y);
    }
    public bool Intersects(MapBuilding building, Rect2 bounds) =>
        building.Sprites.Any(s => bounds.Intersects(Bounds(s))) ||
        BuildingAnimations(building.Id, true).Any(s => bounds.Intersects(Bounds(s))) ||
        bounds.Intersects(new Rect2(Anchor(building), new(116, 55)));
    public bool Intersects(UnitVisual unit, Rect2 bounds) =>
        Day is not null || _history.Length > 0 ? MotionBounds(unit).Intersects(bounds) :
        unit.Animations.Any(s => bounds.Intersects(Bounds(s))) ||
        bounds.Intersects(new Rect2(unit.LabelX, unit.LabelY, 76, 47));
    public MapSceneRegion Select(Rect2 bounds, bool boundaries)
    {
        var buildings = Buildings.Where(b => Intersects(b, bounds)).ToArray();
        return new(this, bounds, buildings,
            Units.Where(u => Intersects(u, bounds)).ToArray(),
            Map.Bridges.Where(s => bounds.Intersects(Bounds(s))).ToArray(),
            boundaries ? Map.Boundaries.Where(s => bounds.Intersects(Bounds(s))).ToArray() : [],
            Map.Clouds.Where(s => bounds.Intersects(Bounds(s))).ToArray(),
            Cells(bounds).Where(_waterBuckets.ContainsKey).SelectMany(k => _waterBuckets[k]).Distinct().Order()
                .Select(i => Map.Animations.Water[i]).Where(s => bounds.Intersects(Bounds(s))).ToArray());
    }
    private static IEnumerable<(int X, int Y)> Cells(Rect2 rect)
    {
        for (int y = (int)Math.Floor(rect.Position.Y / 512); y < Math.Ceiling(rect.End.Y / 512); y++)
        for (int x = (int)Math.Floor(rect.Position.X / 512); x < Math.Ceiling(rect.End.X / 512); x++) yield return (x, y);
    }
}

internal sealed record MapSceneRegion(MapScene Scene, Rect2 Bounds, MapBuilding[] Buildings, UnitVisual[] Units,
    MapSprite[] Bridges, MapSprite[] Boundaries, MapSprite[] Clouds, MapAnimatedSprite[] Water)
{
    public MapSceneRegion Select(Rect2 bounds) => new(Scene, bounds,
        Buildings.Where(b => Scene.Intersects(b, bounds)).ToArray(),
        Units.Where(u => Scene.Intersects(u, bounds)).ToArray(),
        Bridges.Where(s => bounds.Intersects(Scene.Bounds(s))).ToArray(),
        Boundaries.Where(s => bounds.Intersects(Scene.Bounds(s))).ToArray(),
        Clouds.Where(s => bounds.Intersects(Scene.Bounds(s))).ToArray(),
        Water.Where(s => bounds.Intersects(Scene.Bounds(s))).ToArray());
    public IEnumerable<MapAnimatedSprite> ObjectAnimations => Buildings
        .SelectMany(b => Scene.BuildingAnimations(b.Id, true))
        .Where(s => Bounds.Intersects(Scene.Bounds(s)))
        .Concat(Units.SelectMany(u => u.Animations));
    public IEnumerable<string> Assets => new[] { "cloud-fill" }
        .Concat(Bridges.Concat(Boundaries).Concat(Clouds).Concat(Buildings.SelectMany(b => b.Sprites))
            .Where(s => Bounds.Intersects(Scene.Bounds(s))).Select(s => s.AssetId))
        .Concat(Water.Concat(ObjectAnimations).SelectMany(Scene.Frames))
        .Concat(Scene.MotionAssets(Units, Bounds))
        .Concat(Buildings.SelectMany(b => Scene.Animations.GetValueOrDefault(b.Id, [])).Where(s => s.ClipId.StartsWith("flag-", StringComparison.Ordinal))
            .Select(s => Scene.Clips[s.ClipId]).Where(c => c.SeasonFrames.TryGetValue("autumn", out var frames) && frames.Length > 2)
            .Select(c => c.SeasonFrames["autumn"][2])).Distinct();

    public void Objects(double time, Action<string, Vector2, MapObjectTarget?, MapBlendMode, bool> emit,
        Action? groundOverlay = null, (double Depth, Action Draw)? objectOverlay = null,
        Action<string, Vector2>? outline = null, Rect2? viewport = null)
    {
        foreach (var s in Bridges.Concat(Boundaries)) emit(s.AssetId, new(s.X, s.Y), null, MapBlendMode.Alpha, false);
        groundOverlay?.Invoke();
        List<(double Depth, Action Draw)> draws = [];
        List<(string Asset, Vector2 Position)> outlines = [];
        void Animated(MapAnimatedSprite sprite, MapObjectTarget? target, double clock, bool highlight)
        {
            var clip = Scene.Clips[sprite.ClipId];
            string asset = sprite.Frame is int frame ? clip.Frames[frame] :
                MapAnimationClock.Asset(clip, Scene.Season, clock * sprite.TimeScale, sprite.Phase);
            var position = new Vector2(sprite.X, sprite.Y);
            draws.Add((sprite.Depth, () => emit(asset, position, target, clip.BlendMode, highlight)));
            if (sprite.IsBody && UnitOcclusion.IsOccluded(sprite.GroundX, sprite.GroundY, Scene.Occluders)) outlines.Add((asset, position));
        }
        foreach (var building in Buildings)
        {
            foreach (var sprite in building.Sprites)
                draws.Add((sprite.Depth, () => emit(sprite.AssetId, new(sprite.X, sprite.Y), new(building.Id, false),
                    MapBlendMode.Alpha, Scene.HighlightableBuildingAssets.Contains(sprite.AssetId))));
            foreach (var sprite in Scene.BuildingAnimations(building.Id)) Animated(sprite, null, time, false);
        }
        foreach (var unit in Units.Where(u => Scene.IsUnitVisible(u, viewport ?? Bounds)).Select(Scene.Sample))
            foreach (var sprite in unit.Animations)
            {
                double clock = time;
                if (sprite.IsBody && unit.Action is UnitVisualAction.Melee or UnitVisualAction.Ranged &&
                    Scene.Day is { CombatMilliseconds: >= 0 } combat) clock = combat.CombatMilliseconds;
                Animated(sprite, new(unit.Id, true), clock, sprite.IsBody);
            }
        if (objectOverlay is {} overlay) draws.Add((16 * overlay.Depth + 128 + 30000, overlay.Draw));
        foreach (var projectile in Scene.Projectiles(viewport ?? Bounds))
            draws.Add((projectile.Y + 30000, () => emit(projectile.AssetId, new((float)projectile.X, (float)projectile.Y), null, MapBlendMode.Alpha, false)));
        bool outlined = false;
        void Outlines()
        {
            foreach (var item in outlines) outline?.Invoke(item.Asset, item.Position);
            outlined = true;
        }
        foreach (var draw in draws.OrderBy(d => d.Depth))
        {
            if (!outlined && draw.Depth >= 40000) Outlines();
            draw.Draw();
        }
        if (!outlined) Outlines();
        foreach (var s in Clouds) emit(s.AssetId, new(s.X, s.Y), null, MapBlendMode.Alpha, false);
    }

}

public readonly record struct MapObjectTarget(int Id, bool IsUnit);
