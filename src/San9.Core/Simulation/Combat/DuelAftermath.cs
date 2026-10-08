using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record DuelAftermathProgress(DuelOutcome Outcome, int NextDeathIndex, int? PendingRulerId);
public sealed record DuelSuccessionRequestedEvent(GameClock Clock, int RulerId, int RespondingForceId) : DomainEvent;
public sealed record DuelCompletedEvent(GameClock Clock, int WinningUnitId, int LosingUnitId, int SurvivorId, int SurvivorHitPoints) : DomainEvent;

internal static class DuelAftermath
{
    internal static int Interval(int morale) => (int)Math.Min(10, (100 - DuelStrike.Score(morale)) / 10 + 5);
    internal static long DeathChance(int martial) => Math.Min(30, 80L - martial);
    internal static long CaptureChance(int martial) => Math.Min(60, 120 - DuelStrike.Score(martial));

    internal static ImmutableArray<int> Successors(WorldMutation world, int rulerId) => RulerSuccession.Candidates(world, rulerId);

    internal static DuelAftermathProgress Begin(WorldMutation world, DuelOutcome outcome)
    {
        void SetIntervals(int unitId, ImmutableArray<int> people)
        {
            if (!world.Units.TryGetValue(unitId, out var unit)) return;
            foreach (int id in people.Order())
                world.Replace(world.Officers[id] with { TacticCooldown = Interval(unit.Morale) });
        }
        SetIntervals(outcome.Roster.AttackingUnitId, outcome.Roster.Attackers);
        SetIntervals(outcome.Roster.DefendingUnitId, outcome.Roster.Defenders);
        return new(outcome, 0, null);
    }

    // Returns only when a player must select a successor. A null result means all aftermath stages completed.
    internal static DuelAftermathProgress? Advance(WorldMutation world, DuelAftermathProgress progress, SimulationRules rules,
        List<DomainEvent> events, int? successorId = null)
    {
        var outcome = progress.Outcome;
        int loserUnit = outcome.WinningUnitId == outcome.Roster.AttackingUnitId ? outcome.Roster.DefendingUnitId : outcome.Roster.AttackingUnitId;
        var losers = (loserUnit == outcome.Roster.AttackingUnitId ? outcome.Roster.Attackers : outcome.Roster.Defenders).Order().ToArray();
        var priorWinners = outcome.Pairs.Select(p => p.WinnerId).ToHashSet();
        if (progress.PendingRulerId is int ruler)
        {
            if (successorId is not int successor || !Successors(world, ruler).Contains(successor))
                throw new ArgumentException("Invalid duel successor.", nameof(successorId));
            OfficerDeath.Resolve(world, ruler, OfficerDeathCause.Duel, rules, events, afterDailyCommands: world.CurrentDateCommandsConsumed, successorId: successor);
        }
        else if (successorId is not null) throw new ArgumentException("No duel succession is pending.", nameof(successorId));

        bool Roll(string stream, long chance) => chance > 0 && (chance >= 100 || (long)(world.NextRandom(stream) % 100) < chance);
        bool Candidate(int id) => world.Officers[id] is { IsServing: true, OwnerId: int owner, UnitId: int unit } &&
            unit == loserUnit && !world.Forces[owner].IsBarbarian && !priorWinners.Contains(id) &&
            world.Units.TryGetValue(unit, out var current) && current.CommanderId != id;
        bool CanKill() => BattleDeathRules.Enabled(world, rules.Military!) &&
            world.Units.ContainsKey(outcome.Roster.AttackingUnitId) &&
            world.Units.ContainsKey(outcome.Roster.DefendingUnitId);

        for (int i = progress.NextDeathIndex; i < losers.Length && CanKill(); i++)
        {
            int id = losers[i];
            var person = world.Officers[id];
            if (!Candidate(id) || BattleDeathRules.IsProtected(world, person,
                    world.Units[loserUnit].CommanderId == id, OfficerDeathCause.Duel) ||
                !Roll("duel-death", DeathChance(world.Abilities(person).War))) continue;
            if (person.OwnerId is int owner && world.Forces[owner].LeaderId == id &&
                world.Simulation.Controls.Any(c => c.ForceId == owner && c.Kind == ControllerKind.Player) && !Successors(world, id).IsEmpty)
            {
                events.Add(new DuelSuccessionRequestedEvent(world.Clock, id, owner));
                return new(outcome, i + 1, id);
            }
            OfficerDeath.Resolve(world, id, OfficerDeathCause.Duel, rules, events, afterDailyCommands: world.CurrentDateCommandsConsumed);
        }
        if (rules.Military!.DuelCapturesAllowed && world.Units.TryGetValue(outcome.WinningUnitId, out var winner) && !world.Forces[winner.OwnerId].IsBarbarian)
        {
            foreach (int id in losers)
            {
                if (!Candidate(id) || world.HasEscapeMount(id, rules.Treasures) ||
                    !Roll("duel-capture", CaptureChance(world.Abilities(world.Officers[id]).War))) continue;
                PersonnelTransitions.Capture(world, id, null, outcome.WinningUnitId, rules.Personnel, events);
                world.Replace(world.Officers[id] with { TacticCooldown = 0 });
            }
        }
        foreach (int id in outcome.Pairs.Select(p => p.LoserId).Distinct().Order())
            if (world.Officers[id].Status is not (OfficerStatus.Dead or OfficerStatus.Captive))
                world.Replace(world.Officers[id] with { Injury = Math.Max(7, world.Officers[id].Injury) });
        int change = outcome.SurvivorHitPoints / 5 + 10;
        if (world.Units.TryGetValue(outcome.WinningUnitId, out var winningUnit))
            world.Replace(winningUnit with { Morale = Math.Min(100, winningUnit.Morale + change) });
        if (world.Units.TryGetValue(loserUnit, out var losingUnit))
            world.Replace(losingUnit with { Morale = losingUnit.Morale == 0 ? 0 : Math.Max(1, losingUnit.Morale - change) });
        foreach (var pair in outcome.Pairs.GroupBy(p => p.WinnerId).OrderBy(g => g.Key).Select(g => g.First()))
            if (pair.WinnerInitialMartial < pair.LoserInitialMartial)
                OfficerAbilityGrowth.Increase(world, pair.WinnerId, OfficerAbility.War, 1, events);
        world.MaintainLeadership(events);
        events.Add(new DuelCompletedEvent(world.Clock, outcome.WinningUnitId, loserUnit, outcome.SurvivorId, outcome.SurvivorHitPoints));
        return null;
    }
}
