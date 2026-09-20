using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class TreasureMonumentTests
{
    private static TreasureMonumentSearchEventDefinition Definition => new(1, 50, 900, 25000, 10000, 5, new("tower"))
        { Id = "tower", Consumption = EventConsumption.OnSuccess };
    private static SimulationRules Rules() => new()
    {
        Treasures = new([new(50, default, 0)]),
        Personnel = new([new(1, new(0, 0)), new(2, new(0, 0))], searchEvents: [Definition]),
        Politics = new(null, [new(900, 20, 0, 0), new(1, 30, 0, 0), new(999, 10, 0, 0)], [], ImmutableDictionary<int, int>.Empty)
    };
    private static WorldMutation World(uint money = 25001) => CreateWorld(money).BeginMutation();
    private static WorldState CreateWorld(uint money = 25001) => WorldState.Create(new(190, 2, 1), 1,
        [new(1, money, 0, 998) { LeaderId = 1, RankId = 900, BaseTrust = 998 }, new(2, 0, 0, 0) { LeaderId = 10 }],
        [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(2, 1, 1, new(50, 50, 50, 50), false),
         new(3, 1, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive },
         new(10, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
        corps: [new(7, 1, 2, new(50000, 0)) { ControlMode = CorpsControlMode.Direct }],
        treasures: [new(50, 1, TreasureDiscovery.Discovered, null)],
        controls: [new("player", 1, ControllerKind.Player), new("ai", 2, ControllerKind.Automatic)]);
    private static PersistentTask Task() => new(Guid.NewGuid(), "personnel-search", "player", 1, 2, 1,
        new(190, 2, 1), new(190, 2, 1), 0, 0)
        { Journey = new(1, null, 0) { Route = new(new(0, 0), new(0, 0), false) } };

    [Theory]
    [InlineData(25000, false)]
    [InlineData(25001, true)]
    public void StrictFundsGateDoesNotConsumeRandom(uint money, bool eligible)
    {
        var m = World(money);
        Assert.Equal(eligible, TreasureMonument.Eligible(m, Task(), Rules(), Definition));
        Assert.Empty(m.Simulation.RandomStreams); Assert.Empty(m.Simulation.ConsumedEventKeys);
        Assert.Equal(money, m.Forces[1].Money);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(10, false)]
    [InlineData(null, false)]
    public void TreasureUsesHoldersOriginalAffiliationIncludingCaptives(int? holder, bool expected)
    {
        var m = World(); m.Replace(m.Treasures[50] with { HolderId = holder });
        Assert.Equal(expected, TreasureMonument.Eligible(m, Task(), Rules(), Definition));
    }

    [Theory]
    [InlineData("ruler")]
    [InlineData("place")]
    [InlineData("ownership")]
    [InlineData("rank")]
    [InlineData("no-rank")]
    [InlineData("control")]
    [InlineData("consumed")]
    public void InvalidEligibilityDoesNotPartiallyCommit(string reason)
    {
        var m = World(); var task = Task(); var rules = Rules();
        switch (reason)
        {
            case "ruler": task = task with { OfficerId = 1 }; break;
            case "place": task = task with { Journey = task.Journey! with { DestinationPlaceId = 2 } }; break;
            case "ownership": m.Replace(m.Settlements[1] with { OwnerId = 2 }); break;
            case "rank": m.Replace(m.Forces[1] with { RankId = 999 }); break;
            case "no-rank": m.Replace(m.Forces[1] with { RankId = null }); break;
            case "control": m.SetSimulation(m.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
            case "consumed": m.SetSimulation(m.Simulation with { ConsumedEventKeys = [Definition.ConsumptionKey] }); break;
        }
        Assert.False(TreasureMonument.Eligible(m, task, rules, Definition));
        List<DomainEvent> events = [];
        Assert.Throws<InvalidOperationException>(() => TreasureMonument.Resolve(m, task, rules, new("tower"), true, events));
        Assert.Empty(events); Assert.Equal(25001u, m.Forces[1].Money); Assert.Equal(998, m.Forces[1].BaseTrust);
    }

    [Fact]
    public void DeclineCanRetryAndSuccessIsAtomicGlobalAndKeepsItem()
    {
        var m = World(); var rules = Rules(); var task = Task(); List<DomainEvent> events = [];
        m.Replace(m.Forces[1] with { RankId = 1 }); // Higher precedence despite a smaller identifier.
        var item = m.Treasures[50];
        TreasureMonument.Resolve(m, task, rules, new("tower"), false, events);
        Assert.Empty(m.Simulation.ConsumedEventKeys); Assert.Equal(25001u, m.Forces[1].Money);
        Assert.Equal(998, m.Forces[1].BaseTrust); Assert.True(TreasureMonument.Eligible(m, task, rules, Definition));
        events.Clear(); TreasureMonument.Resolve(m, task, rules, new("tower"), true, events);
        Assert.Equal(15001u, m.Forces[1].Money); Assert.Equal(1000, m.Forces[1].BaseTrust); Assert.Equal(item, m.Treasures[50]);
        var payment = Assert.Single(events.OfType<ResourceChangedEvent>());
        Assert.Equal((-10000L, 0L, ResourceOwnerKind.Force), (payment.Actual, payment.Shortfall, payment.Account.Kind));
        Assert.Equal(Definition.ConsumptionKey, Assert.Single(m.Simulation.ConsumedEventKeys));
        Assert.Empty(m.Simulation.RandomStreams); Assert.False(TreasureMonument.Eligible(m, task, rules, Definition));
    }

    [Fact]
    public void IndependentCorpsQualificationCannotCausePartialRootPayment()
    {
        var m = World(9999); var rules = Rules();
        m.Replace(new CorpsState(7, 1, 2, new(50000, 0)) { ControlMode = CorpsControlMode.Direct });
        m.Replace(m.Settlements[1] with { CorpsId = 7 });
        Assert.False(TreasureMonument.Eligible(m, Task(), rules, Definition));
        m.Replace(m.Forces[1] with { Money = 10000 });
        Assert.True(TreasureMonument.Eligible(m, Task(), rules, Definition));
        List<DomainEvent> events = [];
        TreasureMonument.Resolve(m, Task(), rules, new("tower"), true, events);
        Assert.Equal(0u, m.Forces[1].Money); Assert.Equal(50000u, m.Corps[7].Treasury!.Money);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChangedTreasureOrFundsCancelPendingChoiceWithoutPayment(bool transferTreasure)
    {
        var world = CreateWorld(50000); var engine = new SimulationEngine(world, Rules());
        Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        engine.EndControl(new("player")); engine.EndControl(new("ai")); engine.AdvanceXun();
        var prompt = Assert.IsType<TreasureMonumentPrompt>(engine.GetTreasureMonumentPrompt());
        var m = world.BeginMutation();
        if (transferTreasure) m.Replace(m.Treasures[50] with { HolderId = 10 });
        else m.Replace(m.Forces[1] with { Money = 25000 });
        world.Commit(m);
        var result = engine.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
            engine.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, true));
        Assert.DoesNotContain(result.Events, e => e is TreasureMonumentResolvedEvent);
        Assert.DoesNotContain(result.Events.OfType<ResourceChangedEvent>(), e => e.Reason == "treasure-monument");
        Assert.Empty(engine.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Equal(998, engine.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
        Assert.Null(engine.GetTreasureMonumentPrompt());
    }
}
