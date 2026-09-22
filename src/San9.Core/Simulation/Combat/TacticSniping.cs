using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record TacticSnipingResolvedEvent(GameClock Clock, BattleSource Source, BattleSource Target,
    int TacticId, int ActorId, int OfficerId, int PreviousInjury, int Injury) : DomainEvent;

internal sealed record SnipingOutcome(BattleSource Source, BattleSource Target, int TacticId,
    int ActorId, int OfficerId, int Injury, bool ProtectedFromDeath);

internal static class TacticSniping
{
    internal static SnipingOutcome? Select(WorldMutation world, BattleSource source, TacticRule tactic,
        MilitaryOrder target, IEnumerable<int> participants, int effect, MilitaryRulebook rules)
    {
        if (tactic.SnipingProficiencyCategory is not int category || effect < 10) return null;
        var actors = participants.Distinct().Order().Select(id => world.Officers[id])
            .Where(p => p.IsServing && p.TacticProficiencies[category] > 300).ToArray();
        var targets = IntelligenceTactics.Defenders(world, target).Where(p => p.Injury == 0 &&
            (target.UnitId is int unit ? world.Units[unit].MemberRanks.TryGetValue(p.Id, out var rank) &&
                rank is BattleRank.Front or BattleRank.Middle : !p.Acted && !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id)))
            .ToArray();
        if (actors.Length == 0 || targets.Length == 0) return null;
        var actor = actors[(int)(world.NextRandom("military-sniping-actor") % (uint)actors.Length)];
        int bonus = !source.IsSettlement && world.Units[source.Id].FormationId is int formation
            ? rules.Formations[formation].SnipingChanceBonus : 0;
        long proficiency = actor.TacticProficiencies[category] / 100;
        if (!TacticCombat.Roll(world, "military-sniping", (int)Math.Min(100, bonus + proficiency * proficiency / 2))) return null;
        var person = targets[(int)(world.NextRandom("military-sniping-target") % (uint)targets.Length)];
        int attack = world.Abilities(actor).War, defense = world.Abilities(person).War;
        long delta = DuelStrike.Score(attack) - DuelStrike.Score(defense);
        int chance = (int)Math.Clamp(attack < defense ? delta + 70 : delta, 0, 100);
        bool severe = TacticCombat.Roll(world, "military-sniping-injury", chance);
        int injury = attack < defense ? severe ? 4 : 1 : severe ? 7 : 4;
        bool protectedFromDeath = BattleDeathRules.IsProtected(world, person,
            target.UnitId is int targetUnit ? world.Units[targetUnit].CommanderId == person.Id : person.IsCommander,
            OfficerDeathCause.Sniping);
        return new(source, new(target.UnitId ?? target.SettlementId!.Value, target.UnitId is null),
            tactic.Id, actor.Id, person.Id, injury, protectedFromDeath);
    }

    internal static void Apply(WorldMutation world, SnipingOutcome? outcome, bool targetDefeated,
        SimulationRules rules, List<DomainEvent> events)
    {
        if (outcome is null || !world.Officers.TryGetValue(outcome.OfficerId, out var person) || person.Status == OfficerStatus.Dead) return;
        int injury = Math.Max(person.Injury, outcome.Injury);
        world.Replace(person with { Injury = injury });
        events.Add(new TacticSnipingResolvedEvent(world.Clock, outcome.Source, outcome.Target, outcome.TacticId,
            outcome.ActorId, person.Id, person.Injury, injury));
        // Injury belongs to the pre-loss result even if capture, escape or destruction removed its carrier.
        // Subsequent death remains a surviving-combatant effect, independent of message visibility.
        if (targetDefeated || outcome.ProtectedFromDeath || !person.IsServing ||
            !BattleDeathRules.Enabled(world, rules.Military!) ||
            !outcome.Source.Exists(world) || !outcome.Target.Exists(world)) return;
        if (TacticCombat.Roll(world, "military-sniping-death", outcome.Injury * outcome.Injury))
            OfficerDeath.Resolve(world, person.Id, OfficerDeathCause.Sniping, rules, events, afterDailyCommands: true);
    }
}
