using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Participant state and holdings at this contest, before strikes or aftermath mutate them.</summary>
public sealed record DuelStartedEvent(GameClock Clock, DuelRoster Roster,
    ImmutableArray<OfficerState> Participants, ImmutableArray<TreasureState> Treasures) : DomainEvent
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<EquipmentInstance>? Equipment { get; init; }
}
public sealed record DuelStrikeEvent(GameClock Clock, int AttackerId, int DefenderId, DuelStrikeKind Kind,
    int Damage, int DefenderHitPoints, int DefenderInjury) : DomainEvent;
public sealed record DuelPairWonEvent(GameClock Clock, int WinnerId, int LoserId, int RemainingHitPoints) : DomainEvent;
public sealed record DuelPairResult(int WinnerId, int LoserId, int WinnerInitialMartial, int LoserInitialMartial);
public sealed record DuelOutcome(DuelRoster Roster, int WinningUnitId, int SurvivorId, int SurvivorHitPoints, ImmutableArray<DuelPairResult> Pairs);

internal static class DuelContest
{
    internal static DuelOutcome Resolve(WorldMutation world, DuelRoster roster, List<DomainEvent> events)
    {
        if (roster.Attackers.IsDefaultOrEmpty || roster.Defenders.IsDefaultOrEmpty ||
            roster.Attackers.Concat(roster.Defenders).Distinct().Count() != roster.Attackers.Length + roster.Defenders.Length)
            throw new ArgumentException("Invalid duel roster.", nameof(roster));
        var participants = roster.Attackers.Concat(roster.Defenders).Select(id => world.Officers[id]).ToImmutableArray();
        var initialMartial = participants.ToDictionary(p => p.Id, p => world.Abilities(p).War);
        int attackingIndex = 0, defendingIndex = 0, attackHp = 100, defenseHp = 100;
        var pairs = ImmutableArray.CreateBuilder<DuelPairResult>();
        var participantIds = participants.Select(p => p.Id).ToHashSet();
        events.Add(new DuelStartedEvent(world.Clock, roster, participants,
            world.Treasures.Values.Where(t => t.HolderId is int holder && participantIds.Contains(holder))
                .OrderBy(t => t.Id).ToImmutableArray())
        { Equipment = world.EquipmentInitialized ? world.AllEquipment.Values
            .Where(item => item.OfficerId is int holder && participantIds.Contains(holder)).OrderBy(item => item.Id).ToImmutableArray() : null });
        int Strike(int source, int target, int sourceHp, int targetHp)
        {
            DuelFighter Fighter(int id, int hp)
            {
                var p = world.Officers[id];
                return new(world.Abilities(p).War, p.Personality, hp, p.Injury, p.DuelAdvantages);
            }
            var result = DuelStrike.Resolve(Fighter(source, sourceHp), Fighter(target, targetHp), () => world.NextRandom("duel-strike"));
            if (result.Damage > 0)
            {
                world.RecordEquipmentUse([source], EquipmentUse.Attack);
                world.RecordEquipmentUse([target], EquipmentUse.Defense);
            }
            if (result.DefenderInjury != world.Officers[target].Injury)
                world.Replace(world.Officers[target] with { Injury = result.DefenderInjury });
            int remaining = targetHp - result.Damage;
            events.Add(new DuelStrikeEvent(world.Clock, source, target, result.Kind, result.Damage, remaining, result.DefenderInjury));
            return remaining;
        }
        while (true)
        {
            int attacker = roster.Attackers[attackingIndex], defender = roster.Defenders[defendingIndex];
            defenseHp = Strike(attacker, defender, attackHp, defenseHp);
            if (defenseHp > 0) attackHp = Strike(defender, attacker, defenseHp, attackHp);
            if (attackHp > 0 && defenseHp > 0) continue;
            bool attackWon = defenseHp == 0;
            int winner = attackWon ? attacker : defender, loser = attackWon ? defender : attacker;
            int survivorHp = attackWon ? attackHp : defenseHp;
            pairs.Add(new(winner, loser, initialMartial[winner], initialMartial[loser]));
            events.Add(new DuelPairWonEvent(world.Clock, winner, loser, survivorHp));
            if (attackWon) defendingIndex++;
            else attackingIndex++;
            if (attackingIndex == roster.Attackers.Length || defendingIndex == roster.Defenders.Length)
                return new(roster, attackWon ? roster.AttackingUnitId : roster.DefendingUnitId, winner, survivorHp, pairs.ToImmutable());
            attackHp = attackWon ? Math.Min(100, attackHp + 20) : 100;
            defenseHp = attackWon ? 100 : Math.Min(100, defenseHp + 20);
        }
    }
}
