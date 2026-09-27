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

    internal static UnitStep Advance(WorldMutation world, UnitState unit, int? movingUnit, MilitaryRulebook rules,
        int budget, NavigationWorkBudget? work = null)
    {
        if (unit.Waypoint is not { } via || unit.EngagementTargetId is not null || unit.Condition == UnitCondition.Disguised)
            return new(AdvanceDestination(world, unit, movingUnit, rules, budget, work));
        var travel = rules.Travel(unit);
        var first = travel.Map.Navigate(unit.Position, new(via, new(1, 1)), false,
            travel.Shape, travel.Mode, world.MovementOccupancy(rules, unit.OwnerId, unit.Policies, movingUnit, null),
            budget, work: work, lookahead: unit.Lookahead);
        if (!first.Arrived) return new(first);
        var next = AdvanceDestination(world, unit with { Position = first.Position, Waypoint = null, Lookahead = null },
            movingUnit, rules, Math.Max(0, budget - first.Spent), work);
        return new(next with { Spent = first.Spent + next.Spent, Steps = first.Steps.AddRange(next.Steps) }, true);
    }

    private static NavigationStep AdvanceDestination(WorldMutation world, UnitState unit, int? movingUnit, MilitaryRulebook rules,
        int budget, NavigationWorkBudget? work = null)
    {
        var order = unit.EffectiveOrder!;
        var formation = rules.Formations[unit.FormationId!.Value];
        var naval = unit.NavalFormationId is int id ? rules.Formations[id] : null;
        int Range(FormationRule? f) => checked((f?.Attack?.Range ?? 0) + (f?.Attack?.MaximumElevationBonus ?? 0));
        var travel = rules.Travel(unit);
        if (order.IsAttack)
            return travel.Map.Navigate(unit.Position, MilitaryCombat.TargetArea(world, order, rules), true,
                travel.Shape, travel.Mode,
                world.MovementOccupancy(rules, unit.OwnerId, unit.Policies, movingUnit, null), budget,
                range: position => MilitaryCombat.ApproachRange(world, unit, order, rules, position), rectangularReach: true,
                maximumRange: Math.Max(MapCoordinates.UnitsPerCell,
                    Math.Max(Range(formation), Range(naval))), work: work, lookahead: unit.Lookahead,
                lookaheadCells: order.UnitId is null ? 25 : 6);
        var target = order.Construction is { } construction
            ? rules.FacilityArea(construction.Position, construction.FacilityTypeId)
            : order.SettlementId is int destination
                ? rules.SettlementArea(world.Settlements[destination])
                : new MapOccupancy(order.Position!.Value, new(1, 1));
        if (order.Construction is null && order.SettlementId is int home && world.Settlements[home].OwnerId == unit.OwnerId &&
            new MapOccupancy(unit.Position, rules.Formation(unit).Footprint).Overlaps(target))
            return new(unit.Position, 0, NavigationStatus.Arrived);
        return travel.Map.Navigate(unit.Position, target, order.Construction is not null || order.SettlementId is not null,
            travel.Shape, travel.Mode,
            world.MovementOccupancy(rules, unit.OwnerId, unit.Policies, movingUnit, order.Construction?.RegionId), budget, work: work, lookahead: unit.Lookahead);
    }

    internal static int Budget(UnitState unit, MilitaryRulebook rules) =>
        checked(rules.Travel(unit).DailyBudget + unit.MovementRemainder);

    internal static UnitState Apply(UnitState unit, UnitStep step) => unit with
    {
        Position = step.Position, MovementRemainder = step.Remainder, Lookahead = step.Navigation.Lookahead,
        Waypoint = step.WaypointReached ? null : unit.Waypoint
    };
}
