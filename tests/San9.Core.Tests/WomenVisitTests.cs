using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class WomenVisitTests
{
    private static WomenVisitSearchEventDefinition Definition => new(
        [new(1, 3, [101, 102], [], null, null, null)], 8, 50, 1000)
        { Id = "visit", Consumption = EventConsumption.None };

    private static (WorldMutation World, PersistentTask Task) Create(int roll = 0, int year = 190)
    {
        var world = WorldState.Create(new(year, 1, 1), 123,
            [new(1, 999) { LeaderId = 1, BaseTrust = 51 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(50, 99, 99, 50), false),
             new(3, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special }],
            controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(0, 10000)) { IsRoot = true }]).BeginMutation();
        ulong seed = Enumerable.Range(1, 10000).Select(i => (ulong)i).First(s => new StableRandom(s).NextUInt64() % 100 == (ulong)roll);
        world.SetSimulation(world.Simulation with { RandomStreams = [new("event-search-eligibility", seed)] });
        return (world, new(Guid.NewGuid(), "personnel-search", "player", 1, 2, 1, world.Clock, world.Clock, 0, 0)
            { Journey = new(1, null, 0) });
    }

    [Theory]
    [InlineData("eligible", true, true)]
    [InlineData("wrong-place", false, false)]
    [InlineData("computer", false, false)]
    [InlineData("base-trust", false, true)]
    [InlineData("current-trust", false, true)]
    [InlineData("dead", false, true)]
    [InlineData("known", false, true)]
    [InlineData("chance", false, true)]
    public void EligibilityHonorsStrictBoundsAndRandomOrder(string variant, bool expected, bool draws)
    {
        var (world, task) = Create(variant == "chance" ? 12 : 11);
        switch (variant)
        {
            case "wrong-place": task = task with { Journey = new(2, null, 0) }; break;
            case "computer": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
            case "base-trust": world.Replace(world.Forces[1] with { BaseTrust = 50 }); break;
            case "current-trust": world.Replace(world.Forces[1] with { BaseTrust = 1000 }); break;
            case "dead": world.Replace(world.Officers[3] with { Status = OfficerStatus.Dead }); break;
            case "known": world.Replace(world.Officers[2] with { LearnedTactics = [101, 102] }); break;
        }
        var before = world.Officers[2]; var force = world.Forces[1];
        var random = new StableRandom(world.Simulation.RandomStreams.Single().State);
        if (draws) random.NextUInt64();
        var events = new List<DomainEvent>();
        Assert.Equal(expected, WomenVisit.TryResolve(world, task, Definition, events));
        Assert.Equal(random.State, world.Simulation.RandomStreams.Single().State);
        Assert.Equal(force, world.Forces[1]); Assert.Empty(world.Simulation.ConsumedEventKeys);
        if (expected) Assert.Equal(101, Assert.Single(events.OfType<WomenVisitResolvedEvent>()).LearnedTacticId);
        else { Assert.Equal(before, world.Officers[2]); Assert.Empty(events); }
    }

    [Theory]
    [InlineData(199, 1, true, false)]
    [InlineData(200, 1, true, true)]
    [InlineData(200, 2, true, true)]
    [InlineData(200, 3, true, false)]
    [InlineData(200, 1, false, false)]
    public void QiaoRequiresScenarioDateMarriageAndCurrentLeader(int year, int leader, bool married, bool expected)
    {
        var (world, task) = Create(year: year);
        world.SetClock(new(220, 1, 1));
        world.Replace(world.Forces[1] with { LeaderId = leader }); world.Replace(ResourceAccounts.Root(world, 1) with { LeaderId = leader });
        world.SetSimulation(world.Simulation with { ConsumedEventKeys = married ? [new("married")] : [] });
        var d = Definition with { Sites = [Definition.Sites[0] with
            { EarliestScenarioStart = new(200, 1, 1), RequiredLeaderIds = [1, 2], RequiredWorldKey = new("married") }] };
        Assert.Equal(expected, WomenVisit.TryResolve(world, task, d, []));
    }

    [Theory]
    [InlineData(207, false, false)]
    [InlineData(207, true, true)]
    [InlineData(208, false, true)]
    public void CaiYanUsesScenarioDateOrOwnForceSuccessHistory(int year, bool returned, bool expected)
    {
        var (world, task) = Create(year: year);
        world.SetClock(new(220, 1, 1));
        world.SetSimulation(world.Simulation with { ConsumedEventKeys = [new("cai-yan")] });
        world.Replace(world.Forces[1] with { EventHistory = returned ? [new("returned")] : [] });
        var d = Definition with { Sites = [Definition.Sites[0] with
            { EarliestScenarioStart = new(208, 1, 1), AlternativeForceHistoryKey = new("returned") }] };
        Assert.Equal(expected, WomenVisit.TryResolve(world, task, d, []));
    }

    [Fact]
    public void CandidateFailureContinuesAndFirstSuccessStopsTheSearchTable()
    {
        var (world, task) = Create();
        world.Replace(world.Officers[2] with { LearnedTactics = [101] });
        var exhausted = Definition with { Id = "exhausted", Priority = 3, Sites = [Definition.Sites[0] with { RewardTacticIds = [101] }] };
        var winner = Definition with { Id = "winner", Priority = 2 };
        var later = Definition with { Id = "later", Priority = 1 };
        // Choose a stream that passes both eligible-place checks.
        ulong seed = Enumerable.Range(1, 10000).Select(i => (ulong)i).First(s =>
        {
            var random = new StableRandom(s);
            return random.NextUInt64() % 100 < 12 && random.NextUInt64() % 100 < 12;
        });
        world.SetSimulation(world.Simulation with { RandomStreams = [new("event-search-eligibility", seed)] });
        var rules = new SimulationRules { Personnel =
            new PersonnelRulebook([new(1, new(0, 0))], searchEvents: [later, winner, exhausted]) };
        var events = new List<DomainEvent>();
        Assert.True(SearchEvents.Prepare(world, task, rules, events, out var encounter));
        Assert.Null(encounter);
        Assert.Equal("winner", Assert.Single(events.OfType<WomenVisitResolvedEvent>()).EventId);
        var expected = new StableRandom(seed); expected.NextUInt64(); expected.NextUInt64();
        Assert.Equal(expected.State, world.Simulation.RandomStreams.Single().State);
    }

    [Fact]
    public void RepeatedVisitsLearnTheFirstUnknownAndNeverRewardAnExhaustedPool()
    {
        var (world, task) = Create();
        var d = Definition with { PoliticsChanceDivisor = 1 };
        // Keep each attempt eligible while testing the changing learned set.
        var random = world.Simulation.RandomStreams;
        foreach (int tactic in new[] { 101, 102 })
        {
            world.SetSimulation(world.Simulation with { RandomStreams = random });
            var events = new List<DomainEvent>();
            Assert.True(WomenVisit.TryResolve(world, task, d, events));
            Assert.Equal(tactic, Assert.Single(events.OfType<WomenVisitResolvedEvent>()).LearnedTacticId);
        }
        world.SetSimulation(world.Simulation with { RandomStreams = random });
        Assert.False(WomenVisit.TryResolve(world, task, d, []));
        Assert.Equal(new[] { 101, 102 }, world.Officers[2].LearnedTactics);
        Assert.Empty(world.Simulation.ConsumedEventKeys);
    }
}
