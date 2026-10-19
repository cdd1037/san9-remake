using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>World-owned occupancy rectangles in stable order; passage is filtered per query.</summary>
internal sealed class MovementOccupancy
{
    internal MilitaryRulebook Rules { get; }
    private readonly SortedDictionary<(bool Unit, int Id), MapOccupancy> areas = [];

    private readonly Dictionary<MapCell, HashSet<int>> buildingCells = [];

    internal bool BlocksContact(WorldPosition position, int? target)
    {
        if (!buildingCells.TryGetValue(MapCoordinates.Cell(position), out var ids)) return false;
        foreach (int id in ids)
        {
            if (id == target) continue;
            var area = areas[(false, id)];
            if (area.Contains(position)) return true;
        }
        return false;
    }

    private IEnumerable<MapCell> BuildingCells(MapOccupancy area)
    {
        int scale = MapCoordinates.UnitsPerCell;
        long left = Math.Max(0, area.Left), top = Math.Max(0, area.Top);
        long right = Math.Min(Rules.Map.FineWidth, area.Right), bottom = Math.Min(Rules.Map.FineHeight, area.Bottom);
        if (left >= right || top >= bottom) yield break;
        for (int y = (int)(top / scale); y <= (bottom - 1) / scale; y++)
            for (int x = (int)(left / scale); x <= (right - 1) / scale; x++) yield return new(x, y);
    }

    internal MovementOccupancy(IWorldQuery world, MilitaryRulebook rules) : this(world.Settlements.Values, world.Units.Values, rules) { }
    private MovementOccupancy(IEnumerable<SettlementState> places, IEnumerable<UnitState> units, MilitaryRulebook rules)
    {
        Rules = rules;
        foreach (var place in places) Set(place);
        foreach (var unit in units) Set(unit);
    }
    internal static IReadOnlyList<MapOccupancy> Snapshot(WorldSnapshot world, MilitaryRulebook rules, int owner, UnitPolicies policies, bool includeUnits,
        int? movingUnit = null)
    {
        var units = world.Units.ToDictionary(u => u.Id);
        var places = world.Settlements.Where(s => !s.IsDormant).ToDictionary(s => s.Id);
        return new MovementOccupancy(places.Values, includeUnits ? world.Units : [], rules)
            .Query(units, places, policies, other => MilitaryDiplomacy.AutomaticEnemy(world, owner, other), owner, movingUnit);
    }

    internal void Set(UnitState unit) => Set((true, unit.Id), unit.FormationId is int id
        ? new(unit.Position, Rules.UnitFootprint(unit)) : null);
    internal void Set(SettlementState place) => Set((false, place.Id), !place.IsDestroyed && Rules.TryPlace(place, out var p)
        ? Rules.BuildingArea(p) : null);
    internal void RemoveUnit(int id) => Set((true, id), null);
    private void Set((bool Unit, int Id) key, MapOccupancy? area)
    {
        if (area is { } next) area = next with { Object = new(key.Id, !key.Unit) };
        if (areas.TryGetValue(key, out var previous))
        {
            if (area == previous) return;
            if (!key.Unit)
                foreach (var cell in BuildingCells(previous))
                    if (buildingCells.TryGetValue(cell, out var ids) && ids.Remove(key.Id) && ids.Count == 0) buildingCells.Remove(cell);
        }
        if (area is not { } value) { areas.Remove(key); return; }
        areas[key] = value;
        if (!key.Unit)
            foreach (var cell in BuildingCells(value))
            {
                if (!buildingCells.TryGetValue(cell, out var ids)) buildingCells[cell] = ids = [];
                ids.Add(key.Id);
            }
    }

    internal IReadOnlyList<MapOccupancy> Query(IWorldQuery world, int owner, UnitPolicies policies, int? movingUnit = null, int? entry = null)
        => Query(world.Units, world.Settlements, policies, other => MilitaryDiplomacy.AutomaticEnemy(world, owner, other), owner, movingUnit, entry);

    private IReadOnlyList<MapOccupancy> Query(IReadOnlyDictionary<int, UnitState> units, IReadOnlyDictionary<int, SettlementState> places,
        UnitPolicies policies, Func<int?, bool> enemyOf, int owner, int? movingUnit = null, int? entry = null)
    {
        var result = new List<MapOccupancy>();
        foreach (var (key, area) in areas)
        {
            if (key.Unit)
            {
                if (key.Id == movingUnit) continue;
                var unit = units[key.Id];
                if (unit.IsEmbedded || unit.IsPersonnel) continue;
                bool enemy = unit.OwnerId != owner;
                result.Add(area with { Hard = enemy && policies.AttackApproachingEnemies,
                    Avoidance = enemy ? NavigationAvoidance.Hostile : NavigationAvoidance.Friendly });
            }
            else
            {
                if (key.Id == entry) continue;
                var place = places[key.Id];
                if (Rules.Place(place).FacilityTypeId is int type && Rules.Facilities[type].IsWork) result.Add(area with { Avoidance = NavigationAvoidance.Facility });
                else if (place.OwnerId is not null && enemyOf(place.OwnerId))
                    result.Add(area with { Avoidance = NavigationAvoidance.Facility });
                else result.Add(area with { Hard = false, Avoidance = NavigationAvoidance.None });
            }
        }
        return result;
    }
}

