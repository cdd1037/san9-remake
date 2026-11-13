using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum ArrivalEstimateStatus { Known, NotApplicable, Waiting, Unreachable, Incomplete }
public sealed record ArrivalEstimate(ArrivalEstimateStatus Status, int? Days = null);

/// <summary>Navigation inputs for a prospective deployment; no command or world identity is allocated.</summary>
public sealed record DeploymentArrivalRequest(int SettlementId, int FormationId, int? NavalFormationId,
    MilitaryOrder Order, UnitPolicies Policies, int DepartureDelayDays = 0, WorldPosition? Waypoint = null, uint Soldiers = 1)
{
    public int? SiegeEquipmentId { get; init; }
}

/// <summary>Estimates current-route travel time without simulating future movement, combat or command changes.</summary>
public sealed class MilitaryArrivalForecast(WorldSnapshot snapshot, MilitaryRulebook rules,
    IReadOnlyDictionary<int, int>? regionCities = null, TreasureRulebook? treasures = null)
{
    private WorldQuery? deploymentWorld;

    public ArrivalEstimate Estimate(int unitId, int maximumExpanded = 100000)
    {
        var work = new NavigationWorkBudget(maximumExpanded);
        var unit = snapshot.Units.FirstOrDefault(u => u.Id == unitId);
        if (unit is not { FormationId: int formation, EffectiveOrder: not null } || !rules.Formations.ContainsKey(formation)) return new(ArrivalEstimateStatus.NotApplicable);
        return Estimate(unit, work);
    }

    public ArrivalEstimate EstimateDeployment(DeploymentArrivalRequest request, int maximumExpanded = 100000)
    {
        var work = new NavigationWorkBudget(maximumExpanded);
        var home = snapshot.Settlements.FirstOrDefault(s => s.Id == request.SettlementId);
        if (home is not { OwnerId: int owner, CanHostPersons: true, IsDestroyed: false, IsDormant: false } || home.Durability == 0 ||
            request.DepartureDelayDays is < 0 or > 9 || request.Policies is null || request.Order is null ||
            !request.Order.IsValid || !rules.Formations.ContainsKey(request.FormationId) || !rules.AllowsOrder(request.FormationId,request.Order) ||
            request.SiegeEquipmentId is int siege && !rules.SiegeEquipment.ContainsKey(siege))
            return new(ArrivalEstimateStatus.NotApplicable);
        int? naval = request.NavalFormationId ?? rules.NavalFormationForDeployment(request.FormationId);
        if (!rules.ValidUnitFormations(request.FormationId, naval) || !rules.TryPlace(home, out var place))
            return new(ArrivalEstimateStatus.NotApplicable);
        if (request.Order.Construction is { } plan && (regionCities is null ||
            !rules.Facilities.TryGetValue(plan.FacilityTypeId, out var facility) ||
            !rules.ConstructionSites.TryGetValue((plan.RegionId, plan.Position), out var site) ||
            !ConstructionPlacement.IsAvailable(site, facility, rules, regionCities, snapshot.Settlements, snapshot.Units)))
            return new(ArrivalEstimateStatus.NotApplicable);
        deploymentWorld ??= new WorldQuery(snapshot);
        var occupancy = deploymentWorld.MovementOccupancy(rules, owner, request.Policies, null, null);
        var travel = rules.Travel(request.FormationId, naval, request.Soldiers);
        var start = MilitaryRouteQuery.DeploymentPosition(travel, rules.BuildingArea(place), occupancy);
        if (start is null) return new(ArrivalEstimateStatus.Unreachable);
        if (request.Order.Construction is { } construction && travel.AreaAt(start.Value)
            .Overlaps(rules.FacilityArea(construction.Position, construction.FacilityTypeId)))
            return new(ArrivalEstimateStatus.NotApplicable);
        // Exclude only the prospective identity from occupancy, preserving every existing unit.
        if (snapshot.Simulation.NextUnitId > int.MaxValue) return new(ArrivalEstimateStatus.NotApplicable);
        var unit = new UnitState((int)snapshot.Simulation.NextUnitId, owner, start.Value, request.Soldiers)
        {
            FormationId = request.FormationId, NavalFormationId = naval, Order = request.Order, Policies = request.Policies,
            SiegeEquipmentId = request.SiegeEquipmentId,
            DepartureDaysRemaining = request.DepartureDelayDays, Waypoint = request.Waypoint, ServicePlaceId = home.Id,
            Morale = home.Morale
        };
        // The construction site was checked above. This synthetic unit has no serving members in the
        // frozen world; ongoing-construction validation would incorrectly require a deployed serving leader.
        return Estimate(unit, work, constructionPreview: request.Order.Construction is not null);
    }

    public ArrivalEstimate EstimateOrder(int unitId, MilitaryOrder order, UnitPolicies policies, WorldPosition? waypoint,
        int maximumExpanded = 100000)
    {
        var unit = snapshot.Units.FirstOrDefault(u => u.Id == unitId);
        if (unit is not { FormationId: int formation } || !rules.Formations.ContainsKey(formation))
            return new(ArrivalEstimateStatus.NotApplicable);
        return Estimate(unit.WithMilitaryOrder(order, policies, waypoint), new(maximumExpanded));
    }

    private ArrivalEstimate Estimate(UnitState unit, NavigationWorkBudget work, bool constructionPreview = false)
    {
        var world = new WorldQuery(snapshot);
        world.SetForecastUnit(unit);
        if (!constructionPreview && !Valid(world, unit)) return new(ArrivalEstimateStatus.NotApplicable);
        var route = MilitaryRouteQuery.Estimate(world, rules, unit, unit.Id, work);
        if (route.Status == RouteReachability.Incomplete) return new(ArrivalEstimateStatus.Incomplete);
        if (route.Status == RouteReachability.Unreachable) return new(ArrivalEstimateStatus.Unreachable);
        var travel = rules.Travel(unit);
        long cost = Math.Max(0, route.Cost - unit.MovementRemainder);
        int days = (int)Math.Min(int.MaxValue, unit.DepartureDaysRemaining + (cost + travel.DailyBudget - 1) / travel.DailyBudget);
        return new(ArrivalEstimateStatus.Known, days);
    }

    private bool Valid(IWorldQuery world, UnitState unit) => unit.EffectiveOrder is { } order &&
        (order.Construction is not null
            ? regionCities is not null && treasures is not null && FacilityConstruction.CanContinue(world, unit, rules, regionCities, treasures)
            : MilitaryMovement.ValidOrder(world, unit.OwnerId, order, rules.Formations[unit.FormationId!.Value], rules, unit.NavalFormationId, unit.Soldiers, unit.SiegeEquipmentId));
}
