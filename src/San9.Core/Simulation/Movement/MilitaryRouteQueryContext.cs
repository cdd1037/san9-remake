using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record MilitaryRouteEstimate(RouteReachability Status, int? Days, ImmutableArray<MapCell> Cells);

/// <summary>Sequential route queries sharing indexes for one immutable world and rulebook.</summary>
public sealed class MilitaryRouteQueryContext(WorldSnapshot snapshot, MilitaryRulebook rules)
{
    private WorldQuery? world;
    private readonly Dictionary<(int Owner, UnitPolicies Policies), IReadOnlyList<MapOccupancy>> deploymentOccupancy = [];

    public MilitaryRoutePlan Plan(UnitState unit, int? movingUnit, int maximumDays = 4096, int maximumExpanded = 100000) =>
        MilitaryRouteQuery.Plan(world ??= new(snapshot), rules, unit, movingUnit, maximumDays, maximumExpanded);

    /// <summary>One strategic search; frozen occupancy affects route selection, never adds to travel time.
    /// This estimate does not guarantee that daily movement will reach the destination on that day.</summary>
    public MilitaryRouteEstimate Estimate(UnitState unit, int? movingUnit, int maximumExpanded = 100000)
    {
        var query = world ??= new(snapshot);
        var travel = rules.Travel(unit);
        if (travel.DailyBudget <= 0 || unit.EffectiveOrder is not { } order)
            return new(RouteReachability.Unreachable, null, []);
        var target = order.IsAttack ? MilitaryCombat.TargetArea(query, order, rules) :
            order.Construction is { } construction ? rules.FacilityArea(construction.Position, construction.FacilityTypeId) :
            order.SettlementId is int home ? rules.SettlementArea(query.Settlements[home]) : new MapOccupancy(order.Position!.Value, new(1, 1));
        if (order.SettlementId is int destination && query.Settlements[destination].OwnerId == unit.OwnerId &&
            new MapOccupancy(unit.Position, rules.Formation(unit).Footprint).Overlaps(target))
            return new(RouteReachability.Reachable, 0, [MapCoordinates.Cell(unit.Position)]);
        var route = travel.Map.EstimateTravelCost(unit.Position, target, travel.Shape, travel.Mode,
            query.MovementOccupancy(rules, unit.OwnerId, unit.Policies, movingUnit, order.Construction?.RegionId),
            new NavigationWorkBudget(maximumExpanded), intoRange: order.IsAttack || order.SettlementId is not null || order.Construction is not null,
            includeCells: true);
        int? days = route.Status == RouteReachability.Reachable
            ? (int)Math.Min(int.MaxValue, (route.Cost + travel.DailyBudget - 1) / travel.DailyBudget) : null;
        return new(route.Status, days, route.Cells);
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
