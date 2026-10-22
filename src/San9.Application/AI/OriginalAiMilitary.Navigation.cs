using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal sealed partial class OriginalAiMilitary
{
    // RE-0193: read effective orders independently of player display settings, then project
    // intervening route obstacles without changing the unit's authoritative order.
    private MilitaryOrder? ProjectedOrder(UnitState u)
    {
        var order = u.EffectiveOrder;
        // Resolving an intervening target cannot turn a return, transport or construction task into an attack.
        if (u.IsEmbedded || u.IsPersonnel || u.FormationId is null || u.EngagementTargetId is not null ||
            !OriginalAiStrategy.IsOpponent(World, Owner, u.OwnerId) ||
            order?.Kind is not (MilitaryOrderKind.Move or MilitaryOrderKind.AttackUnit or MilitaryOrderKind.AttackSettlement)) return order;
        Refresh();
        if (orders.TryGetValue(u.Id, out var cached)) return cached;
        MilitaryOrder Remember(MilitaryOrder result) { orders[u.Id] = result; return result; }
        MapOccupancy? destination = order.UnitId is int targetUnit
            ? World.Units.FirstOrDefault(t => t.Id == targetUnit) is { } unitTarget
                ? new(unitTarget.Position, rules.UnitFootprint(unitTarget)) : null
            : order.SettlementId is int targetId
                ? World.Settlements.FirstOrDefault(s => s.Id == targetId && !s.IsDestroyed && !s.IsDormant) is { } target
                    ? rules.SettlementArea(target) : null
                : order.Position is { } position ? new(position, new(1, 1)) : null;
        if (destination is null) return Remember(order);
        var travel = rules.Travel(u);
        var steps = ImmutableArray.CreateBuilder<WorldPosition>();
        var start = u.Position;
        if (u.Waypoint is { } waypoint)
        {
            paths++;
            var first = terrainRoutes.FindRoute(travel.Map, start, waypoint, travel.Shape, travel.Mode);
            if (!first.Reachable) return Remember(order);
            steps.AddRange(first.Steps);
            start = waypoint;
        }
        paths++;
        var last = order.Kind == MilitaryOrderKind.Move
            ? terrainRoutes.FindRoute(travel.Map, start, destination.Value.Position, travel.Shape, travel.Mode)
            : terrainRoutes.FindRouteIntoRange(travel.Map, start, destination.Value, travel.Shape, travel.Mode);
        if (!last.Reachable) return Remember(order);
        steps.AddRange(last.Steps);
        foreach (var point in steps)
        {
            var obstacle = BuildingAlongRoute(point, s => !s.IsDestroyed && !s.IsDormant &&
                rules.TryPlace(s, out var p) &&
                (p.FacilityTypeId is int type && Policy.RouteObstacleFacilityIds.Contains(type) || s.Id != order.SettlementId &&
                    s.OwnerId is int owner && OriginalAiStrategy.IsOpponent(World, u.OwnerId, owner)));
            if (obstacle is not null) return Remember(new(MilitaryOrderKind.AttackSettlement, null, obstacle.Id));
        }
        return Remember(order);
    }
    private SettlementState? BuildingAlongRoute(WorldPosition point, Func<SettlementState, bool> eligible)
    {
        Refresh();
        if (rules.RegionAt(point) is int region)
        {
            regionBuildings ??= World.Settlements.ToDictionary(s => s.Id);
            return regionBuildings.TryGetValue(region, out var building) && eligible(building) &&
                rules.SettlementArea(building).Overlaps(new(point, new(1, 1))) ? building : null;
        }
        return World.Settlements.FirstOrDefault(s => eligible(s) && rules.SettlementArea(s).Overlaps(new(point, new(1, 1))));
    }

    // Retain the route's immutable world inputs: deferred inspection must not observe a later command.
    private sealed class Route(int days, ImmutableArray<MapCell> cells, bool slow, bool obstacle,
        WorldSnapshot world, MilitaryRulebook rules, IReadOnlyDictionary<int, SettlementState> buildings,
        int owner, int? targetRegion)
    {
        public int Days => days;
        public int CellCount => cells.Length;
        public bool Slow => slow;
        public bool Obstacle => obstacle;
        private bool? hostileBuilding, hostileRegion;
        public bool HostileBuilding => hostileBuilding ??= InspectHostility(buildingOnly: true);
        public bool HostileRegion => hostileRegion ??= InspectHostility(buildingOnly: false);

        private bool Hostile(SettlementState s) => !s.IsDestroyed && !s.IsDormant && s.OwnerId is int other &&
            OriginalAiStrategy.IsOpponent(world, owner, other);
        private bool InspectHostility(bool buildingOnly)
        {
            foreach (var cell in cells)
            {
                var point = MapCoordinates.Center(cell);
                if (rules.RegionAt(point) is int region)
                {
                    if (buildings.TryGetValue(region, out var building) && Hostile(building) &&
                        (!buildingOnly || building.Id != targetRegion &&
                            rules.SettlementArea(building).Overlaps(new(point, new(1, 1))))) return true;
                }
                else if (buildingOnly)
                    foreach (var building in world.Settlements)
                        if (building.Id != targetRegion && Hostile(building) &&
                            rules.SettlementArea(building).Overlaps(new(point, new(1, 1)))) return true;
            }
            return false;
        }
    }
    private Route? RouteTo(UnitState unit, MilitaryOrder order, int? moving = null)
    {
        Refresh();
        var key = (unit, order, moving);
        if (routes.TryGetValue(key, out var cached)) return cached;
        var planned = unit.WithMilitaryOrder(order, unit.Policies, null);
        var result = AnalyzeRoute(planned, moving);
        routes[key] = result;
        return result;
    }
    private Route? AnalyzeRoute(UnitState unit, int? moving)
    {
        paths++;
        var path = Navigation.Estimate(unit, moving);
        if (path.Status == RouteReachability.Incomplete) { incomplete = true; return null; }
        if (path.Status != RouteReachability.Reachable || path.Days is not int days) return null;
        bool slow = content.Military is { } map && path.Cells.Any(p => Policy.SlowTerrainIds.Contains(map.TerrainIds[p.Y * map.Width + p.X]));
        obstacles ??= World.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && rules.TryPlace(s, out var place) &&
            place.FacilityTypeId is int type && Policy.RouteObstacleFacilityIds.Contains(type)).ToArray();
        bool obstacle = false;
        foreach (var cell in path.Cells)
        {
            var area = new MapOccupancy(MapCoordinates.Center(cell), new(1, 1));
            foreach (var building in obstacles)
                if (rules.SettlementArea(building).Overlaps(area)) { obstacle = true; break; }
            if (obstacle) break;
        }
        int? targetRegion = unit.Order?.UnitId is int targetUnit
            ? World.Units.FirstOrDefault(u => u.Id == targetUnit) is { } target ? rules.RegionAt(target.Position) : null
            : unit.Order?.SettlementId;
        regionBuildings ??= World.Settlements.ToDictionary(s => s.Id);
        return new(days, path.Cells, slow, obstacle, World, rules, regionBuildings, unit.OwnerId, targetRegion);
    }
    private Route? RouteFrom(SettlementState source, int land, int? naval, MilitaryOrder order, UnitPolicies? policies = null)
    {
        Refresh();
        policies ??= new();
        var key = (source.Id, land, naval, order, policies);
        if (sourceRoutes.TryGetValue(key, out var cached)) return cached;
        var departure = (source.Id, land, naval, policies);
        if (!departures.TryGetValue(departure, out var start))
        {
            start = Navigation.DeploymentPosition(source, land, naval, Owner, policies);
            departures.Add(departure, start);
        }
        var result = start is { } position ? RouteTo(new(0, Owner, position, 1) { FormationId = land,
            NavalFormationId = naval, Policies = policies }, order) : null;
        sourceRoutes.Add(key, result);
        return result;
    }
}
