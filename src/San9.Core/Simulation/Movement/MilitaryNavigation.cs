using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Shared movement target, occupancy and budget rules for execution and read-only forecasts.</summary>
internal static class MilitaryNavigation
{
    internal static NavigationStep Advance(WorldMutation world, UnitState unit, MilitaryRulebook rules,
        int budget, NavigationWorkBudget? work = null)
    {
        var order = unit.EffectiveOrder!;
        var formation = rules.Formations[unit.FormationId!.Value];
        if (order.IsAttack)
            return rules.Map.Navigate(unit.Position, MilitaryCombat.TargetArea(world, order, rules), true,
                formation.Footprint, formation.TravelMode,
                area => world.MovementOccupancy(rules, unit.Id, null, area), budget, unit.MovementPlan,
                range: position => MilitaryCombat.ApproachRange(world, unit, order, rules, position), rectangularReach: true,
                maximumRange: Math.Max(MapCoordinates.UnitsPerCell,
                    checked((formation.Attack?.Range ?? 0) + (formation.Attack?.MaximumElevationBonus ?? 0))), work: work);
        var target = order.Construction is { } construction
            ? new MapOccupancy(construction.Position, rules.Facilities[construction.FacilityTypeId].Footprint)
            : order.SettlementId is int destination
                ? new MapOccupancy(rules.Place(world.Settlements[destination]).Position, rules.Place(world.Settlements[destination]).Footprint)
                : new MapOccupancy(order.Position!.Value, new(1, 1));
        return rules.Map.Navigate(unit.Position, target, order.Construction is not null || order.SettlementId is not null,
            formation.Footprint, formation.TravelMode,
            area => world.MovementOccupancy(rules, unit.Id, order.Construction?.RegionId, area), budget, unit.MovementPlan, work: work);
    }

    internal static int Budget(UnitState unit, MilitaryRulebook rules) =>
        checked(rules.Formations[unit.FormationId!.Value].MovementBudget + unit.MovementRemainder);

    internal static UnitState Apply(UnitState unit, NavigationStep step) => unit with
    {
        Position = step.Position, MovementPlan = step.Plan, MovementRemainder = step.Remainder
    };
}
