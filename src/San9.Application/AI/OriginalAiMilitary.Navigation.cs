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
                ? new(unitTarget.Position, rules.Formation(unitTarget).Footprint) : null
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
            var first = travel.Map.FindRoute(start, waypoint, travel.Shape, travel.Mode, []);
            if (!first.Reachable) return Remember(order);
            steps.AddRange(first.Steps);
            start = waypoint;
        }
        paths++;
        var last = order.Kind == MilitaryOrderKind.Move
            ? travel.Map.FindRoute(start, destination.Value.Position, travel.Shape, travel.Mode, [])
            : travel.Map.FindRouteIntoRange(start, destination.Value, travel.Shape, travel.Mode, []);
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

    private sealed record Route(int Days, int CellCount, bool Slow, bool Obstacle, bool HostileBuilding, bool HostileRegion);
    private Route? RouteTo(UnitState unit, MilitaryOrder order, int? moving = null)
    {
        Refresh();
        var key = (unit, order, moving);
        if (routes.TryGetValue(key, out var cached)) return cached;
        var planned = unit with { Order = order, EngagementTargetId = null, DepartureDaysRemaining = 0, Waypoint = null, Lookahead = null };
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
        bool obstacle = path.Cells.Any(p => obstacles.Any(s =>
            rules.SettlementArea(s).Overlaps(new(MapCoordinates.Center(p), new(1, 1)))));
        int? targetRegion = unit.Order?.UnitId is int targetUnit
            ? World.Units.FirstOrDefault(u => u.Id == targetUnit) is { } target ? rules.RegionAt(target.Position) : null
            : unit.Order?.SettlementId;
        bool hostileBuilding = path.Cells.Any(p => BuildingAlongRoute(MapCoordinates.Center(p), s =>
            s.Id != targetRegion && !s.IsDestroyed && !s.IsDormant && s.OwnerId is int owner &&
            OriginalAiStrategy.IsOpponent(World, unit.OwnerId, owner)) is not null);
        bool hostileRegion = path.Cells.Any(p => rules.RegionAt(MapCoordinates.Center(p)) is int region &&
            World.Settlements.Any(s => s.Id == region && !s.IsDestroyed && !s.IsDormant && s.OwnerId is int owner &&
                OriginalAiStrategy.IsOpponent(World, unit.OwnerId, owner)));
        return new(days, path.Cells.Length, slow, obstacle, hostileBuilding, hostileRegion);
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