public sealed record MilitaryRoutePlan(NavigationStatus Status, ImmutableArray<WorldPosition> Steps)
{
    public int? Days { get; init; }
}

/// <summary>Read-only intention routes share the coarse planner with daily movement.</summary>
public static class MilitaryRouteQuery
{
    /// <summary>Plans coarse route segments through the player waypoint without simulating future days.</summary>
    public static MilitaryRoutePlan Plan(WorldSnapshot snapshot, MilitaryRulebook rules, UnitState unit, int? movingUnit,
        int maximumDays = 4096, int maximumExpanded = 100000)
        => new MilitaryRouteQueryContext(snapshot, rules).Plan(unit, movingUnit, maximumDays, maximumExpanded);

    internal static MilitaryRoutePlan Plan(IWorldQuery world, MilitaryRulebook rules, UnitState unit, int? movingUnit,
        int maximumDays, int maximumExpanded)
    {
        var work = new NavigationWorkBudget(maximumExpanded);
        var travel = rules.Travel(unit);
        var order = unit.EffectiveOrder!;
        var target = order.IsAttack ? MilitaryCombat.TargetArea(world, order, rules) :
            order.Construction is { } site ? rules.FacilityArea(site.Position, site.FacilityTypeId) :
            order.SettlementId is int home ? rules.SettlementArea(world.Settlements[home]) : new MapOccupancy(order.Position!.Value, new(1, 1));
        var occupancy = world.MovementOccupancy(rules, unit.OwnerId, unit.Policies, movingUnit, order.Construction?.RegionId);
        var steps = ImmutableArray.CreateBuilder<WorldPosition>();
        WorldPosition start = unit.Position;
        RouteCostEstimate Segment(WorldPosition end, MapOccupancy? excluded = null) => travel.Map.EstimateTravelCost(start, new(end, new(1, 1)),
            travel.Shape, travel.Mode, occupancy, work, intoRange: false, includeCells: true, excluded: excluded);
        var waypoint = unit.ActiveWaypoint;
        if (waypoint is { } via)
        {
            var first = Segment(via, order.SettlementId is not null ? target : null);
            if (first.Status != RouteReachability.Reachable)
                return new(first.Status == RouteReachability.Incomplete ? NavigationStatus.Planning : NavigationStatus.Unreachable, []);
            steps.AddRange(first.Cells.Select(MapCoordinates.Center));
            start = via;
        }
        var last = Segment(target.Position, waypoint is not null && order.SettlementId is not null ? target : null);
        if (last.Status != RouteReachability.Reachable)
            return new(last.Status == RouteReachability.Incomplete ? NavigationStatus.Planning : NavigationStatus.Unreachable, steps.ToImmutable());
        foreach (var cell in last.Cells)
        {
            var position = MapCoordinates.Center(cell);
            if (steps.Count == 0 || steps[^1] != position) steps.Add(position);
        }
        return new(NavigationStatus.Arrived, steps.ToImmutable());
    }

    public static IReadOnlyList<MapOccupancy> Occupancy(WorldSnapshot snapshot, MilitaryRulebook rules, int owner, UnitPolicies policies,
        bool includeUnits = true, int? movingUnit = null) => MovementOccupancy.Snapshot(snapshot, rules, owner, policies, includeUnits, movingUnit);

    public static WorldPosition? DeploymentPosition(UnitTravel travel, MapOccupancy place, IReadOnlyList<MapOccupancy> occupancy)
    {
        var area = new MapOccupancy(place.Position, travel.Map.ShapeAt(place.Position, travel.Shape));
        if (travel.Map.EndpointPassageCost(place.Position, travel.Mode) is not null &&
            !occupancy.Any(o => o.Hard && area.Overlaps(o))) return place.Position;
        // Water-only formations can require an actual shoreline entrance instead of a land facility's center.
        return travel.Entrances(place).Cast<WorldPosition?>().FirstOrDefault(p =>
            travel.Map.PassageCost(p!.Value, travel.Shape, travel.Mode, occupancy) is not null);
    }
}
