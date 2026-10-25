using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SetUnitPoliciesCommand(Guid CommandId, int IssuerId, long Sequence, int UnitId, UnitPolicies Policies)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record UnitPoliciesChangedEvent(GameClock Clock, int UnitId, UnitPolicies Policies) : DomainEvent;
public sealed record UnitEngagementChangedEvent(GameClock Clock, int UnitId, int? TargetId, string Reason) : DomainEvent;

internal static partial class MilitaryPolicies
{
    internal static CommandRejection Eligibility(WorldMutation world, SetUnitPoliciesCommand command)
    {
        if (command.Policies is null || !world.Units.TryGetValue(command.UnitId, out var unit) ||
            unit.OwnerId != command.IssuerId || unit.IsEmbedded || unit.IsPersonnel || unit.FormationId is null)
            return CommandRejection.InvalidTask;
        return CommandRejection.None;
    }

    internal static CommandRejection Set(WorldMutation world, SetUnitPoliciesCommand command, List<DomainEvent> events)
    {
        var rejection = Eligibility(world, command);
        if (rejection != CommandRejection.None) return rejection;
        var unit = world.Units[command.UnitId];
        world.Replace(unit with { Policies = command.Policies, CommandedThisXun = true });
        events.Add(new UnitPoliciesChangedEvent(world.Clock, unit.Id, command.Policies));
        return CommandRejection.None;
    }

    internal static void AdvanceDay(WorldMutation world, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        if (rules is null) return;
        MilitaryOrders.ResolveOrders(world, rules, events);
        ResolveRetreat(world, rules, events);
        ResolvePursuit(world, rules, events);
    }

    internal static void FinishPursuitResponses(WorldMutation world)
    {
        foreach (var unit in world.OrderedUnits.Where(u => u.PursuitResponsePending).ToArray())
            world.Replace(unit with { PursuitResponsePending = false });
    }

    internal static void AfterTransfer(WorldMutation world, int id, MilitaryRulebook? rules, List<DomainEvent> events,
        int? excludedForce = null)
    {
        if (rules is null || !world.Units.TryGetValue(id, out var unit) || unit.IsEmbedded || unit.IsPersonnel ||
            unit.FormationId is null || unit.Order is null && !unit.AwaitingPostAction) return;
        unit = UnitServicePlaces.Repair(world, unit, rules);
        if (unit.Condition is not (UnitCondition.Normal or UnitCondition.HighSpirits))
        {
            var previous = unit.Condition;
            unit = unit with { Condition = UnitCondition.Normal, ConditionDaysRemaining = 0 };
            world.Replace(unit);
            events.Add(new BattleConditionChangedEvent(world.Clock, id, false, UnitCondition.Normal, 0)
                { PreviousCondition = previous, Target = BattleObjectContext.Capture(unit) });
        }
        if (world.Forces[unit.OwnerId].IsBarbarian || rules.Purpose(unit.FormationId!.Value) != UnitPurpose.Combat ||
            Commander(world, unit) is not { } leader || unit.Morale <= 5 * leader.Personality + 30)
        {
            MilitaryOrders.ReturnAfterTask(world, unit, rules, events, "transfer-return");
            return;
        }
        var candidate = world.Units.Values.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.DepartureDaysRemaining == 0 &&
                u.OwnerId != excludedForce && MilitaryDiplomacy.AutomaticEnemy(world, unit.OwnerId, u.OwnerId))
            .Select(u => Attack(u))
            .Concat(world.Settlements.Values.Where(s => (excludedForce is null || s.OwnerId != excludedForce) &&
                MilitaryDiplomacy.AutomaticEnemy(world, unit.OwnerId, s.OwnerId))
                .Select(s => new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, s.Id)))
            .Where(o => MilitaryOrders.ValidMission(world, unit, o, rules))
            .Select(o => (Order: o, Distance: Distance(unit, o, world, rules)))
            .Where(c => c.Distance <= rules.PolicyRadius)
            .OrderBy(c => c.Distance).ThenBy(c => c.Order.UnitId is null).ThenBy(c => c.Order.UnitId ?? c.Order.SettlementId)
            .Select(c => c.Order).FirstOrDefault();
        if (candidate is { } target)
        {
            var strength = target.UnitId is int other ? MilitaryCombat.Strength(world, world.Units[other])
                : MilitaryCombat.GarrisonStrength(world, world.Settlements[target.SettlementId!.Value]);
            long ratio = UnitPolicyQueries.StrengthRatio(world, unit, strength);
            if (ratio - 5 * leader.Personality + 5 > 100)
            {
                MilitaryOrders.ChangeOrder(world, unit, target, events, "transfer-attack");
                return;
            }
        }
        MilitaryOrders.ReturnAfterTask(world, unit, rules, events, "transfer-return");
    }

    private static OfficerState? Commander(WorldMutation world, UnitState unit) => unit.CommanderId is int id &&
        world.Officers.TryGetValue(id, out var person) && person.IsServing && person.UnitId == unit.Id ? person : null;

    private static long Distance(UnitState unit, MilitaryOrder order, WorldMutation world, MilitaryRulebook rules) =>
        MilitaryCombat.Area(unit, rules).DistanceTo(MilitaryCombat.TargetArea(world, order, rules));

    private static void Respond(WorldMutation world, UnitState unit, UnitPolicyResponse response, int? other,
        List<DomainEvent> events, UnitPolicyReturnOutcome returnOutcome = UnitPolicyReturnOutcome.NotRequested) =>
        events.Add(UnitPolicyRespondedEvent.Capture(world, unit, response, other, unit.ServicePlaceId,
            returnOutcome: returnOutcome));

    private static void ReturnAndRespond(WorldMutation world, UnitState unit, MilitaryRulebook rules,
        UnitPolicyResponse response, int? other, List<DomainEvent> events, string reason)
    {
        bool returned = unit.AwaitingPostAction
            ? MilitaryOrders.ReturnAfterTask(world, unit, rules, events, reason)
            : MilitaryOrders.Return(world, unit, rules, events, reason);
        Respond(world, world.Units[unit.Id], response, other, events,
            returned ? UnitPolicyReturnOutcome.Started : UnitPolicyReturnOutcome.NoDestination);
    }

    private static MilitaryOrder Attack(UnitState unit) => new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = unit.Id };

    internal static void Engage(WorldMutation world, UnitState unit, int? target, List<DomainEvent> events, string reason)
    {
        world.Replace(unit with { EngagementTargetId = target, ContactTarget = null, Lookahead = null });
        events.Add(new UnitEngagementChangedEvent(world.Clock, unit.Id, target, reason));
    }

}
