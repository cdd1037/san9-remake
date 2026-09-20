using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class DivinerVisitTests
{
    private static DivinerVisitSearchEventDefinition Definition => new(1, 3, 3, 50, new("visited"))
        { Id = "diviner", Consumption = EventConsumption.None };

    private static (WorldMutation World, PersistentTask Task) Create(int roll = 0)
    {
        var world = WorldState.Create(new(190, 1, 1), 123,
            [new(1, 0, 0, 51) { LeaderId = 1, BaseTrust = 0 }, new(2, 0, 0, 51) { LeaderId = 4, BaseTrust = 0 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(50, 99, 99, 50), false),
             new(3, null, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special },
             new(4, 2, 2, new(50, 99, 99, 50), false) { Status = OfficerStatus.Ruler }],
            controls: [new("first", 1, ControllerKind.Player), new("second", 2, ControllerKind.Player)]).BeginMutation();
        ulong seed = Enumerable.Range(1, 10000).Select(i => (ulong)i).First(s => new StableRandom(s).NextUInt64() % 100 == (ulong)roll);
        world.SetSimulation(world.Simulation with { RandomStreams = [new("event-search-eligibility", seed)] });
        return (world, new(Guid.NewGuid(), "personnel-search", "first", 1, 2, 1, world.Clock, world.Clock, 0, 0)
            { Journey = new(1, null, 0) });
    }

    [Theory]
    [InlineData("eligible", true, true)]
    [InlineData("place", false, false)]
    [InlineData("automatic", false, false)]
    [InlineData("visited", false, false)]
    [InlineData("trust", false, true)]
    [InlineData("chance", false, true)]
    [InlineData("dead", false, true)]
    [InlineData("free", false, true)]
    public void GatesUseCurrentTrustAndHistoryBeforeRandom(string variant, bool expected, bool draws)
    {
        var (world, task) = Create(variant == "chance" ? 33 : 32);
        switch (variant)
        {
            case "place": task = task with { Journey = new(2, null, 0) }; break;
            case "automatic": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
            case "visited": world.Replace(world.Forces[1] with { EventHistory = [Definition.HistoryKey] }); break;
            case "trust": world.Replace(world.Forces[1] with { Trust = 50, BaseTrust = 1000 }); break;
            case "dead": world.Replace(world.Officers[3] with { Status = OfficerStatus.Dead }); break;
            case "free": world.Replace(world.Officers[3] with { Status = OfficerStatus.Free }); break;
        }
        var force = world.Forces[1]; var actor = world.Officers[2];
        var random = new StableRandom(world.Simulation.RandomStreams.Single().State); if (draws) random.NextUInt64();
        var events = new List<DomainEvent>();
        Assert.Equal(expected, DivinerVisit.TryResolve(world, task, Definition, events));
        Assert.Equal(random.State, world.Simulation.RandomStreams.Single().State);
        Assert.Equal(actor, world.Officers[2]); Assert.Empty(world.Simulation.ConsumedEventKeys);
        if (expected)
        {
            Assert.Equal(force with { EventHistory = world.Forces[1].EventHistory }, world.Forces[1]);
            Assert.Equal(Definition.HistoryKey, Assert.Single(world.Forces[1].EventHistory));
            Assert.Single(events.OfType<DivinerVisitResolvedEvent>());
        }
        else { Assert.Equal(force, world.Forces[1]); Assert.Empty(events); }
    }

    [Fact]
    public void EachForceVisitsIndependentlyAndRepeatDoesNotRollAgain()
    {
        var (world, task) = Create(); var random = world.Simulation.RandomStreams;
        world.SetSimulation(world.Simulation with { ConsumedEventKeys = [Definition.HistoryKey] });
        Assert.True(DivinerVisit.TryResolve(world, task, Definition, []));
        var after = world.Simulation.RandomStreams;
        Assert.False(DivinerVisit.TryResolve(world, task, Definition, [])); Assert.Equal(after, world.Simulation.RandomStreams);
        Assert.Empty(world.Forces[2].EventHistory);
        world.SetSimulation(world.Simulation with { RandomStreams = random });
        Assert.True(DivinerVisit.TryResolve(world, task with { ForceId = 2, OfficerId = 4, OriginSettlementId = 2 }, Definition, []));
        Assert.Contains(Definition.HistoryKey, world.Forces[2].EventHistory);
    }
}
