using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static partial class MilitaryPolicies
{
    internal static void ResolveRetreat(WorldMutation world, MilitaryRulebook rules, List<DomainEvent> events)
    {
        foreach (int id in world.OrderedUnits.Select(unit => unit.Id).ToArray())
        {
            var unit = world.Units[id];
            if (unit.IsEmbedded || unit.IsPersonnel || unit.FormationId is null || unit.Order is null ||
                unit.AutonomousReturn || unit.Condition is not (UnitCondition.Normal or UnitCondition.HighSpirits) ||
                !MilitaryOrders.HasReturnObject(world, unit) || Commander(world, unit) is not { } leader) continue;
            if (unit.Order.Kind is MilitaryOrderKind.Return or MilitaryOrderKind.Transport &&
                unit.EngagementTargetId is null && unit.ContactTarget is null)
                continue;
            long opposingSoldiers = UnitPolicyQueries.Combatants(world, rules)
                .Where(source => (!source.IsSettlement || world.Settlements[source.Id] is { OwnerId: not null, CanHostPersons: true } &&
                    !MilitaryDiplomacy.Work(world, source.Id, rules)) &&
                    IsThreat(world, source, unit, rules) && Threatens(world, source, unit, rules))
                .Sum(source => (long)source.Soldiers(world));
            if (!UnitPolicyDecisions.Retreat(unit.Policies.AutonomousRetreat, leader.Personality, unit.Morale,
                    unit.Soldiers, opposingSoldiers)) continue;
            var response = unit.Policies.AutonomousRetreat
                ? unit.Morale < 4 * leader.Personality + 25 ? UnitPolicyResponse.RetreatOrdered : UnitPolicyResponse.RetreatOutnumbered
                : unit.Morale < 4 * (leader.Personality - 2) + 25 ? UnitPolicyResponse.RetreatLowMorale : UnitPolicyResponse.RetreatDespiteOrders;
            ReturnAndRespond(world, unit, rules, response, null, events, "autonomous-retreat");
        }
    }

    private static bool IsThreat(WorldMutation world, BattleSource source, UnitState unit, MilitaryRulebook rules)
    {
        int owner = source.Owner(world);
        if (owner == unit.OwnerId) return false;
        if (MilitaryDiplomacy.AutomaticEnemy(world, unit.OwnerId, owner)) return true;
        var engagements = world.Engagements(rules);
        var subject = new BattleSource(unit.Id, false);
        return engagements.Target(source) == subject || engagements.Target(subject) == source;
    }

    private static bool Threatens(WorldMutation world, BattleSource source, UnitState target, MilitaryRulebook rules)
    {
        if (source.Soldiers(world) == 0) return false;
        int range = source.IsSettlement
            ? MilitaryCombat.FacilityRange(world, world.Settlements[source.Id], target, rules)
            : MilitaryCombat.UnitRange(world, world.Units[source.Id], Attack(target), rules);
        return range > 0 && source.Area(world, rules).IntersectsExpanded(MilitaryCombat.Area(target, rules), range);
    }
}
