using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum ArrivalEstimateStatus { Known, NotApplicable, Waiting, Unreachable, Incomplete }
public sealed record ArrivalEstimate(ArrivalEstimateStatus Status, int? Days = null);

/// <summary>Navigation inputs for a prospective deployment; no command or world identity is allocated.</summary>
public sealed record DeploymentArrivalRequest(int SettlementId, int FormationId, int? NavalFormationId,
    MilitaryOrder Order, UnitPolicies Policies, int DepartureDelayDays = 0, WorldPosition? Waypoint = null, uint Soldiers = 1);

/// <summary>Predicts navigation against a frozen world without publishing paths or consuming simulation randomness.</summary>
public sealed class MilitaryArrivalForecast(WorldSnapshot snapshot, MilitaryRulebook rules,
    IReadOnlyDictionary<int, int>? regionCities = null, TreasureRulebook? treasures = null)
{
    private WorldQuery? deploymentWorld;

    public ArrivalEstimate Estimate(int unitId, int maximumDays = 4096, int maximumExpanded = 100000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDays);
        var work = new NavigationWorkBudget(maximumExpanded);
        var unit = snapshot.Units.FirstOrDefault(u => u.Id == unitId);
        if (unit is not { FormationId: int formation, EffectiveOrder: not null } || !rules.Formations.ContainsKey(formation)) return new(ArrivalEstimateStatus.NotApplicable);
        return Estimate(unit, maximumDays, work);
    }

    public ArrivalEstimate EstimateDeployment(DeploymentArrivalRequest request, int maximumDays = 4096, int maximumExpanded = 100000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDays);
        var work = new NavigationWorkBudget(maximumExpanded);
        var home = snapshot.Settlements.FirstOrDefault(s => s.Id == request.SettlementId);
        if (home is not { OwnerId: int owner, CanHostPersons: true, IsDestroyed: false, IsDormant: false } || home.Durability == 0 ||
            request.DepartureDelayDays is < 0 or > 9 || request.Policies is null || request.Order is null ||
            !request.Order.IsValid || !rules.Formations.ContainsKey(request.FormationId) || !rules.AllowsOrder(request.FormationId,request.Order))
            return new(ArrivalEstimateStatus.NotApplicable);
        int? naval = request.NavalFormationId ?? rules.NavalFormationForDeployment(request.FormationId);
        if (!rules.ValidUnitFormations(request.FormationId, naval) || !rules.TryPlace(home, out var place))
            return new(ArrivalEstimateStatus.NotApplicable);
        if (request.Order.Construction is { } plan && (regionCities is null ||
            !rules.Facilities.TryGetValue(plan.FacilityTypeId, out var facility) ||
            !rules.ConstructionSites.TryGetValue((plan.RegionId, plan.Position), out var site) ||
            !ConstructionPlacement.IsAvailable(owner, site, facility, rules, regionCities, snapshot.Settlements, snapshot.Units)))
            return new(ArrivalEstimateStatus.NotApplicable);
        deploymentWorld ??= new WorldQuery(snapshot);
        var occupancy = deploymentWorld.MovementOccupancy(rules, owner, request.Policies, null, null);
        var travel = rules.Travel(request.FormationId, naval, request.Soldiers);
        var start = MilitaryRouteQuery.DeploymentPosition(travel, rules.BuildingArea(place), occupancy);
        if (start is null) return new(ArrivalEstimateStatus.Unreachable);
        if (request.Order.Construction is { } construction && travel.AreaAt(start.Value)
            .Overlaps(rules.FacilityArea(construction.Position, construction.FacilityTypeId)))
            return new(ArrivalEstimateStatus.NotApplicable);
        // Use the next deployment identity for its deterministic navigation seed without allocating it.
        // The predicted unit is added only to its private query world.
        if (snapshot.Simulation.NextUnitId > int.MaxValue) return new(ArrivalEstimateStatus.NotApplicable);
        var unit = new UnitState((int)snapshot.Simulation.NextUnitId, owner, start.Value, request.Soldiers)
        {
            FormationId = request.FormationId, NavalFormationId = naval, Order = request.Order, Policies = request.Policies,
            DepartureDaysRemaining = request.DepartureDelayDays, Waypoint = request.Waypoint, ServicePlaceId = home.Id,
            Morale = home.Morale
        };
        // The construction site was checked above. This synthetic unit has no serving members in the
        // frozen world; ongoing-construction validation would incorrectly require a deployed serving leader.
        return Estimate(unit, maximumDays, work, constructionPreview: request.Order.Construction is not null);
    }

    public ArrivalEstimate EstimateOrder(int unitId, MilitaryOrder order, UnitPolicies policies, WorldPosition? waypoint,
        int maximumDays = 4096, int maximumExpanded = 100000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDays);
        var unit = snapshot.Units.FirstOrDefault(u => u.Id == unitId);
        if (unit is not { FormationId: int formation } || !rules.Formations.ContainsKey(formation))
            return new(ArrivalEstimateStatus.NotApplicable);
        return Estimate(unit.WithMilitaryOrder(order, policies, waypoint), maximumDays, new(maximumExpanded));
    }

    private ArrivalEstimate Estimate(UnitState unit, int maximumDays, NavigationWorkBudget work, bool constructionPreview = false)
    {
        var world = new WorldQuery(snapshot);
        world.SetForecastUnit(unit);
        if (!constructionPreview && !Valid(world, unit)) return new(ArrivalEstimateStatus.NotApplicable);
        if (unit.DepartureDaysRemaining == 0 && MilitaryNavigation.Advance(world, unit, unit.Id, rules, 0).Arrived) return new(ArrivalEstimateStatus.Known, 0);
        var movementDate = snapshot.Clock;
        for (int day = 1; day <= maximumDays; day++)
        {
            // ResolutionPipeline advances the calendar before DailyMapUpdate, including waiting days.
            movementDate = movementDate.NextDay();
            bool preparing = unit.DepartureDaysRemaining > 0;
            bool immobilized = unit.Condition == UnitCondition.Confused;
            if (!preparing && !constructionPreview && !Valid(world, unit)) return new(ArrivalEstimateStatus.NotApplicable);
            if (immobilized) unit = unit with { MovementRemainder = 0 };
            world.SetForecastUnit(unit);
            if (!preparing && !immobilized)
            {
                var step = MilitaryNavigation.Advance(world, unit, unit.Id, rules, MilitaryNavigation.Budget(unit, rules), work, movementDate: movementDate);
                if (work.Exhausted || step.Status == NavigationStatus.Planning) return new(ArrivalEstimateStatus.Incomplete);
                if (step.Arrived) return new(ArrivalEstimateStatus.Known, day);
                if (step.Status == NavigationStatus.Unreachable) return new(ArrivalEstimateStatus.Unreachable);
                // Daily randomness can change a blocked route; an idle move is not a fixed point.
                unit = MilitaryNavigation.Apply(unit, step);
                var contacted = MilitaryContacts.AfterMovement(world, unit, step.Navigation.Collision, rules);
                if (contacted.EffectiveOrder != unit.EffectiveOrder) return new(ArrivalEstimateStatus.Incomplete);
                unit = contacted;
                world.SetForecastUnit(unit);
            }
            // The common daily phase expires conditions and departure delays after movement, before combat.
            var previousOrder = unit.EffectiveOrder;
            unit = MilitaryUnitTransitions.Apply(unit, MilitaryConditions.Tick(unit));
            if (unit.EffectiveOrder != previousOrder) return new(ArrivalEstimateStatus.Incomplete);
            if (preparing) unit = unit with { DepartureDaysRemaining = unit.DepartureDaysRemaining - 1 };
            world.SetForecastUnit(unit);
            unit = MilitaryEngagements.ForecastNextCombat(world, unit, rules);
        }
        return new(ArrivalEstimateStatus.Incomplete);
    }

    private bool Valid(IWorldQuery world, UnitState unit) => unit.EffectiveOrder is { } order &&
        (order.Construction is not null
            ? regionCities is not null && treasures is not null && FacilityConstruction.CanContinue(world, unit, rules, regionCities, treasures)
            : MilitaryMovement.ValidOrder(world, unit.OwnerId, order, rules.Formations[unit.FormationId!.Value], rules, unit.NavalFormationId, unit.Soldiers));
}
