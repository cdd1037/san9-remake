using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static partial class MilitaryPolicies
{
    // Starting an interception and continuing one use the same rule for a retreating target.
    private static bool MayPursue(WorldMutation world, UnitState unit, UnitState target, int personality,
        MilitaryRulebook rules)
    {
        bool primary = unit.Order?.UnitId == target.Id;
        return unit.Policies.Pursue ? primary || personality == 0 :
            primary && personality == 0 && !UnitPolicyQueries.NearbyEnemies(world, unit, rules, rules.PolicyRadius)
                .Any(other => other.IsSettlement || other.Id != target.Id);
    }

    internal static void ResolvePursuit(WorldMutation world, MilitaryRulebook rules, List<DomainEvent> events)
    {
        foreach (int id in world.OrderedUnits.Select(u => u.Id).ToArray())
        {
            var unit = world.Units[id];
            if (unit.IsEmbedded || unit.IsPersonnel || unit.FormationId is null || unit.AutonomousReturn ||
                unit.Condition is not (UnitCondition.Normal or UnitCondition.HighSpirits) ||
                rules.Purpose(unit.FormationId.Value) != UnitPurpose.Combat || unit.Order is null ||
                unit.Order.Kind is MilitaryOrderKind.Construct or MilitaryOrderKind.Transport ||
                unit.EffectiveOrder?.UnitId is not int targetId || !world.Units.TryGetValue(targetId, out var target) ||
                !target.PursuitResponsePending || !target.AutonomousReturn ||
                Commander(world, unit) is not { } leader) continue;
            bool primary = unit.Order.UnitId == targetId;
            if (MayPursue(world, unit, target, leader.Personality, rules))
            {
                Respond(world, unit, unit.Policies.Pursue ? UnitPolicyResponse.PursueOrdered :
                    UnitPolicyResponse.PursueDespiteOrders, target.CommanderId, events);
            }
            else if (!primary)
            {
                Engage(world, unit, null, events, "pursuit-resume");
                Respond(world, unit, UnitPolicyResponse.ResumeOrders, target.CommanderId, events);
            }
            else
            {
                ReturnAndRespond(world, unit, rules, UnitPolicyResponse.ReturnHome, target.CommanderId,
                    events, "pursuit-return");
            }
        }
    }
}
