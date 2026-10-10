using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum ArrivalEstimateStatus { Known, NotApplicable, Waiting, Unreachable, Incomplete }
public sealed record ArrivalEstimate(ArrivalEstimateStatus Status, int? Days = null);

/// <summary>Navigation inputs for a prospective deployment; no command or world identity is allocated.</summary>
public sealed record DeploymentArrivalRequest(int SettlementId, int FormationId, int? NavalFormationId,
    MilitaryOrder Order, UnitPolicies Policies, int DepartureDelayDays = 0, WorldPosition? Waypoint = null);

/// <summary>Predicts navigation against a frozen world, without publishing forecast paths or consuming randomness.</summary>
public sealed class MilitaryArrivalForecast(WorldSnapshot snapshot, MilitaryRulebook rules,
    IReadOnlyDictionary<int, int>? regionCities = null, TreasureRulebook? treasures = null)
{
    private WorldQuery? world;

    public ArrivalEstimate Estimate(int unitId, int maximumDays = 4096, int maximumExpanded = 100000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDays);
        var work = new NavigationWorkBudget(maximumExpanded);
        var unit = snapshot.Units.FirstOrDefault(u => u.Id == unitId);
        if (unit is not { FormationId: int formation, EffectiveOrder: not null } || !rules.Formations.ContainsKey(formation)) return new(ArrivalEstimateStatus.NotApplicable);
        return Estimate(unit, unit.Id, maximumDays, work);
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
        world ??= new WorldQuery(snapshot);
        var occupancy = world.MovementOccupancy(rules, owner, request.Policies, null, null);
        var travel = rules.Travel(request.FormationId, naval);
        var start = MilitaryRouteQuery.DeploymentPosition(travel, rules.BuildingArea(place), occupancy);
        if (start is null) return new(ArrivalEstimateStatus.Unreachable);
        // This value only carries navigation state. Null movingUnit below excludes no real world object,
        // even when the world already contains unit zero; nothing is added to the world or allocated.
        var unit = new UnitState(0, owner, start.Value, 1)
        {
            FormationId = request.FormationId, NavalFormationId = naval, Order = request.Order, Policies = request.Policies,
            DepartureDaysRemaining = request.DepartureDelayDays, Waypoint = request.Waypoint, ServicePlaceId = home.Id
        };
        // The construction site was checked above. This synthetic unit has no serving members in the
        // frozen world; ongoing-construction validation would incorrectly require the leader to belong to unit zero.
        return Estimate(unit, null, maximumDays, work, constructionPreview: request.Order.Construction is not null);
    }

    public ArrivalEstimate EstimateOrder(int unitId, MilitaryOrder order, UnitPolicies policies, WorldPosition? waypoint,
        int maximumDays = 4096, int maximumExpanded = 100000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDays);
        var unit = snapshot.Units.FirstOrDefault(u => u.Id == unitId);
        if (unit is not { FormationId: int formation } || !rules.Formations.ContainsKey(formation))
            return new(ArrivalEstimateStatus.NotApplicable);
        return Estimate(unit with { Order = order, Policies = policies, Waypoint = waypoint, DepartureDaysRemaining = 0,
            EngagementTargetId = null, AwaitingPostAction = false, Lookahead = null }, unit.Id, maximumDays, new(maximumExpanded));
    }

    private ArrivalEstimate Estimate(UnitState unit, int? movingUnit, int maximumDays, NavigationWorkBudget work, bool constructionPreview = false)
    {
        world ??= new WorldQuery(snapshot);
        if (!constructionPreview && !Valid(unit)) return new(ArrivalEstimateStatus.NotApplicable);
        if (unit.DepartureDaysRemaining == 0 && MilitaryNavigation.Advance(world, unit, movingUnit, rules, 0).Arrived) return new(ArrivalEstimateStatus.Known, 0);
        for (int day = 1; day <= maximumDays; day++)
        {
            // BeforeDailyCommands expires conditions before that day's map movement.
            if (day > 1 || snapshot.Simulation.PendingDailyTask is not { Phase: TaskResolutionPhase.DailyCommands })
            {
                int remaining = Math.Max(0, unit.ConditionDaysRemaining - 1);
                var previousOrder = unit.EffectiveOrder;
                unit = unit with { ConditionDaysRemaining = remaining,
                    Condition = remaining == 0 ? UnitCondition.Normal : unit.Condition };
                // Do not label arrival at a different future order as arrival at the displayed target.
                if (unit.EffectiveOrder != previousOrder) return new(ArrivalEstimateStatus.Incomplete);
            }
            if (unit.DepartureDaysRemaining > 0)
            { unit = unit with { DepartureDaysRemaining = unit.DepartureDaysRemaining - 1 }; continue; }
            if (!constructionPreview && !Valid(unit)) return new(ArrivalEstimateStatus.NotApplicable);
            if (unit.Condition == UnitCondition.Confused ||
                unit.Condition == UnitCondition.Routed && unit.EffectiveOrder!.Kind == MilitaryOrderKind.Construct)
            {
                unit = unit with { MovementRemainder = 0 };
                continue;
            }
            var step = MilitaryNavigation.Advance(world, unit, movingUnit, rules, MilitaryNavigation.Budget(unit, rules), work);
            if (work.Exhausted || step.Status == NavigationStatus.Planning) return new(ArrivalEstimateStatus.Incomplete);
            if (step.Arrived) return new(ArrivalEstimateStatus.Known, day);
            if (step.Status == NavigationStatus.Unreachable) return new(ArrivalEstimateStatus.Unreachable);
            // Search limits and transient obstructions are not proof of no route.
            if (step.Status == NavigationStatus.Waiting && step.Remainder == 0 && step.Spent == 0)
                return new(ArrivalEstimateStatus.Waiting);
            unit = MilitaryNavigation.Apply(unit, step);
        }
        return new(ArrivalEstimateStatus.Incomplete);
    }

    private bool Valid(UnitState unit) => unit.EffectiveOrder is { } order &&
        (order.Construction is not null
            ? regionCities is not null && treasures is not null && FacilityConstruction.CanContinue(world!, unit, rules, regionCities, treasures)
            : MilitaryMovement.ValidOrder(world!, unit.OwnerId, order, rules.Formations[unit.FormationId!.Value], rules, unit.NavalFormationId));
}
