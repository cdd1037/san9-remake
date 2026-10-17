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
        ResolveOrders(world, rules, events);
        var ids = world.Units.Keys.Order().ToArray();
        // Resolve every retreat before pursuit so evaluation does not depend on a target's identity.
        foreach (int id in ids)
        {
            var unit = world.Units[id];
            if (!Active(unit) || unit.Order is null || unit.Order.Kind == MilitaryOrderKind.Return || Commander(world, unit) is not { } leader) continue;
            long enemySoldiers = NearbyEnemies(world, unit, rules).Sum(u => (long)u.Soldiers);
            if (UnitPolicyDecisions.Retreat(unit.Policies.AutonomousRetreat, leader.Personality, unit.Morale, unit.Soldiers, enemySoldiers))
                Return(world, unit, rules, events, "autonomous-retreat");
        }
        foreach (int id in ids)
        {
            var unit = world.Units[id];
            if (!Active(unit) || rules.Purpose(unit.FormationId!.Value) != UnitPurpose.Combat || unit.Order?.Kind is MilitaryOrderKind.Construct or MilitaryOrderKind.Transport or MilitaryOrderKind.Return ||
                unit.EffectiveOrder?.UnitId is not int targetId || !world.Units.TryGetValue(targetId, out var target) ||
                target.Order?.Kind != MilitaryOrderKind.Return || Commander(world, unit) is not { } leader) continue;
            bool mainTarget = unit.Order?.UnitId == targetId;
            if (unit.Policies.Pursue && (mainTarget || leader.Personality == 0)) continue;
            if (!mainTarget)
            {
                Engage(world, unit, null, events, "pursuit-resume");
            }
            else if (leader.Personality != 0 || Nearby(world, unit, rules, false).Any(u => u.Id != targetId))
            {
                Return(world, unit, rules, events, "pursuit-return");
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
            if (candidate is not null && UnitPolicyDecisions.Attack(unit.Policies.AttackApproachingEnemies, leader.Personality,
                    Ratio(world, unit, candidate)))
                Engage(world, unit, candidate.Id, events, "enemy-approach");
        }
    }

    internal static void ResolveOrders(WorldMutation world, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        if (rules is null) return;
        foreach (int id in world.Units.Keys.Order().ToArray())
        {
            var unit = world.Units[id];
            if (unit.FormationId is null) continue;
            var formation = rules.Formation(unit);
            if (unit.EngagementTargetId is not null && !MilitaryCombat.ValidTarget(world, unit.OwnerId, unit.EffectiveOrder!, formation, rules))
            {
                Engage(world, unit, null, events, "engagement-target-invalid");
                unit = world.Units[id];
            }
            if (unit.Order is { IsAttack: true } attack && !MilitaryCombat.ValidTarget(world, unit.OwnerId, attack, formation, rules))
            {
                if (attack.SettlementId is int place && MilitaryMovement.ValidOrder(world, unit.OwnerId,
                        new(MilitaryOrderKind.Return, null, place), formation, rules))
                {
                    ChangeOrder(world, unit, new(MilitaryOrderKind.Return, null, place), events, "target-captured-entry");
                    continue;
                }
                unit = unit with { Order = null, AwaitingPostAction = true, Waypoint = null, Lookahead = null, MovementRemainder = 0 };
                world.Replace(unit);
            }
            if (!unit.AwaitingPostAction || !Active(unit)) continue;
            var candidate = Nearby(world, unit, rules, true).FirstOrDefault();
            if (candidate is not null && Commander(world, unit) is { } leader &&
                UnitPolicyDecisions.Attack(unit.Policies.AttackAfterAction, leader.Personality, Ratio(world, unit, candidate)))
                ChangeOrder(world, unit, Attack(candidate), events, "post-action-attack");
            else Return(world, unit, rules, events, "post-action-return");
        }
    }

    private static OfficerState? Commander(WorldMutation world, UnitState unit) => unit.CommanderId is int id &&
        world.Officers.TryGetValue(id, out var person) && person.IsServing && person.UnitId == unit.Id ? person : null;

    private static long Distance(UnitState unit, MilitaryOrder order, WorldMutation world, MilitaryRulebook rules) =>
        MilitaryCombat.Area(unit, rules).DistanceTo(MilitaryCombat.TargetArea(world, order, rules));

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
    {
        long own = CombatMath.PolicyStrength(MilitaryCombat.Strength(world, unit));
        long enemy = CombatMath.PolicyStrength(MilitaryCombat.Strength(world, candidate));
        return (long)System.Numerics.BigInteger.Min(long.MaxValue - 25, (System.Numerics.BigInteger)own * 100 / enemy);
    }

    internal static bool Return(WorldMutation world, UnitState unit, MilitaryRulebook rules, List<DomainEvent> events, string reason)
    {
        if (unit.Order is null && !unit.AwaitingPostAction) return false;
        var order = unit.ServicePlaceId is int home ? new MilitaryOrder(MilitaryOrderKind.Return, null, home) : null;
        if (!MilitaryMovement.ValidOrder(world, unit.OwnerId, order, rules.Formations[unit.FormationId!.Value], rules, unit.NavalFormationId)) return false;
        FacilityConstruction.Cancel(world, unit.Id, events);
        ChangeOrder(world, world.Units[unit.Id], order, events, reason);
        return true;
    }

    private static void ChangeOrder(WorldMutation world, UnitState unit, MilitaryOrder? order, List<DomainEvent> events, string reason)
    {
        world.Replace(unit with { Order = order, EngagementTargetId = null, AwaitingPostAction = false,
            Waypoint = null, Lookahead = null, MovementRemainder = 0 });
        events.Add(new UnitOrderChangedEvent(world.Clock, unit.Id, order, reason));
        if (unit.EngagementTargetId is not null) events.Add(new UnitEngagementChangedEvent(world.Clock, unit.Id, null, reason));
    }

    internal static void Engage(WorldMutation world, UnitState unit, int? target, List<DomainEvent> events, string reason)
    {
        world.Replace(unit with { EngagementTargetId = target });
        events.Add(new UnitEngagementChangedEvent(world.Clock, unit.Id, target, reason));
    }

    internal static void RemoveTarget(WorldMutation world, int targetId, bool settlement, List<DomainEvent> events)
    {
        foreach (var unit in world.Units.Values.OrderBy(u => u.Id).ToArray())
        {
            bool main = settlement ? unit.Order?.SettlementId == targetId : unit.Order?.UnitId == targetId;
            bool temporary = !settlement && unit.EngagementTargetId == targetId;
            if (!main && !temporary) continue;
            world.Replace(unit with { Order = main ? null : unit.Order,
                AwaitingPostAction = unit.AwaitingPostAction || main && unit.Order!.IsAttack,
                EngagementTargetId = temporary ? null : unit.EngagementTargetId });
            if (main) events.Add(new UnitOrderChangedEvent(world.Clock, unit.Id, null, "target-removed"));
            if (temporary) events.Add(new UnitEngagementChangedEvent(world.Clock, unit.Id, null, "target-removed"));
        }
    }

    internal static void SettlementChanged(WorldMutation world, int id, MilitaryRulebook rules, List<DomainEvent> events)
    {
        foreach (var unit in world.Units.Values.Where(u => u.Order?.SettlementId == id).OrderBy(u => u.Id).ToArray())
        {
            var enter = new MilitaryOrder(MilitaryOrderKind.Return, null, id);
            if (MilitaryMovement.ValidOrder(world, unit.OwnerId, enter, rules.Formations[unit.FormationId!.Value], rules, unit.NavalFormationId))
                ChangeOrder(world, unit, enter, events, "target-captured-entry");
            else
            {
                world.Replace(unit with { Order = null, AwaitingPostAction = unit.Order!.IsAttack });
                events.Add(new UnitOrderChangedEvent(world.Clock, unit.Id, null, "settlement-changed-owner"));
            }
        }
    }
}
