using San9.Core.Domain;
using San9.Core.Simulation;
using static San9.Core.Tests.DuelContestTests;

namespace San9.Core.Tests;

public sealed class DuelAftermathTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComputerOnlyDuelsRespectDeathOptionWithoutPlayerGate(bool enabled)
    {
        var world = Mutation(enabled);
        world.SetSimulation(world.Simulation with { Controls = [] });
        DuelAftermath.Advance(world, DuelAftermath.Begin(world, SingleLoser()),
            new() { Military = Policy(true, false) }, []);
        Assert.Equal(enabled, world.Officers[22].Status == OfficerStatus.Dead);
        Assert.Equal(enabled, world.Simulation.RandomStreams.Any(s => s.Name == "duel-death"));
    }

    [Theory]
    [InlineData(80, 10)]
    [InlineData(100, 5)]
    [InlineData(0, 10)]
    public void CooldownUsesUnitMorale(int morale, int expected) => Assert.Equal(expected, DuelAftermath.Interval(morale));

    [Theory]
    [InlineData(50, 30, 60)]
    [InlineData(79, 1, 60)]
    [InlineData(80, 0, 60)]
    [InlineData(100, -20, 20)]
    [InlineData(int.MaxValue, -2147483567L, -6442450621L)]
    public void ProbabilityBoundsSupportLargeAuthoredAbilities(int martial, long death, long capture)
    {
        Assert.Equal(death, DuelAftermath.DeathChance(martial));
        Assert.Equal(capture, DuelAftermath.CaptureChance(martial));
    }

    private static DuelOutcome Outcome() => new(new(1, 2, [11], [22, 23, 21]), 1, 11, 50,
        [new(11, 22, 40, 50), new(11, 23, 40, 50), new(11, 21, 40, 100)]);

    private static MilitaryRulebook Policy(bool deaths, bool captures) => new(new(4, 1, [0, 0, 0, 0], [new(0, 1, 1)], [], [])
        { BattleDeathsAllowed = deaths, DuelCapturesAllowed = captures });

    private static DuelOutcome SingleLoser() => new(new(1, 2, [11], [22]), 1, 11, 50, [new(11, 22, 70, 50)]);

    private static ulong Seed()
    {
        for (ulong seed = 0; seed < 100000; seed++)
        {
            var death = new StableRandom(StableRandom.DeriveSeed(seed, "duel-death"));
            var capture = new StableRandom(StableRandom.DeriveSeed(seed, "duel-capture"));
            if (death.NextUInt64() % 100 < 30 && death.NextUInt64() % 100 >= 30 && capture.NextUInt64() % 100 < 60) return seed;
        }
        throw new InvalidOperationException("Missing test seed.");
    }

    private static WorldMutation Mutation(bool deaths = true)
    {
        var world = World([Person(11, 1, 70), Person(21, 2, 100), Person(22, 2, 50), Person(23, 2, 50)], Seed()).BeginMutation();
        world.Replace(world.Forces[1] with { LeaderId = 11 });
        world.Replace(world.Forces[2] with { LeaderId = 21 });
        world.SetSimulation(world.Simulation with { BattleDeathsEnabled = deaths, Controls = [new("player", 1, ControllerKind.Player)] });
        return world;
    }

    [Fact]
    public void DeathCaptureAndInjuryArePartitionedBeforeMoraleAndOneGrowthReward()
    {
        var world = Mutation();
        var events = new List<DomainEvent>();
        Assert.Null(DuelAftermath.Advance(world, DuelAftermath.Begin(world, Outcome()), new() { Military = Rules() }, events));
        Assert.Equal(OfficerStatus.Dead, world.Officers[22].Status);
        Assert.Equal(OfficerStatus.Captive, world.Officers[23].Status);
        Assert.Equal(1, world.Officers[23].UnitId);
        Assert.Equal(0, world.Officers[23].TacticCooldown);
        Assert.Equal(7, world.Officers[21].Injury);
        Assert.Equal(5, world.Officers[21].TacticCooldown);
        Assert.Equal(80, world.Units[2].Morale);
        Assert.Equal(100, world.Units[1].Morale);
        Assert.Equal(71, world.Officers[11].BaseAbilities.War);
        Assert.Single(events.OfType<OfficerAbilityGrownEvent>());
        Assert.True(events.FindIndex(e => e is OfficerDiedEvent) < events.FindIndex(e => e is OfficerCapturedEvent));
        Assert.All(world.Simulation.RandomStreams, s => Assert.Contains(s.Name, new[] { "duel-death", "duel-capture" }));
    }

    [Fact]
    public void PlayerRulerWaitsBeforeDeathAndResumesWithoutRerolling()
    {
        var world = Mutation();
        world.Replace(world.Forces[2] with { LeaderId = 22 });
        world.Replace(world.Officers[22] with { Status = OfficerStatus.Ruler });
        world.SetSimulation(world.Simulation with { Controls = [new("player", 2, ControllerKind.Player)] });
        var events = new List<DomainEvent>();
        var rules = new SimulationRules { Military = Rules() };
        var progress = DuelAftermath.Advance(world, DuelAftermath.Begin(world, Outcome()), rules, events)!;
        Assert.Equal(22, progress.PendingRulerId);
        Assert.Equal(2, progress.NextDeathIndex); // Commander 21 was skipped before ruler 22 in identity order.
        Assert.Equal(OfficerStatus.Ruler, world.Officers[22].Status);
        Assert.Empty(events.OfType<OfficerDiedEvent>());
        Assert.Empty(events.OfType<OfficerCapturedEvent>());
        Assert.DoesNotContain(22, DuelAftermath.Successors(world, 22));
        Assert.Throws<ArgumentException>(() => DuelAftermath.Advance(world, progress, rules, events, 22));
        Assert.Null(DuelAftermath.Advance(world, progress, rules, events, 21));
        Assert.Equal(21, world.Forces[2].LeaderId);
        Assert.Equal(OfficerStatus.Dead, world.Officers[22].Status);
        Assert.Equal(OfficerStatus.Captive, world.Officers[23].Status);
        var random = new StableRandom(StableRandom.DeriveSeed(Seed(), "duel-death"));
        random.NextUInt64(); random.NextUInt64();
        Assert.Equal(random.State, world.Simulation.RandomStreams.Single(s => s.Name == "duel-death").State);
    }

    [Fact]
    public void DisabledDeathsStillAllowCaptureAndPriorPairWinnerCannotDieOrBeCaptured()
    {
        var world = Mutation(false);
        var outcome = Outcome() with { Pairs = [new(22, 11, 50, 70), new(11, 22, 70, 50), new(11, 23, 70, 50), new(11, 21, 70, 100)] };
        Assert.Null(DuelAftermath.Advance(world, DuelAftermath.Begin(world, outcome), new() { Military = Rules() }, []));
        Assert.Equal(OfficerStatus.Serving, world.Officers[22].Status);
        Assert.Equal(7, world.Officers[22].Injury);
        Assert.Equal(OfficerStatus.Captive, world.Officers[23].Status);
        Assert.Equal(7, world.Officers[11].Injury);
        Assert.Equal(70, world.Officers[11].BaseAbilities.War);
        Assert.Equal(51, world.Officers[22].BaseAbilities.War);
        Assert.DoesNotContain(world.Simulation.RandomStreams, s => s.Name == "duel-death");
    }

    [Theory]
    [InlineData("scenario")]
    [InlineData("barbarian")]
    [InlineData("commander")]
    [InlineData("war")]
    [InlineData("intelligence")]
    [InlineData("politics")]
    [InlineData("leadership")]
    public void DeathExemptionsConsumeNoDeathRandom(string exemption)
    {
        var world = Mutation();
        if (exemption == "barbarian") world.Replace(world.Forces[2] with { IsBarbarian = true });
        if (exemption == "commander") world.Replace(world.Units[2] with { CommanderId = 22 });
        var abilities = world.Officers[22].BaseAbilities;
        world.Replace(world.Officers[22] with { BaseAbilities = exemption switch
        {
            "war" => abilities with { War = 80 }, "intelligence" => abilities with { Intelligence = 80 },
            "politics" => abilities with { Politics = 80 }, "leadership" => abilities with { Leadership = 80 }, _ => abilities
        } });
        DuelAftermath.Advance(world, DuelAftermath.Begin(world, SingleLoser()), new() { Military = Policy(exemption != "scenario", false) }, []);
        Assert.NotEqual(OfficerStatus.Dead, world.Officers[22].Status);
        Assert.DoesNotContain(world.Simulation.RandomStreams, s => s.Name == "duel-death");
    }

    [Theory]
    [InlineData("scenario")]
    [InlineData("winner-barbarian")]
    [InlineData("loser-barbarian")]
    [InlineData("commander")]
    public void CaptureExemptionsFallBackToInjuryWithoutRandom(string exemption)
    {
        var world = Mutation(false);
        if (exemption == "winner-barbarian") world.Replace(world.Forces[1] with { IsBarbarian = true });
        if (exemption == "loser-barbarian") world.Replace(world.Forces[2] with { IsBarbarian = true });
        if (exemption == "commander") world.Replace(world.Units[2] with { CommanderId = 22 });
        DuelAftermath.Advance(world, DuelAftermath.Begin(world, SingleLoser()), new() { Military = Policy(true, exemption != "scenario") }, []);
        Assert.Equal(OfficerStatus.Serving, world.Officers[22].Status);
        Assert.Equal(7, world.Officers[22].Injury);
        Assert.Empty(world.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(50, 20)]
    [InlineData(100, 30)]
    public void SurvivorHitPointsDetermineMoraleAndGrowthDoesNotLowerOvercap(int hp, int change)
    {
        var world = Mutation(false);
        world.Replace(world.Units[1] with { Morale = 80 });
        world.Replace(world.Units[2] with { Morale = 5 });
        world.Replace(world.Officers[11] with { BaseAbilities = new(120, 50, 50, 50) });
        DuelAftermath.Advance(world, DuelAftermath.Begin(world, Outcome() with { SurvivorHitPoints = hp }),
            new() { Military = Policy(false, false) }, []);
        Assert.Equal(Math.Min(100, 80 + change), world.Units[1].Morale);
        Assert.Equal(1, world.Units[2].Morale);
        Assert.Equal(120, world.Officers[11].BaseAbilities.War);
        Assert.Empty(world.Simulation.RandomStreams);
    }

    [Fact]
    public void EscapeMountPreventsCaptureWithoutRandom()
    {
        var rules = new SimulationRules { Military = Rules(),
            Treasures = new([new(7, default, 0) { EscapeMount = true }]) };
        var world = World([Person(11, 1), Person(21, 2, 100), Person(22, 2, 50)], Seed(),
            [new(7, 22, TreasureDiscovery.Discovered, null)]).BeginMutation(rules.Abilities);
        DuelAftermath.Advance(world, DuelAftermath.Begin(world, SingleLoser()), rules, []);
        Assert.Equal(OfficerStatus.Serving, world.Officers[22].Status);
        Assert.Equal(7, world.Officers[22].Injury);
        Assert.Empty(world.Simulation.RandomStreams);
    }
}
