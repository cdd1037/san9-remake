using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SetUnitPoliciesCommand(Guid CommandId, int IssuerId, long Sequence, int UnitId, UnitPolicies Policies)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record UnitPoliciesChangedEvent(GameClock Clock, int UnitId, UnitPolicies Policies) : DomainEvent;
public sealed record UnitEngagementChangedEvent(GameClock Clock, int UnitId, int? TargetId, string Reason) : DomainEvent;

internal static class MilitaryPolicies
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

    private static bool Active(UnitState unit) => unit.DepartureDaysRemaining == 0 && !unit.IsEmbedded && !unit.IsPersonnel && unit.FormationId is not null &&
        unit.Condition is UnitCondition.Normal or UnitCondition.HighSpirits;

    internal static void AdvanceDay(WorldMutation world, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        if (rules is null) return;
        MilitaryOrders.ResolveOrders(world, rules, events);
        var ids = world.Units.Keys.Order().ToArray();
        // Resolve every retreat before pursuit so evaluation does not depend on a target's identity.
        foreach (int id in ids)
        {
            var unit = world.Units[id];
            if (!Active(unit) || unit.Order is null || unit.Order.Kind == MilitaryOrderKind.Return || Commander(world, unit) is not { } leader) continue;
            long enemySoldiers = NearbyEnemies(world, unit, rules).Sum(u => (long)u.Soldiers);
            if (UnitPolicyDecisions.Retreat(unit.Policies.AutonomousRetreat, leader.Personality, unit.Morale, unit.Soldiers, enemySoldiers) &&
                MilitaryOrders.Return(world, unit, rules, events, "autonomous-retreat"))
                Respond(world, unit, unit.Policies.AutonomousRetreat
                    ? unit.Morale < 4 * leader.Personality + 25 ? UnitPolicyResponse.RetreatOrdered : UnitPolicyResponse.RetreatOutnumbered
                    : unit.Morale < 4 * (leader.Personality - 2) + 25 ? UnitPolicyResponse.RetreatLowMorale : UnitPolicyResponse.RetreatDespiteOrders,
                    null, events);
        }
        foreach (int id in ids)
        {
            var unit = world.Units[id];
            if (!Active(unit) || rules.Purpose(unit.FormationId!.Value) != UnitPurpose.Combat || unit.Order?.Kind is MilitaryOrderKind.Construct or MilitaryOrderKind.Transport or MilitaryOrderKind.Return ||
                unit.EffectiveOrder?.UnitId is not int targetId || !world.Units.TryGetValue(targetId, out var target) ||
                target.Order?.Kind != MilitaryOrderKind.Return || Commander(world, unit) is not { } leader) continue;
            bool mainTarget = unit.Order?.UnitId == targetId;
            if (unit.Policies.Pursue && (mainTarget || leader.Personality == 0) ||
                !unit.Policies.Pursue && mainTarget && leader.Personality == 0 && !Nearby(world, unit, rules, false).Any(u => u.Id != targetId))
            {
                // Continuing pursuit changes no order. Report only the transition into the target's return.
                if (events.OfType<UnitOrderChangedEvent>().Any(e => e.UnitId == targetId && e.Order?.Kind == MilitaryOrderKind.Return))
                    Respond(world, unit, unit.Policies.Pursue ? UnitPolicyResponse.PursueOrdered : UnitPolicyResponse.PursueDespiteOrders,
                        target.CommanderId, events);
                continue;
            }
            if (!mainTarget)
            {
                Engage(world, unit, null, events, "pursuit-resume");
                Respond(world, unit, UnitPolicyResponse.ResumeOrders, target.CommanderId, events);
            }
            else if (leader.Personality != 0 || Nearby(world, unit, rules, false).Any(u => u.Id != targetId))
            {
                if (MilitaryOrders.Return(world, unit, rules, events, "pursuit-return"))
                    Respond(world, unit, UnitPolicyResponse.ReturnHome, target.CommanderId, events);
            }
        }
    }

    internal static void ApproachXun(WorldMutation world, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        if (rules is null) return;
        foreach (int id in world.Units.Keys.Order().ToArray())
        {
            var unit = world.Units[id];
            if (!Active(unit) || unit.EngagementTargetId is not null || unit.Order is null ||
                rules.Purpose(unit.FormationId!.Value) != UnitPurpose.Combat || unit.Order.Kind is MilitaryOrderKind.Construct or MilitaryOrderKind.Transport or MilitaryOrderKind.Return ||
                Commander(world, unit) is not { } leader) continue;
            if (unit.Order.IsAttack && MilitaryCombat.ValidTarget(world, unit.OwnerId, unit.Order, rules.Formation(unit), rules) &&
                Distance(unit, unit.Order, world, rules) <= rules.PolicyRadius) continue;
            var candidate = Nearby(world, unit, rules, false).FirstOrDefault();
            if (candidate is null)
            {
                if (!unit.ApproachNotificationPending) world.Replace(unit with { ApproachNotificationPending = true });
                continue;
            }
            world.Replace(unit with { ApproachNotificationPending = false });
            if (UnitPolicyDecisions.Attack(unit.Policies.AttackApproachingEnemies, leader.Personality, Ratio(world, unit, candidate)))
            {
                Engage(world, unit with { ApproachNotificationPending = false }, candidate.Id, events, "enemy-approach");
                Respond(world, unit, unit.Policies.AttackApproachingEnemies ? UnitPolicyResponse.AttackOrdered : UnitPolicyResponse.AttackDespiteOrders,
                    candidate.CommanderId, events);
            }
            else if (unit.ApproachNotificationPending)
                Respond(world, unit, unit.Policies.AttackApproachingEnemies ? UnitPolicyResponse.AvoidDespiteOrders : UnitPolicyResponse.AvoidOrdered,
                    candidate.CommanderId, events);
        }
    }

    internal static void ResolvePostAction(WorldMutation world, UnitState unit, MilitaryRulebook rules, List<DomainEvent> events)
    {
        if (!unit.AwaitingPostAction || !Active(unit)) return;
        var candidate = Nearby(world, unit, rules, true).FirstOrDefault();
        if (candidate is not null && Commander(world, unit) is { } leader &&
            UnitPolicyDecisions.Attack(unit.Policies.AttackAfterAction, leader.Personality, Ratio(world, unit, candidate)))
            MilitaryOrders.ChangeOrder(world, unit, Attack(candidate), events, "post-action-attack");
        else MilitaryOrders.Return(world, unit, rules, events, "post-action-return");
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
            long ratio = StrengthRatio(world, unit, strength);
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

    private static void Respond(WorldMutation world, UnitState unit, UnitPolicyResponse response, int? other, List<DomainEvent> events) =>
        events.Add(new UnitPolicyRespondedEvent(world.Clock, new(unit.Id, false, unit.OwnerId, unit.CommanderId,
            unit.Position, unit.ServicePlaceId, unit.IsEmbedded), response, other, unit.ServicePlaceId));

    private static MilitaryOrder Attack(UnitState unit) => new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = unit.Id };

    private static IEnumerable<UnitState> Nearby(WorldMutation world, UnitState unit, MilitaryRulebook rules, bool requireAutomaticEnemy)
    {
        var formation = rules.Formation(unit);
        return world.Units.Values.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.DepartureDaysRemaining == 0)
            .Where(u => MilitaryDiplomacy.AutomaticEnemy(world, unit.OwnerId, u.OwnerId) || !requireAutomaticEnemy &&
                (MilitaryDiplomacy.TargetOwner(world, unit.Order) == u.OwnerId || MilitaryDiplomacy.TargetOwner(world, unit.EffectiveOrder) == u.OwnerId))
            .Where(u => MilitaryCombat.ValidTarget(world, unit.OwnerId, Attack(u), formation, rules))
            .Select(u => (Unit: u, Distance: MilitaryCombat.Area(unit, rules).DistanceTo(MilitaryCombat.Area(u, rules))))
            .Where(c => c.Distance <= rules.PolicyRadius)
            .OrderBy(c => c.Distance).ThenBy(c => c.Unit.Id)
            .Select(c => c.Unit);
    }

    private static IEnumerable<UnitState> NearbyEnemies(WorldMutation world, UnitState unit, MilitaryRulebook rules) =>
        world.Units.Values.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.FormationId is not null &&
            MilitaryDiplomacy.AutomaticEnemy(world, unit.OwnerId, u.OwnerId) &&
            MilitaryCombat.Area(unit, rules).DistanceTo(MilitaryCombat.Area(u, rules)) <= rules.PolicyRadius);

    private static long Ratio(WorldMutation world, UnitState unit, UnitState candidate)
        => StrengthRatio(world, unit, MilitaryCombat.Strength(world, candidate));

    private static long StrengthRatio(WorldMutation world, UnitState unit, CombatStrength target)
    {
        long own = CombatMath.PolicyStrength(MilitaryCombat.Strength(world, unit));
        long enemy = CombatMath.PolicyStrength(target);
        return (long)System.Numerics.BigInteger.Min(long.MaxValue - 25, (System.Numerics.BigInteger)own * 100 / enemy);
    }

    internal static void Engage(WorldMutation world, UnitState unit, int? target, List<DomainEvent> events, string reason)
    {
        world.Replace(unit with { EngagementTargetId = target, ContactTarget = null, Lookahead = null });
        events.Add(new UnitEngagementChangedEvent(world.Clock, unit.Id, target, reason));
    }

}
