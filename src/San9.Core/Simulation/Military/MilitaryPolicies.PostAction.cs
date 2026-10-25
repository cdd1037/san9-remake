using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static partial class MilitaryPolicies
{
    private const int MaximumPostActionTravelDays = 8;

    internal static void ResolvePostAction(WorldMutation world, UnitState unit, MilitaryRulebook rules, List<DomainEvent> events)
    {
        if (!unit.AwaitingPostAction || unit.IsEmbedded || unit.IsPersonnel || unit.FormationId is null ||
            unit.Condition is not (UnitCondition.Normal or UnitCondition.HighSpirits)) return;
        if (Commander(world, unit) is not { } leader)
        {
            MilitaryOrders.ReturnAfterTask(world, unit, rules, events, "post-action-return");
            return;
        }
        var route = new MilitaryRouteQueryContext(world.CapturePresentation(), rules);
        var area = MilitaryCombat.Area(unit, rules);
        var candidate = UnitPolicyQueries.ActiveFieldUnits(world)
            .Where(other => other.Id != unit.Id && MilitaryDiplomacy.AutomaticEnemy(world, unit.OwnerId, other.OwnerId) &&
                (!other.AutonomousReturn || MayPursue(world, unit, other, leader.Personality, rules)) &&
                area.IntersectsExpanded(MilitaryCombat.Area(other, rules), rules.PolicyRadius) &&
                MilitaryOrders.ValidMission(world, unit, Attack(other), rules))
            .Select(other => (Unit: other, Distance: area.DistanceTo(MilitaryCombat.Area(other, rules))))
            .OrderBy(item => item.Distance)
            .FirstOrDefault(item => route.Estimate(unit.WithMilitaryOrder(Attack(item.Unit), unit.Policies, null), unit.Id)
                is { Status: RouteReachability.Reachable, Days: <= MaximumPostActionTravelDays }).Unit;
        bool attack = candidate is not null && UnitPolicyDecisions.Attack(unit.Policies.AttackAfterAction,
            leader.Personality, UnitPolicyQueries.StrengthRatio(world, unit, candidate));
        var response = candidate is null ? UnitPolicyResponse.PostActionNoTarget
            : unit.Policies.AttackAfterAction
                ? attack ? UnitPolicyResponse.PostActionAttackOrdered : UnitPolicyResponse.PostActionAvoidDespiteOrders
                : attack ? UnitPolicyResponse.PostActionAttackDespiteOrders : UnitPolicyResponse.PostActionReturnOrdered;
        if (attack)
        {
            MilitaryOrders.ChangeOrder(world, unit, Attack(candidate!), events, "post-action-attack");
            Respond(world, world.Units[unit.Id], response, candidate!.CommanderId, events);
        }
        else ReturnAndRespond(world, unit, rules, response, candidate?.CommanderId, events, "post-action-return");
    }
}
