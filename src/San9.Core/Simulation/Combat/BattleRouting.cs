using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Resolve morale collapse once both participants have completed their combat opportunities.</summary>
internal static class BattleRouting
{
    internal static void CompleteExchange(WorldMutation world, BattleSource source, BattleSource target,
        SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.Military is not { } military || world.Simulation.TerminalReason is not null) return;
        TryRoute(world, source, military, rules.Treasures, events);
        TryRoute(world, target, military, rules.Treasures, events);
    }

    private static void TryRoute(WorldMutation world, BattleSource subject, MilitaryRulebook military,
        TreasureRulebook treasures, List<DomainEvent> events)
    {
        if (subject.IsSettlement || !subject.IsCombatant(world)) return;
        var unit = world.Units[subject.Id];
        if (unit.IsEmbedded || unit.IsPersonnel || unit.Condition == UnitCondition.Routed || unit.Morale >= 40 ||
            unit.Soldiers == 0 || unit.ServicePlaceId is not int home ||
            !world.Settlements.TryGetValue(home, out var settlement) || settlement.IsDestroyed || !settlement.CanHostPersons) return;
        var members = world.ServingUnitMembers(unit.Id).Where(member => member.OwnerId == unit.OwnerId).OrderBy(member => member.Id).ToArray();
        if (members.Any(member => member.EquippedTacticId is int id && military.Tactics.TryGetValue(id, out var tactic) &&
            tactic.Effect == TacticEffect.CounterConfusion && MilitaryTactics.HasTactic(world, unit.OwnerId, [member.Id], id, treasures))) return;
        long soldiers = world.Engagements(military).Attackers(subject).Where(attacker => attacker.IsCombatant(world))
            .Sum(attacker => (long)attacker.Soldiers(world));
        if (soldiers < 8L * unit.Soldiers) return;
        int days = 5 + (int)(world.NextRandom("military-rout-duration") % 5);
        FacilityConstruction.Cancel(world, unit.Id, events);
        var next = MilitaryConditions.Apply(unit, UnitCondition.Routed, days);
        world.Replace(next);
        events.Add(new BattleConditionChangedEvent(world.Clock, unit.Id, false, UnitCondition.Routed, next.ConditionDaysRemaining)
        { Target = BattleObjectContext.Capture(unit), PreviousCondition = unit.Condition });
        if (unit.EngagementTargetId is not null)
            events.Add(new UnitEngagementChangedEvent(world.Clock, unit.Id, null, "routed"));
        BattleLearning.Passive(world, members, TacticEffect.CounterConfusion, military, events);
    }
}
