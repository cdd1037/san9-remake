using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static partial class MilitaryPolicies
{
    internal static void ApproachDay(WorldMutation world, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        if (rules is null) return;
        foreach (int id in world.OrderedUnits.Select(u => u.Id).ToArray())
        {
            var unit = world.Units[id];
            if (unit.IsEmbedded || unit.IsPersonnel || unit.FormationId is null) continue;
            var visible = UnitPolicyQueries.VisibleForeignCombatants(world, unit, rules).ToArray();
            int? primaryOwner = MilitaryDiplomacy.TargetOwner(world, unit.Order);
            int? currentOwner = MilitaryDiplomacy.TargetOwner(world, unit.EffectiveOrder);
            var leader = Commander(world, unit);
            var candidates = visible.Where(actor => !actor.IsSettlement)
                .Select(actor => world.Units[actor.Id]).Where(other =>
                    MilitaryDiplomacy.AutomaticEnemy(world, unit.OwnerId, other.OwnerId) ||
                    other.OwnerId == primaryOwner || other.OwnerId == currentOwner)
                .Where(other => !other.AutonomousReturn || leader is not null &&
                    MayPursue(world, unit, other, leader.Personality, rules)).ToArray();
            bool notifyAvoidance = unit.ApproachNotificationPending;
            unit = unit with { ApproachNotificationPending = candidates.Length == 0 };
            world.Replace(unit);
            if (candidates.Length == 0 || unit.AutonomousReturn || unit.Order is null ||
                unit.Condition is not (UnitCondition.Normal or UnitCondition.HighSpirits) ||
                rules.Purpose(unit.FormationId.Value) != UnitPurpose.Combat ||
                unit.Order.Kind is MilitaryOrderKind.Construct or MilitaryOrderKind.Transport ||
                MilitaryEngagements.Engaged(world, unit, rules) || leader is null) continue;
            if (UnitPolicyQueries.Target(world, unit.Order, rules) is { } primary && visible.Contains(primary) ||
                UnitPolicyQueries.Target(world, unit.EffectiveOrder, rules) is { } current && visible.Contains(current)) continue;
            var candidate = candidates.MaxBy(other => CombatMath.PolicyTargetPriority(
                MilitaryCombat.Strength(world, other), Commander(world, other)?.Status == OfficerStatus.Ruler))!;
            if (UnitPolicyDecisions.Attack(unit.Policies.AttackApproachingEnemies, leader.Personality,
                    UnitPolicyQueries.StrengthRatio(world, unit, candidate)))
            {
                Engage(world, unit, candidate.Id, events, "enemy-approach");
                Respond(world, unit, unit.Policies.AttackApproachingEnemies ? UnitPolicyResponse.AttackOrdered :
                    UnitPolicyResponse.AttackDespiteOrders, candidate.CommanderId, events);
            }
            else if (notifyAvoidance)
                Respond(world, unit, unit.Policies.AttackApproachingEnemies ? UnitPolicyResponse.AvoidDespiteOrders :
                    UnitPolicyResponse.AvoidOrdered, candidate.CommanderId, events);
        }
    }
}
