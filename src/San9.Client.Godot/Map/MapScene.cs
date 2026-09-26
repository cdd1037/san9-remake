using Godot;
using San9.Application.Presentation;
using San9.Content.Maps;
using San9.Core.Domain;
using San9.Rendering;

namespace San9.Client.Godot;

// Immutable display snapshot shared by all cameras. Visible sets never own world state.
internal sealed class MapScene
{
    public MapPackage Map { get; }
    public TerrainRenderer Terrain { get; }
    public string Season { get; }
    public StrategySnapshot State { get; }
    public IReadOnlyDictionary<int, SettlementView> Settlements { get; }
    public MapBuilding[] Buildings { get; }
    public UnitVisual[] Units { get; }
    public IReadOnlyDictionary<int, MapAnimatedSprite[]> Animations { get; }
    public IReadOnlyDictionary<string, MapAnimationClip> Clips { get; }
    private readonly TextureService _textures;
    private readonly Dictionary<(int X, int Y), int[]> _waterBuckets;

    public MapScene(MapPackage map, TerrainRenderer terrain, string season, StrategySnapshot state, TextureService textures, MapScene? previous = null)
    {
        Map = map; Terrain = terrain; Season = season; State = state; _textures = textures;
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
        var forces = state.Forces.ToDictionary(f => f.Id);
        var visuals = BuildingVisuals.Resolve(map, state.Settlements.Select(s =>
        {
            var force = s.OwnerId is int owner ? forces[owner] : null;
            return new BuildingVisualState(s.Id, s.Durability, s.Appearance, Math.Max(s.Commerce, s.Cultivation),
                s.Soldiers, force?.ColorCode ?? 50, force?.FlagGlyph ?? -1)
            { IsDestroyed = s.IsDestroyed, Placement = s.FacilityTypeId is int type ? new(s.Id, s.X, s.Y, type) : null };
        }).ToArray());
        var unitPositions = state.World.Units.ToDictionary(u => u.Id, u => u.Position);
        Units = state.World.Units.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.FormationId is not null && u.Soldiers > 0)
            .Select(u =>
            {
                var order = u.EffectiveOrder;
                WorldPosition? target = u.MovementPlan is { Steps.Length: > 0 } plan ? plan.Steps[0] :
                    order?.UnitId is int targetId && unitPositions.TryGetValue(targetId, out var position) ? position :
                    order?.Position ?? (order?.SettlementId is int place ?
                        state.Settlements.FirstOrDefault(s => s.Id == place) is {} destination ? MapCoordinates.Center(new(destination.X, destination.Y)) : null : null);
                var commander = u.CommanderId is int commanderId ? state.Officers.FirstOrDefault(o => o.Id == commanderId) : null;
                var force = forces[u.OwnerId];
                return UnitVisuals.Resolve(new(u.Id,
                    (u.Position.X - MapCoordinates.UnitsPerCell / 2) / (double)MapCoordinates.UnitsPerCell,
                    (u.Position.Y - MapCoordinates.UnitsPerCell / 2) / (double)MapCoordinates.UnitsPerCell,
                    map.Squares[u.Cell.Y * map.Width + u.Cell.X].Elevation, u.Soldiers, state.FormationId(u)!.Value,
                    u.Condition, UnitVisuals.Direction(u.Position, target),
                    u.EffectiveOrderClass == UnitOrderClass.Military && state.HasCityInfluence(u.OwnerId, u.Position),
                    force.ColorCode, commander?.FlagGlyph ?? -1)
                {
                    Morale = u.Morale, CommanderNameKey = commander?.NameKey,
                    IsRulerCommander = u.CommanderId is int leader && force.LeaderId == leader,
                    UsesImperialAppearance = force.UsesImperialAppearance
                }, map.Units, Clips);
            }).ToArray();
        Buildings = visuals.Buildings;
        Animations = visuals.Animations.GroupBy(s => s.BuildingId).ToDictionary(g => g.Key, g => g.ToArray());
    }
    public Rect2 Bounds(MapSprite sprite)
    {
        var asset = _textures.MapAssets[sprite.AssetId];
        return new(sprite.X, sprite.Y, asset.Width, asset.Height);
    }
    public IEnumerable<string> Frames(MapAnimatedSprite sprite) => Clips[sprite.ClipId].SeasonFrames.GetValueOrDefault(Season, Clips[sprite.ClipId].Frames);
    public Rect2 Bounds(MapAnimatedSprite sprite)
    {
        var size = Vector2.Zero;
        foreach (string id in Frames(sprite))
        { var asset = _textures.MapAssets[id]; size = size.Max(new Vector2(asset.Width, asset.Height)); }
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
        Animations.GetValueOrDefault(building.Id, []).Any(s => bounds.Intersects(Bounds(s))) ||
        bounds.Intersects(new Rect2(Anchor(building), new(116, 55)));
    public bool Intersects(UnitVisual unit, Rect2 bounds) =>
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
        .SelectMany(b => Scene.Animations.GetValueOrDefault(b.Id, []))
        .Where(s => Bounds.Intersects(Scene.Bounds(s)))
        .Concat(Units.SelectMany(u => u.Animations));
    public IEnumerable<string> Assets => new[] { "cloud-fill" }
        .Concat(Bridges.Concat(Boundaries).Concat(Clouds).Concat(Buildings.SelectMany(b => b.Sprites))
            .Where(s => Bounds.Intersects(Scene.Bounds(s))).Select(s => s.AssetId))
        .Concat(Water.Concat(ObjectAnimations).SelectMany(Scene.Frames))
        .Concat(Buildings.SelectMany(b => Scene.Animations.GetValueOrDefault(b.Id, [])).Where(s => s.ClipId.StartsWith("flag-", StringComparison.Ordinal))
            .Select(s => Scene.Clips[s.ClipId]).Where(c => c.SeasonFrames.TryGetValue("autumn", out var frames) && frames.Length > 2)
            .Select(c => c.SeasonFrames["autumn"][2])).Distinct();

    // Buildings and field units share terrain depth; clouds cover both views last.
    public void Objects(double time, Action<string, Vector2, MapObjectTarget?, MapBlendMode> emit)
    {
        foreach (var s in Bridges.Concat(Boundaries)) emit(s.AssetId, new(s.X, s.Y), null, MapBlendMode.Alpha);
        var objects = Buildings.Select(b => (Depth: (double)(b.X + b.Y), X: (double)b.X, Kind: 0, Id: b.Id,
                Building: (MapBuilding?)b, Unit: (UnitVisual?)null))
            .Concat(Units.Select(u => (Depth: u.Depth, X: u.CellX, Kind: 1, Id: u.Id,
                Building: (MapBuilding?)null, Unit: (UnitVisual?)u)))
            .OrderBy(o => o.Depth).ThenBy(o => o.X).ThenBy(o => o.Kind).ThenBy(o => o.Id);
        foreach (var item in objects)
        {
            if (item.Building is { } building)
            {
                foreach (var s in building.Sprites) emit(s.AssetId, new(s.X, s.Y), new(building.Id, false), MapBlendMode.Alpha);
                foreach (var s in Scene.Animations.GetValueOrDefault(building.Id, []))
                    emit(MapAnimationClock.Asset(Scene.Clips[s.ClipId], Scene.Season, time, s.Phase), new(s.X, s.Y), null, Scene.Clips[s.ClipId].BlendMode);
            }
            else
                foreach (var s in item.Unit!.Animations)
                    emit(MapAnimationClock.Asset(Scene.Clips[s.ClipId], Scene.Season, time, s.Phase), new(s.X, s.Y), new(item.Id, true), Scene.Clips[s.ClipId].BlendMode);
        }
        foreach (var s in Clouds) emit(s.AssetId, new(s.X, s.Y), null, MapBlendMode.Alpha);
    }
}

public readonly record struct MapObjectTarget(int Id, bool IsUnit);
