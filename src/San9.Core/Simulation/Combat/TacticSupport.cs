using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record TacticHealingEvent(GameClock Clock, BattleSource Source, int TargetUnitId, uint RecoveredSoldiers,
    ImmutableArray<int> HealedOfficers, bool ConditionCleared) : DomainEvent
{
    public int? TacticId { get; init; }
    public int? ActorId { get; init; }
    public TacticOccurrence? Occurrence { get; init; }
}

internal static class TacticSupport
{
    internal static void AdvanceDay(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.Military is not { } military) return;
        foreach (int id in world.Units.Keys.Order().ToArray())
        {
            if (world.Simulation.TerminalReason is not null) return;
            if (!world.Units.TryGetValue(id, out var source) || source.IsPersonnel || source.IsEmbedded ||
                source.Condition is UnitCondition.Confused or UnitCondition.Routed || source.FormationId is not int formationId ||
                military.Formation(source).Tactics is not { } profile) continue;
            var members = world.ServingUnitMembers(id).Where(p => p.OwnerId == source.OwnerId)
                .OrderBy(p => military.Rank(source, p.Id)).ThenBy(p => p.Id).ToArray();
            var candidates = members.Where(p => p.Injury == 0 && p.TacticCooldown == 0 && p.EquippedTacticId is int tacticId &&
                military.Tactics[tacticId] is var tactic && tactic.Effect is TacticEffect.Inspire or TacticEffect.Heal &&
                tactic.CompatibleFormations.Contains(formationId) && MilitaryTactics.HasTactic(world, source.OwnerId, [p.Id], tacticId, rules.Treasures))
                .Select(p => new { Officer = p, Tactic = military.Tactics[p.EquippedTacticId!.Value] })
                .Select(p => new { p.Officer, p.Tactic, Targets = Targets(world, new(source.Id, false), p.Tactic, military) })
                .Where(p => p.Targets.Length > 0).ToArray();
            if (candidates.Length == 0 || !TacticCombat.Roll(world, "military-tactic-support-opportunity",
                Math.Min(100, profile.OpportunityChance + (source.Condition == UnitCondition.HighSpirits ? 5 : 0)))) continue;
            foreach (var candidate in candidates)
            {
                if (!TacticCombat.Roll(world, "military-tactic-support-member", TacticCombat.AllocationChance(
                    military.Rank(source, candidate.Officer.Id), members.Length, false))) continue;
                Resolve(world, new(id, false), candidate.Officer, candidate.Tactic, candidate.Targets, members, military, events);
                break;
            }
        }
    }

    internal static void Resolve(WorldMutation world, BattleSource source, OfficerState actor, TacticRule tactic,
        int[] targets, OfficerState[] members, MilitaryRulebook military, List<DomainEvent> events)
    {
        world.Replace(world.Officers[actor.Id] with { TacticCooldown = tactic.MinimumInterval });
        events.Add(new TacticActivatedEvent(world.Clock, source.Id, source.IsSettlement, targets[0],
            tactic.Effect == TacticEffect.Inspire && source.IsSettlement, tactic.Id, [actor.Id], false, 10)
        {
            Occurrence = TacticOccurrence.Capture(world, source,
            new(source.IsSettlement && tactic.Effect == TacticEffect.Inspire ? MilitaryOrderKind.AttackSettlement : MilitaryOrderKind.AttackUnit,
                null, source.IsSettlement && tactic.Effect == TacticEffect.Inspire ? source.Id : null)
            { UnitId = source.IsSettlement && tactic.Effect == TacticEffect.Inspire ? null : targets[0] }, tactic, military)
        });
        int proficiency = actor.TacticProficiencies[tactic.Category!.Value];
        int intelligence = world.Abilities(actor).Intelligence;
        foreach (int targetId in targets)
        {
            if (tactic.Effect == TacticEffect.Inspire)
            {
                int previous = source.Morale(world);
                int morale = Math.Min(100, previous + TacticMath.InspireMorale(proficiency, intelligence, tactic.Power));
                source.Reward(world, 0, morale - previous);
                events.Add(new TacticMoraleChangedEvent(world.Clock, source.Id, source.IsSettlement, previous, morale, tactic.Id));
            }
            else
            {
                var target = world.Units[targetId];
                var occurrence = TacticOccurrence.Capture(world, source,
                    new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = targetId }, tactic, military);
                uint recovered = TacticMath.HealSoldiers(proficiency, intelligence, tactic.Power, target.Wounded,
                    military.Capacity(target) - target.Soldiers);
                bool highSpirits = target.Condition == UnitCondition.HighSpirits;
                bool clear = !highSpirits && (target.Condition != UnitCondition.Normal || target.ConditionDaysRemaining > 0);
                world.Replace(target with
                {
                    Soldiers = target.Soldiers + recovered,
                    Wounded = target.Wounded - recovered,
                    Condition = highSpirits ? target.Condition : UnitCondition.Normal,
                    ConditionDaysRemaining = highSpirits ? target.ConditionDaysRemaining : 0,
                    HighSpiritsMoralePenaltyPending = false
                });
                var healed = world.ServingUnitMembers(targetId).Where(p => p.Injury > 0).OrderBy(p => p.Id).ToArray();
                foreach (var officer in healed) world.Replace(officer with { Injury = 0 });
                ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, targetId), ResourceKind.Soldiers, "tactic-healing", source.Id, recovered, recovered, events);
                ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, targetId), ResourceKind.Wounded, "tactic-healing", source.Id, -(long)recovered, -(long)recovered, events);
                if (clear) events.Add(new BattleConditionChangedEvent(world.Clock, targetId, false, UnitCondition.Normal, 0));
                events.Add(new TacticHealingEvent(world.Clock, source, targetId, recovered, healed.Select(p => p.Id).ToImmutableArray(), clear)
                { TacticId = tactic.Id, ActorId = actor.Id, Occurrence = occurrence });
            }
        }
        TacticCombat.Grow(world, members, [actor.Id], tactic, events);
    }

    internal static int[] Targets(WorldMutation world, BattleSource source, TacticRule tactic, MilitaryRulebook rules)
    {
        if (tactic.Effect == TacticEffect.Inspire) return source.Morale(world) < 80 ? [source.Id] : [];
        var area = source.Area(world, rules);
        return world.Units.Values.Where(u => u.OwnerId == source.Owner(world) && u.FormationId is int formation &&
            MilitaryCombat.Area(u, rules).DistanceTo(area) <= tactic.EffectRadius &&
            (u.Wounded > 0 && u.Soldiers < rules.Capacity(u) || u.Condition is UnitCondition.Confused or UnitCondition.Routed ||
                world.ServingUnitMembers(u.Id).Any(p => p.Injury > 0)))
            .OrderBy(u => u.Id).Select(u => u.Id).ToArray();
    }
}
