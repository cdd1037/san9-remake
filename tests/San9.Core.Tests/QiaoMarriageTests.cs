using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class QiaoMarriageTests
{
    [Fact]
    public void EngineValidatesConfiguredEventReferencesBeforeInitializingTheWorld()
    {
        var world = World();
        var before = world.CreateSnapshot();
        Assert.Throws<ArgumentException>(() => new SimulationEngine(world, Rules(Definition() with { RulerId = 999 })));
        Assert.Same(before, world.CreateSnapshot());
        var engine = new SimulationEngine(world, Rules());
        Assert.Single(engine.InitialEvents.OfType<QiaoMarriageEvent>());
        Assert.Equal(engine.Snapshot, engine.Fork().Snapshot);
    }

    internal static QiaoMarriageDefinition Definition() => new(11, 12, [31, 32], 21, 9, [3],
        new(199, 12, 30), new(209, 12, 30), 3, 7, 20, 25, new("qiao-marriage"));
    private static WorldState World() => WorldState.Create(new(190, 1, 1), 17,
        [new(1, 100) { LeaderId = 11, BaseTrust = 100 },
         new(2, 100) { LeaderId = 21, BaseTrust = 100 }],
        Enumerable.Range(1, 3).Select(id => new SettlementState(id, id == 1 ? 1 : 2, 100, 100, 100, 50, 100, 100, 100, 100, 100)
            { Kind = SettlementKind.City, ConsumesFood = false }),
        [new(11, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, LifespanTier = 2 },
         new(12, 1, 1, new(50, 50, 50, 50), false) { LifespanTier = 6 },
         new(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(31, null, null, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special },
         new(32, null, null, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special }],
        controls: [new("player", 1, ControllerKind.Player), new("ai", 2, ControllerKind.Automatic)],
        relations: [new(1, 2, 80)], corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }]);
    private static SimulationRules Rules(QiaoMarriageDefinition? definition = null) => new()
    {
        QiaoMarriage = definition ?? Definition(),
        Personnel = new(Enumerable.Range(1, 3).Select(id => new PersonPlace(id, new(id, id))),
            cityProvinces: new Dictionary<int, int> { [1] = 9, [2] = 8, [3] = 9 })
    };

    [Fact]
    public void CompletionCommitsLifespanTrustRelationAndConsumptionWithoutRandomOrSecondReward()
    {
        var state = World(); var world = state.BeginMutation(); List<DomainEvent> events = [];
        QuarterHistoricalEvents.Begin(world, Rules(), events);
        var result = Assert.Single(events.OfType<QiaoMarriageEvent>());
        Assert.Equal((3, 1, 20, 2), (result.RulerLifespanGain, result.CompanionLifespanGain, result.BaseTrustGain, result.RivalForceId));
        Assert.Equal(5, world.Officers[11].LifespanTier); Assert.Equal(7, world.Officers[12].LifespanTier);
        Assert.Equal(120, world.Forces[1].BaseTrust); Assert.Equal(55, ForceRelations.Get(world, 1, 2).Value);
        Assert.Equal(Definition().ConsumptionKey, Assert.Single(world.Simulation.ConsumedEventKeys));
        Assert.Empty(world.Simulation.RandomStreams);
        state.Commit(world); world = state.BeginMutation();
        QuarterHistoricalEvents.Begin(world, Rules(), events);
        Assert.Single(events.OfType<QiaoMarriageEvent>());
    }

    [Theory]
    [InlineData("dead-participant")]
    [InlineData("captive-companion")]
    [InlineData("foreign-companion")]
    [InlineData("not-ruler")]
    [InlineData("automatic")]
    [InlineData("city-lost")]
    [InlineData("city-destroyed")]
    [InlineData("late-start")]
    [InlineData("late-date")]
    [InlineData("non-quarter")]
    [InlineData("terminal")]
    public void FailedEligibilityDoesNotConsumeOrReward(string failure)
    {
        var world = World().BeginMutation(); var rules = Rules();
        switch (failure)
        {
            case "dead-participant": world.Replace(world.Officers[31] with { Status = OfficerStatus.Dead }); break;
            case "captive-companion": world.Replace(world.Officers[12] with { Status = OfficerStatus.Captive }); break;
            case "foreign-companion": world.Replace(world.Officers[12] with { OwnerId = 2 }); break;
            case "not-ruler": world.Replace(world.Officers[11] with { Status = OfficerStatus.Serving }); break;
            case "automatic": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
            case "city-lost": world.Replace(world.Settlements[1] with { OwnerId = 2 }); break;
            case "city-destroyed": world.Replace(world.Settlements[1] with { IsDestroyed = true }); break;
            case "late-start": rules = Rules(Definition() with { LatestScenarioStart = new(189, 12, 30) }); break;
            case "late-date": world.SetClock(new(210, 1, 1)); break;
            case "non-quarter": world.SetClock(new(190, 2, 1)); break;
            case "terminal": world.SetSimulation(world.Simulation with { TerminalReason = "player-defeated" }); break;
        }
        List<DomainEvent> events = []; QuarterHistoricalEvents.ApplyQiaoMarriage(world, rules, events);
        Assert.Empty(events); Assert.Empty(world.Simulation.ConsumedEventKeys);
        Assert.Equal(100, world.Forces[1].BaseTrust); Assert.Equal(2, world.Officers[11].LifespanTier);
        Assert.Equal(80, ForceRelations.Get(world, 1, 2).Value);
    }

    [Fact]
    public void MissingEligibilityCanRecoverAndExtendedLifespanNeverDecreases()
    {
        var world = World().BeginMutation(); List<DomainEvent> events = [];
        world.Replace(world.Settlements[1] with { OwnerId = 2 });
        QuarterHistoricalEvents.ApplyQiaoMarriage(world, Rules(), events);
        world.Replace(world.Settlements[1] with { OwnerId = 1 });
        world.Replace(world.Officers[11] with { LifespanTier = 12 });
        world.Replace(world.Forces[1] with { BaseTrust = 995 });
        world.Replace(world.Officers[21] with { Status = OfficerStatus.Serving });
        QuarterHistoricalEvents.ApplyQiaoMarriage(world, Rules(), events);
        var result = Assert.Single(events.OfType<QiaoMarriageEvent>());
        Assert.Equal(0, result.RulerLifespanGain); Assert.Equal(5, result.BaseTrustGain);
        Assert.Null(result.RivalForceId); Assert.Equal(80, ForceRelations.Get(world, 1, 2).Value);
    }

    [Fact]
    public void InclusiveDateLimitsAllowEventWithoutMinimumElapsedTime()
    {
        var world = World().BeginMutation(); List<DomainEvent> events = [];
        QuarterHistoricalEvents.Begin(world, Rules(Definition() with
            { LatestScenarioStart = new(190, 1, 1), LatestDate = new(190, 1, 1) }), events);
        Assert.Single(events.OfType<QiaoMarriageEvent>());
        Assert.Empty(events.OfType<AllianceProposedEvent>());
    }
}
