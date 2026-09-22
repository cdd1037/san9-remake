using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ImperialCourtTests
{
    private static OfficerState Person(int id, int? owner, int city, OfficerStatus status) =>
        new(id, owner, city, new(50, 50, 50, 50), false) { Status = status };
    private static WorldState World(ulong seed = 17, bool ended = false) => WorldState.Create(new(190, 1, 1), seed,
        [new(1, 1000, 1000, 100) { LeaderId = 1, BaseTrust = 100 }, new(2, 1000, 1000, 100) { LeaderId = 2, BaseTrust = 100 }],
        [new(1, 1, 100, 100, 100, 50, 300, 0, 100, 0, 100) { Kind = SettlementKind.City },
         new(2, 2, 100, 100, 100, 50, 300, 0, 100, 0, 100) { Kind = SettlementKind.City }],
        [Person(1, 1, 1, OfficerStatus.Ruler), Person(2, 2, 2, OfficerStatus.Ruler),
         Person(12, null, 1, OfficerStatus.Free), Person(11, null, 2, OfficerStatus.Free), Person(13, null, 2, OfficerStatus.Free)],
        corps: [new(1, 1, 1, null), new(2, 2, 2, null)], imperialCourtEnded: ended);
    private static ImperialCourtDefinition Definition(int survival = 60) => new([12, 11, 13],
        [new(12, new(190, 2, 1), new(190, 4, 1), survival)]);
    private static SimulationRules Rules(ImperialCourtDefinition? definition = null) => new()
    { Economy = new([], []) { ImperialCourt = definition ?? Definition() } };
    private static WorldMutation Due(WorldState state) { var w = state.BeginMutation(economy: Rules().Economy); w.RebuildForceTrust(); w.SetClock(new(190, 4, 1)); return w; }
    private static ulong Seed(int roll)
    {
        for (ulong seed = 0; seed < 100000; seed++)
            if (World(seed).BeginMutation().NextRandom("event-emperor-death") % 100 == (ulong)roll) return seed;
        throw new InvalidOperationException();
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    public void SurvivalBoundaryAndDynamicSuccessionRefreshBothForcesTrust(int roll, bool dies)
    {
        var state = World(Seed(roll)); var world = Due(state); var rules = Rules(); List<DomainEvent> events = [];
        Assert.Equal(200, world.Forces[1].Trust);
        events.Clear(); ImperialCourt.ApplyDeaths(world, rules, events);
        Assert.Equal(dies ? 11 : 12, rules.Economy.ImperialCourt.Resolve(false, world.Officers.Values)!.Id);
        Assert.Equal(dies ? 100 : 200, world.Forces[1].Trust);
        Assert.Equal(dies ? 200 : 100, world.Forces[2].Trust);
        Assert.Equal(dies ? 1 : 0, events.OfType<EmperorDeathEvent>().Count());
        if (dies)
        {
            Assert.Equal(new EmperorDeathEvent(world.Clock, 12, 12, 11), Assert.Single(events.OfType<EmperorDeathEvent>()));
            Assert.Equal(OfficerDeathCause.HistoricalEvent, Assert.Single(events.OfType<OfficerDiedEvent>()).Cause);
        }
        state.Commit(world);
    }

    [Theory]
    [InlineData("ended")]
    [InlineData("start-equal")]
    [InlineData("start-after")]
    [InlineData("too-early")]
    [InlineData("empty")]
    public void WorldAndDateGatesDoNotConsumeRandom(string gate)
    {
        var world = Due(World(ended: gate == "ended")); var definition = Definition(0);
        if (gate == "start-equal") definition = definition with { DeathEvents = [definition.DeathEvents[0] with { ScenarioStartsBefore = world.Simulation.Started }] };
        if (gate == "start-after") definition = definition with { DeathEvents = [definition.DeathEvents[0] with { ScenarioStartsBefore = new(189, 12, 1) }] };
        if (gate == "too-early") world.SetClock(new(190, 3, 30));
        if (gate == "empty") definition = ImperialCourtDefinition.Empty;
        List<DomainEvent> events = []; ImperialCourt.ApplyDeaths(world, Rules(definition), events);
        Assert.Empty(events); Assert.Empty(world.Simulation.RandomStreams);
    }

    [Fact]
    public void DeadCandidateStillConsumesDrawAndDoesNotDieTwiceOrEndCourt()
    {
        var world = Due(World()); var rules = Rules(Definition(0)); List<DomainEvent> events = [];
        ImperialCourt.ApplyDeaths(world, rules, events);
        var first = world.Simulation.RandomStreams;
        events.Clear(); ImperialCourt.ApplyDeaths(world, rules, events);
        Assert.Empty(events); Assert.NotEqual(first, world.Simulation.RandomStreams);
        foreach (int id in new[] { 11, 13 }) world.Replace(world.Officers[id] with { Status = OfficerStatus.Dead, SettlementId = null });
        Assert.Null(rules.Economy.ImperialCourt.Resolve(false, world.Officers.Values));
        Assert.False(world.Simulation.ImperialCourtEnded);
    }

    [Fact]
    public void FirstSuccessfulDeathStopsQuarterPreludeAndNextQuarterResumesEligibility()
    {
        var state = World(); var world = Due(state);
        world.Replace(world.Officers[12] with { Status = OfficerStatus.Captive, OwnerId = 2 });
        var definition = Definition(0) with { DeathEvents = [
            new(12, new(191, 1, 1), new(190, 4, 1), 0), new(11, new(191, 1, 1), new(190, 4, 1), 0)] };
        Assert.Equal(12, definition.Resolve(false, world.Officers.Values)!.Id);
        List<DomainEvent> events = []; ImperialCourt.ApplyDeaths(world, Rules(definition), events);
        Assert.Equal(12, Assert.Single(events.OfType<EmperorDeathEvent>()).OfficerId);
        Assert.Equal(OfficerStatus.Free, world.Officers[11].Status);
        Assert.Equal(11, definition.Resolve(false, world.Officers.Values)!.Id);
        var expected = Due(World());
        expected.NextRandom("event-emperor-death");
        Assert.Equal(expected.Simulation.RandomStreams.ToArray(), world.Simulation.RandomStreams.ToArray());
        events.Clear(); world.SetClock(new(190, 7, 1));
        ImperialCourt.ApplyDeaths(world, Rules(definition), events);
        Assert.Equal(11, Assert.Single(events.OfType<EmperorDeathEvent>()).OfficerId);
        expected.NextRandom("event-emperor-death"); // Already-dead first entry still tests its probability.
        expected.NextRandom("event-emperor-death");
        Assert.Equal(expected.Simulation.RandomStreams.ToArray(), world.Simulation.RandomStreams.ToArray());
        Assert.Equal(13, definition.Resolve(false, world.Officers.Values)!.Id);
        Assert.All(world.Forces.Values, f => Assert.Null(f.RankId)); state.Commit(world);
    }

    [Fact]
    public void SharedDeathClearsAdviserAndTransfersTreasureWithoutChangingInstitution()
    {
        var world = Due(World());
        world.Replace(world.Officers[12] with { Status = OfficerStatus.Strategist, OwnerId = 1 });
        world.Replace(world.Forces[1] with { AdviserId = 12 });
        // Recreate with a valid authored treasure so the entire death transaction can be validated.
        var state = WorldState.Create(world.Clock, 17, world.Forces.Values, world.Settlements.Values, world.Officers.Values,
            corps: world.Corps.Values, treasures: [new(7, 12, TreasureDiscovery.Discovered, 1)]);
        var rules = new SimulationRules
        {
            Economy = Rules(Definition(0) with { DeathEvents = [new(12, new(191, 1, 1), world.Clock, 0)] }).Economy,
            Treasures = new([new(7, default, 0)])
        };
        world = state.BeginMutation(rules.Abilities);
        ImperialCourt.ApplyDeaths(world, rules, []);
        Assert.Null(world.Forces[1].AdviserId); Assert.Equal(1, world.Treasures[7].HolderId);
        Assert.Null(world.Officers[12].OwnerId); Assert.Null(world.Officers[12].SettlementId);
        Assert.False(world.Simulation.ImperialCourtEnded); state.Commit(world);
    }

    [Fact]
    public void CityResolutionHandlesDirectCityRegionAndTravelWithoutCachedIcons()
    {
        var world = World().BeginMutation(); var emperor = world.Officers[12];
        Assert.Equal(1, ImperialCourtDefinition.City(emperor, world.Settlements.Values, new Dictionary<int, int>(), false));
        Assert.Equal(2, ImperialCourtDefinition.City(emperor with { SettlementId = null, RegionId = 50 },
            world.Settlements.Values, new Dictionary<int, int> { [50] = 2 }, false));
        Assert.Null(ImperialCourtDefinition.City(emperor, world.Settlements.Values, new Dictionary<int, int>(), true));
        Assert.Null(Definition().Resolve(true, world.Officers.Values));
    }
}
