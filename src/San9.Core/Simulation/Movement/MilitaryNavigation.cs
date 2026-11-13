using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Shared movement target, occupancy and budget rules for execution and read-only forecasts.</summary>
internal static class MilitaryNavigation
{
    internal sealed record UnitStep(NavigationStep Navigation, bool WaypointReached = false)
    {
        public WorldPosition Position => Navigation.Position;
        public int Spent => Navigation.Spent;
        public int Remainder => Navigation.Remainder;
        public NavigationStatus Status => Navigation.Status;
        public bool Arrived => Navigation.Arrived;
    }

    internal static UnitStep Advance(IWorldQuery world, UnitState unit, int? movingUnit, MilitaryRulebook rules,
        int budget, NavigationWorkBudget? work = null, GameClock? movementDate = null)
    {
        ulong seed = StableRandom.DeriveSeed(world.Simulation.InitialSeed,
            FormattableString.Invariant($"navigation:{(movementDate ?? world.Clock).DayNumber}:{unit.Id}"));
        if (unit.ActiveWaypoint is not { } via)
            return new(AdvanceDestination(world, unit, movingUnit, rules, budget, work, seed));
        var travel = rules.Travel(unit);
        var first = travel.Map.Navigate(unit.Position, new(via, new(1, 1)), NavigationArrival.Point,
            travel.Shape, travel.Mode, world.MovementOccupancy(rules, unit.OwnerId, unit.Policies, movingUnit, null, unit),
            budget, work: work, lookahead: unit.Lookahead, randomSeed: seed, mobility: rules.Formation(unit).MovementBudget / 5,
            excluded: unit.EffectiveOrder?.SettlementId is int destination ? rules.SettlementArea(world.Settlements[destination]) : null);
        if (!first.Arrived) return new(first);
        var next = AdvanceDestination(world, unit with { Position = first.Position, Waypoint = null, Lookahead = null },
            movingUnit, rules, Math.Max(0, budget - first.Spent), work, seed ^ 0x9E3779B97F4A7C15UL);
        return new(next with { Spent = first.Spent + next.Spent, Steps = first.Steps.AddRange(next.Steps) }, true);
    }

    private static NavigationStep AdvanceDestination(IWorldQuery world, UnitState unit, int? movingUnit, MilitaryRulebook rules,
        int budget, NavigationWorkBudget? work, ulong seed)
    {
        var order = unit.EffectiveOrder!;
        var formation = rules.Formations[unit.FormationId!.Value];
        var naval = unit.NavalFormationId is int id ? rules.Formations[id] : null;
        int Range(FormationRule? f) => f?.Attack is { } attack && (attack.RangedSiege || attack.RequiresWaterContact)
            ? Math.Max(1, checked(attack.Range + attack.MaximumElevationBonus)) : 1;
        var travel = rules.Travel(unit);
        if (order.IsAttack)
            return NavigationArrivalForecast.Advance(travel, unit.Position, MilitaryCombat.TargetArea(world, order, rules), NavigationArrival.Range,
                world.MovementOccupancy(rules, unit.OwnerId, unit.Policies, movingUnit, null, unit), budget, work, unit.Lookahead,
                range: position => MilitaryCombat.ApproachRange(world, unit, order, rules, position), rectangularReach: true,
                maximumRange: Math.Max(Range(rules.AttackFormation(unit, againstSettlement: order.SettlementId is not null)), Range(naval)),
                lookaheadCells: order.UnitId is null ? 25 : 6, randomSeed: seed, continueApproach: true, mobility: rules.Formation(unit).MovementBudget / 5,
                requireTargetClearance: formation.Attack?.RequiresWaterContact == true || naval?.Attack?.RequiresWaterContact == true);
        var target = order.Construction is { } construction
            ? rules.FacilityArea(construction.Position, construction.FacilityTypeId)
            : order.SettlementId is int destination
                ? rules.SettlementArea(world.Settlements[destination])
                : new MapOccupancy(order.Position!.Value, new(1, 1));
        return NavigationArrivalForecast.Advance(travel, unit.Position, target, Arrival(order),
            world.MovementOccupancy(rules, unit.OwnerId, unit.Policies, movingUnit, order.Construction?.RegionId, unit), budget, work, unit.Lookahead, randomSeed: seed, mobility: rules.Formation(unit).MovementBudget / 5,
            requireTargetClearance: order.Construction is not null);
    }

    internal static NavigationArrival Arrival(MilitaryOrder order) => order.IsAttack || order.Construction is not null
        ? NavigationArrival.Range : order.SettlementId is not null ? NavigationArrival.Region : NavigationArrival.Point;

    internal static int Budget(UnitState unit, MilitaryRulebook rules)
    {
        int daily = rules.Travel(unit).DailyBudget;
        bool engaged = unit.ActiveEngagement || unit.PassiveEngagement;
        return checked((engaged ? daily / 2 : daily) + unit.MovementRemainder);
    }

    internal static UnitState Apply(UnitState unit, UnitStep step) => unit with
    {
        Position = step.Position, MovementRemainder = step.Remainder,
        Facing = WorldDirections.Between(unit.Position, step.Position) ?? unit.Facing,
        Lookahead = step.Navigation.Lookahead,
        Waypoint = step.WaypointReached ? null : unit.Waypoint
    };

    internal static UnitState Commit(WorldMutation world, UnitState unit, UnitStep step, MilitaryRulebook rules, List<DomainEvent> events)
    {
        if (step.WaypointReached)
        {
            var waypoint = unit.Waypoint!.Value;
            events.Add(new UnitWaypointReachedEvent(world.Clock,
                BattleObjectContext.Capture(unit with { Position = waypoint }), waypoint));
        }
        var moved = world.AdvanceNavigation(unit.Id, step);
        var contacted = MilitaryContacts.AfterMovement(world, moved, step.Navigation.Collision, rules);
        if (contacted != moved) { world.Replace(contacted); moved = world.Units[unit.Id]; }
        events.Add(new UnitMovedEvent(world.Clock, unit.Id, unit.Position, step.Position, step.Spent, step.Status)
            { Steps = step.Navigation.Steps });
        return moved;
    }
}
