using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record MilitaryRouteEstimate(RouteReachability Status, int? Days, ImmutableArray<MapCell> Cells);

/// <summary>Sequential route queries sharing indexes for one immutable world and rulebook.</summary>
public sealed class MilitaryRouteQueryContext(WorldSnapshot snapshot, MilitaryRulebook rules)
{
    private WorldQuery? world;
    private readonly Dictionary<(int Owner, UnitPolicies Policies), IReadOnlyList<MapOccupancy>> deploymentOccupancy = [];
    private readonly Dictionary<ReachabilityQuery, RouteReachability> reachability = [];

    // Speed changes route preference and ETA, but not connectivity. PlanningMap retains both
    // land/water footprints and passage rules while normalizing their relative speeds.
    private readonly record struct ReachabilityQuery(BattlefieldMap Map, WorldPosition Start, MapOccupancy Target,
        Footprint Shape, TravelMode Mode, int Owner, UnitPolicies Policies, int? MovingUnit,
        int? ConstructionRegion, bool IntoRange);

    public MilitaryRoutePlan Plan(UnitState unit, int? movingUnit, int maximumDays = 4096, int maximumExpanded = 100000) =>
        MilitaryRouteQuery.Plan(world ??= new(snapshot), rules, unit, movingUnit, maximumDays, maximumExpanded);

    /// <summary>One strategic search; frozen occupancy affects route selection, never adds to travel time.
    /// This estimate does not guarantee that daily movement will reach the destination on that day.</summary>
    public MilitaryRouteEstimate Estimate(UnitState unit, int? movingUnit, int maximumExpanded = 100000)
    {
        var travel = rules.Travel(unit);
        var route = FindRoute(unit, movingUnit, travel, maximumExpanded, connectivityOnly: false);
        int? days = route.Status == RouteReachability.Reachable
            ? (int)Math.Min(int.MaxValue, (route.Cost + travel.DailyBudget - 1) / travel.DailyBudget) : null;
        return new(route.Status, days, route.Cells);
    }

    /// <summary>Strategic connectivity only. Reuses completed estimates with identical passage conditions;
    /// neither this check nor an estimate guarantees footprint clearance during daily movement.</summary>
    public RouteReachability Reachability(UnitState unit, int? movingUnit, int maximumExpanded = 100000) =>
        FindRoute(unit, movingUnit, rules.Travel(unit), maximumExpanded, connectivityOnly: true).Status;

    private RouteCostEstimate FindRoute(UnitState unit, int? movingUnit, UnitTravel travel, int maximumExpanded,
        bool connectivityOnly)
    {
        if (travel.DailyBudget <= 0 || unit.EffectiveOrder is not { } order)
            return new(RouteReachability.Unreachable);
        var query = world ??= new(snapshot);
        var target = order.IsAttack ? MilitaryCombat.TargetArea(query, order, rules) :
            order.Construction is { } construction ? rules.FacilityArea(construction.Position, construction.FacilityTypeId) :
            order.SettlementId is int home ? rules.SettlementArea(query.Settlements[home]) : new MapOccupancy(order.Position!.Value, new(1, 1));
        bool intoRange = order.IsAttack || order.SettlementId is not null || order.Construction is not null;
        var key = new ReachabilityQuery(travel.Map.PlanningMap(), unit.Position, target, travel.Shape, travel.Mode,
            unit.OwnerId, unit.Policies, movingUnit, order.Construction?.RegionId, intoRange);
        if (connectivityOnly && reachability.TryGetValue(key, out var cached)) return new(cached);
        RouteCostEstimate route;
        if (order.SettlementId is int destination && query.Settlements[destination].OwnerId == unit.OwnerId &&
            new MapOccupancy(unit.Position, rules.UnitFootprint(unit)).Overlaps(target))
            route = new(RouteReachability.Reachable) { Cells = connectivityOnly ? [] : [MapCoordinates.Cell(unit.Position)] };
        else
            route = travel.Map.EstimateTravelCost(unit.Position, target, travel.Shape, travel.Mode,
                query.MovementOccupancy(rules, unit.OwnerId, unit.Policies, movingUnit, order.Construction?.RegionId),
                new NavigationWorkBudget(maximumExpanded), intoRange: intoRange, includeCells: !connectivityOnly);
        if (route.Status != RouteReachability.Incomplete) reachability[key] = route.Status;
        return route;
    }

    public WorldPosition? DeploymentPosition(SettlementState source, int land, int? naval, int owner, UnitPolicies policies)
    {
        var travel = rules.Travel(land, naval);
        if (travel.DailyBudget <= 0 || !rules.TryPlace(source, out var place)) return null;
        var key = (owner, policies);
        if (!deploymentOccupancy.TryGetValue(key, out var occupancy))
        {
            occupancy = MilitaryRouteQuery.Occupancy(snapshot, rules, owner, policies, includeUnits: false);
            deploymentOccupancy.Add(key, occupancy);
        }
        return MilitaryRouteQuery.DeploymentPosition(travel, rules.BuildingArea(place), occupancy);
    }
}
