using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class DuelContestTests
{
    internal static OfficerState Person(int id, int unit, int martial = 80) =>
        new(id, unit, null, new(martial, 50, 50, 50), false) { UnitId = unit };

    internal static WorldState World(OfficerState[] people, ulong seed = 42, IEnumerable<TreasureState>? treasures = null)
    {
        var units = new[] { 1, 2 }.Select(id => new UnitState(id, id, new(5 + id, 2), 10000)
        {
            FormationId = 1, Morale = 100, MemberOrder = people.Where(p => p.UnitId == id).Select(p => p.Id).ToImmutableArray(),
            CommanderId = people.First(p => p.UnitId == id).Id,
            MemberRanks = people.Where(p => p.UnitId == id).ToImmutableDictionary(p => p.Id, _ => BattleRank.Front)
        });
        return WorldState.Create(new(190, 1, 1), seed,
            [new(1, 1000, 1000, 100), new(2, 1000, 1000, 100)],
            [new(1, 1, 100, 100, 100, 100, 0, 0, 100, 0, 100), new(2, 2, 100, 100, 100, 100, 0, 0, 100, 0, 100)],
            people, units: units, treasures: treasures);
    }

    internal static MilitaryRulebook Rules(bool doubles = false, bool water = false) => new(new(4, 1, [0, 0, 0, 0], [new(0, 1, 1)],
        [new(1, 5, 0, 100000, 100, new(1, 1), water ? TravelMode.Water : TravelMode.Land)
            { DoublesDuelChance = doubles, Tactics = new(0, 0, 2, [], []) }], []));

    private static ulong SeedForTrigger(int roll)
    {
        for (ulong seed = 0; seed < 10000; seed++)
            if (new StableRandom(StableRandom.DeriveSeed(seed, "duel-trigger")).NextUInt64() % 100 == (ulong)roll) return seed;
        throw new InvalidOperationException("Missing test seed.");
    }

    [Theory]
    [InlineData(false, 1, 3)]
    [InlineData(false, 2, 6)]
    [InlineData(false, 3, 9)]
    [InlineData(true, 1, 6)]
    [InlineData(true, 2, 12)]
    [InlineData(true, 3, 18)]
    public void TriggerCountsAllFrontMembersIncludingIneligibleSupport(bool doubles, int count, int threshold)
    {
        var people = Enumerable.Range(0, count).Select(i => Person(11 + i, 1) with { Injury = i == 0 ? 0 : 1 }).Append(Person(21, 2)).ToArray();
        foreach (int roll in new[] { threshold - 1, threshold })
        {
            var world = World(people, SeedForTrigger(roll)).BeginMutation();
            var roster = DuelParticipants.Select(world, world.Units[1], world.Units[2], Rules(doubles));
            Assert.Equal(roll < threshold, roster is not null);
            Assert.DoesNotContain(world.Simulation.RandomStreams, s => s.Name == "duel-support");
        }
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(1, 3)]
    [InlineData(2, 0)]
    public void PersonalityGateUsesChosenInitialAndDoesNotTryLowerMartialRecklessMember(int personality, int allowedDeficit)
    {
        foreach (int extra in new[] { 0, 1 })
        {
            var world = World([Person(11, 1) with { Personality = personality }, Person(12, 1, 79),
                Person(21, 2, 80 + allowedDeficit + extra) with { Personality = 3, TacticCooldown = 10 }], SeedForTrigger(0)).BeginMutation();
            var roster = DuelParticipants.Select(world, world.Units[1], world.Units[2], Rules());
            Assert.Equal(extra == 0, roster is not null);
            if (roster is not null)
            {
                Assert.Equal(11, roster.Attackers[0]);
                Assert.Equal(21, roster.Defenders[0]);
            }
            else Assert.DoesNotContain(world.Simulation.RandomStreams, s => s.Name == "duel-support");
        }
    }

    [Fact]
    public void TiedInitialsAndSupportersUseMemberOrderAndActedExcludesAttackerButNotDefender()
    {
        var world = World([Person(19, 1) with { Acted = true }, Person(11, 1) with { TacticLinkBonus = 100 },
            Person(15, 1) with { TacticLinkBonus = 100 }, Person(13, 1) with { Acted = true, TacticLinkBonus = 100 },
            Person(21, 2) with { Acted = true }], SeedForTrigger(0)).BeginMutation();
        var roster = DuelParticipants.Select(world, world.Units[1], world.Units[2], Rules())!;
        Assert.Equal(new[] { 11, 15 }, roster.Attackers);
        Assert.Equal(new[] { 21 }, roster.Defenders);
        Assert.DoesNotContain(world.Simulation.RandomStreams, s => s.Name == "duel-support");
    }

    [Fact]
    public void WaterAndNoEligibleInitialConsumeNoRandom()
    {
        var world = World([Person(11, 1), Person(21, 2)]).BeginMutation();
        Assert.Null(DuelParticipants.Select(world, world.Units[1], world.Units[2], Rules(water: true)));
        world.Replace(world.Officers[11] with { Personality = 3 });
        Assert.Null(DuelParticipants.Select(world, world.Units[1], world.Units[2], Rules()));
        Assert.Empty(world.Simulation.RandomStreams);
    }

    [Fact]
    public void SupportsDrawInAttackingThenDefendingMemberOrder()
    {
        ulong seed = 0;
        for (; seed < 100000; seed++)
        {
            if (new StableRandom(StableRandom.DeriveSeed(seed, "duel-trigger")).NextUInt64() % 100 >= 9) continue;
            var support = new StableRandom(StableRandom.DeriveSeed(seed, "duel-support"));
            if (support.NextUInt64() % 100 < 50 && support.NextUInt64() % 100 >= 50 &&
                support.NextUInt64() % 100 >= 50 && support.NextUInt64() % 100 < 50) break;
        }
        Assert.True(seed < 100000);
        var world = World([Person(11, 1, 100), Person(19, 1), Person(12, 1),
            Person(21, 2, 100), Person(29, 2), Person(22, 2)], seed).BeginMutation();
        var roster = DuelParticipants.Select(world, world.Units[1], world.Units[2], Rules())!;
        Assert.Equal(new[] { 11, 19 }, roster.Attackers);
        Assert.Equal(new[] { 21, 22 }, roster.Defenders);
        var expected = new StableRandom(StableRandom.DeriveSeed(seed, "duel-support"));
        for (int i = 0; i < 4; i++) expected.NextUInt64();
        Assert.Equal(expected.State, world.Simulation.RandomStreams.Single(s => s.Name == "duel-support").State);
    }

    [Fact]
    public void SupportQualificationExcludesInjuredCooldownActedAndNonFrontMembers()
    {
        var world = World([Person(11, 1, 100), Person(12, 1) with { Injury = 1 },
            Person(13, 1) with { TacticCooldown = 1 }, Person(14, 1) with { Acted = true }, Person(15, 1),
            Person(21, 2, 100)], SeedForTrigger(0)).BeginMutation();
        world.Replace(world.Units[1] with { MemberRanks = world.Units[1].MemberRanks.SetItem(15, BattleRank.Rear) });
        var roster = DuelParticipants.Select(world, world.Units[1], world.Units[2], Rules())!;
        Assert.Equal(new[] { 11 }, roster.Attackers);
        Assert.DoesNotContain(world.Simulation.RandomStreams, s => s.Name == "duel-support");
    }

    [Fact]
    public void ContestPreservesFirstStrikeHitPointConservationReplacementsAndUnboundedRosters()
    {
        var people = Enumerable.Range(0, 7).Select(i => Person(11 + i, 1))
            .Concat(Enumerable.Range(0, 7).Select(i => Person(21 + i, 2))).ToArray();
        var world = World(people).BeginMutation();
        var restored = World(people).BeginMutation();
        var roster = new DuelRoster(1, 2, [11, 12, 13, 14, 15, 16, 17], [21, 22, 23, 24, 25, 26, 27]);
        var events = new List<DomainEvent>();
        var outcome = DuelContest.Resolve(world, roster, events);
        var repeatEvents = new List<DomainEvent>();
        Assert.Equal(CanonicalJson.Text(outcome), CanonicalJson.Text(DuelContest.Resolve(restored, roster, repeatEvents)));
        Assert.Equal(CanonicalJson.Text(events), CanonicalJson.Text(repeatEvents));
        Assert.True(outcome.Pairs.Length >= 7);
        var hp = people.ToDictionary(p => p.Id, _ => 100);
        int attackIndex = 0, defenseIndex = 0;
        bool attackingTurn = true;
        foreach (var item in events.Skip(1))
        {
            if (item is DuelStrikeEvent strike)
            {
                Assert.Equal(attackingTurn ? roster.Attackers[attackIndex] : roster.Defenders[defenseIndex], strike.AttackerId);
                Assert.True(hp[strike.AttackerId] > 0);
                Assert.Equal(hp[strike.DefenderId] - strike.Damage, strike.DefenderHitPoints);
                hp[strike.DefenderId] = strike.DefenderHitPoints;
                attackingTurn = !attackingTurn;
                Assert.IsType<DuelStrikeEvent>(JsonSerializer.Deserialize<DomainEvent>(JsonSerializer.Serialize<DomainEvent>(strike)));
            }
            else if (item is DuelPairWonEvent pair)
            {
                Assert.Equal(0, hp[pair.LoserId]);
                Assert.Equal(hp[pair.WinnerId], pair.RemainingHitPoints);
                hp[pair.WinnerId] = Math.Min(100, hp[pair.WinnerId] + 20);
                if (roster.Attackers.Contains(pair.LoserId)) attackIndex++;
                else defenseIndex++;
                attackingTurn = true;
            }
        }
        Assert.All(outcome.Pairs, p => { Assert.Equal(80, p.WinnerInitialMartial); Assert.Equal(80, p.LoserInitialMartial); });
        Assert.InRange(outcome.SurvivorHitPoints, 1, 100);
        Assert.Contains(world.Officers.Values, p => p.Injury > 0);
    }
}
