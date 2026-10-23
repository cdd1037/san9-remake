using San9.Application.Presentation;
using San9.Content.Maps;
using San9.Core.Domain;
using San9.Rendering;

namespace San9.Client.Godot;

internal sealed record MapSceneProjection(BuildingVisualState[] BuildingStates,
    MapBuilding[] Buildings, MapAnimatedSprite[] Animations, Dictionary<int, UnitVisualState> Units)
{
    public MapAnimatedSprite[] CombatAnimations { get; init; } = [];

    private static BuildingVisualState BuildingState(StrategySnapshot state, SettlementView settlement,
        IReadOnlyDictionary<int, ForceView> forces)
    {
        var force = settlement.OwnerId is int owner ? forces[owner] : null;
        return new(settlement.Id, settlement.Durability, settlement.Appearance, Math.Max(settlement.Commerce, settlement.Cultivation),
            settlement.Soldiers, force?.ColorCode ?? 50, force?.FlagGlyph ?? -1)
        {
            IsDestroyed = settlement.IsDestroyed,
            Placement = settlement.FacilityTypeId is int type ? new(settlement.Id, settlement.X, settlement.Y, type) : null,
            IsEngaged = state.IsSettlementEngaged(settlement.Id), IsUnderConstruction = settlement.IsUnderConstruction,
            Morale = settlement.Morale, Month = state.Month
        };
    }

    private static MapAnimatedSprite[] Combat(MapPackage map, IEnumerable<BuildingVisualState> states, StrategySnapshot? after) =>
        after is null ? [] : BuildingVisuals.Resolve(map, states.Select(s => s with
            { IsEngaged = after.IsSettlementEngaged(s.Id) }).ToArray()).Animations;

    public static MapSceneProjection Create(StrategySnapshot state, MapPackage map, BattleMapFrame? frame = null,
        StrategySnapshot? after = null)
    {
        var forces = state.Forces.ToDictionary(f => f.Id);
        var buildingStates = (frame?.Settlements.Values ?? state.Settlements).Select(s => BuildingState(state, s, forces)).ToArray();
        var visuals = BuildingVisuals.Resolve(map, buildingStates);
        var units = state.World.Units.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.FormationId is not null && u.Soldiers > 0 &&
                frame?.RemovedUnits.Contains(u.Id) != true)
            .Select(u =>
            {
                var commander = u.CommanderId is int commanderId ? state.Officers.FirstOrDefault(o => o.Id == commanderId) : null;
                var force = forces[u.OwnerId];
                return new UnitVisualState(u.Id,
                    (u.Position.X - MapCoordinates.UnitsPerCell / 2) / (double)MapCoordinates.UnitsPerCell,
                    (u.Position.Y - MapCoordinates.UnitsPerCell / 2) / (double)MapCoordinates.UnitsPerCell,
                    map.Squares[u.Cell.Y * map.Width + u.Cell.X].Elevation, u.Soldiers, state.FormationId(u)!.Value,
                    u.Condition, (int)u.Facing,
                    u.EffectiveOrderClass == UnitOrderClass.Military && state.HasCityInfluence(u.OwnerId, u.Position),
                    force.ColorCode, commander?.FlagGlyph ?? -1, map.Height)
                {
                    IsEngaged = state.IsUnitEngaged(u.Id),
                    VisionAngleDegrees = state.Information.Formations.Single(f => f.Id == state.FormationId(u)).Presentation?.VisionAngleDegrees ?? 0,
                    Morale = u.Morale, CommanderNameKey = commander?.NameKey,
                    IsRulerCommander = u.CommanderId is int leader && force.LeaderId == leader,
                    UsesImperialAppearance = force.UsesImperialAppearance
                };
            }).ToDictionary(u => u.Id);
        return new(buildingStates, visuals.Buildings, visuals.Animations, units)
        { CombatAnimations = Combat(map, buildingStates, after) };
    }

    // A presentation boundary changes local building inputs, not the world's unit projection.
    public MapSceneProjection WithFrame(StrategySnapshot state, MapPackage map, BattleMapFrame previous,
        BattleMapFrame frame, StrategySnapshot after)
    {
        var changed = frame.Settlements.Values.Where(s => !previous.Settlements.TryGetValue(s.Id, out var old) || old != s).ToArray();
        var changedIds = changed.Select(s => s.Id).ToHashSet();
        var forces = state.Forces.ToDictionary(f => f.Id);
        var states = changed.Select(s => BuildingState(state, s, forces)).ToArray();
        var visuals = changed.Length == 0 ? new BuildingVisuals([], []) : BuildingVisuals.Resolve(map, states);
        var buildings = Replace(Buildings, visuals.Buildings, changedIds, b => b.Id,
            (a, b) => (a with { Sprites = b.Sprites }) == b && a.Sprites.SequenceEqual(b.Sprites));
        if (!ReferenceEquals(buildings, Buildings)) buildings = buildings.OrderBy(b => b.X + b.Y).ThenBy(b => b.X).ToArray();
        var units = previous.RemovedUnits.SetEquals(frame.RemovedUnits) ? Units :
            Units.Where(p => !frame.RemovedUnits.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        return new(BuildingStates.Where(s => !changedIds.Contains(s.Id)).Concat(states).ToArray(), buildings,
            Replace(Animations, visuals.Animations, changedIds, s => s.BuildingId), units)
        { CombatAnimations = Replace(CombatAnimations, changed.Length == 0 ? [] : Combat(map, states, after), changedIds, s => s.BuildingId) };
    }

    private static T[] Replace<T>(T[] previous, T[] next, IReadOnlySet<int> changed, Func<T, int> id,
        Func<T, T, bool>? equals = null)
    {
        var oldGroups = previous.Where(s => changed.Contains(id(s))).GroupBy(id).ToDictionary(g => g.Key, g => g.ToArray());
        var newGroups = next.GroupBy(id).ToDictionary(g => g.Key, g => g.ToArray());
        bool Same(T[] a, T[] b) => a.Length == b.Length && a.Zip(b).All(p =>
            equals?.Invoke(p.First, p.Second) ?? EqualityComparer<T>.Default.Equals(p.First, p.Second));
        return changed.All(key => Same(oldGroups.GetValueOrDefault(key, []), newGroups.GetValueOrDefault(key, []))) ? previous :
            previous.Where(s => !changed.Contains(id(s))).Concat(next).ToArray();
    }
}
