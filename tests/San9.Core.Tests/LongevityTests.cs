using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class LongevityTests
{
    private static LongevitySearchEventDefinition Definition => new(1, 3, new("visited"), 3, 2500, 500, 7, true)
        { Id = "longevity", Consumption = EventConsumption.None };

    private static (WorldMutation World, PersistentTask Task) Create(int roll = 0)
    {
        var world = WorldState.Create(new(190, 1, 1), 123,
            [new(1, 2501, 0, 51) { LeaderId = 1, BaseTrust = 0, EventHistory = [new("visited")] }, new(2, 0, 0, 51) { LeaderId = 4, BaseTrust = 0 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(99, 50, 50, 50), false),
             new(3, null, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special },
             new(4, 2, 2, new(99, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
            controls: [new("first", 1, ControllerKind.Player), new("second", 2, ControllerKind.Player)]).BeginMutation();
        ulong seed = Enumerable.Range(1, 10000).Select(i => (ulong)i).First(s => new StableRandom(s).NextUInt64() % 100 == (ulong)roll);
        world.SetSimulation(world.Simulation with { RandomStreams = [new("event-search-eligibility", seed)] });
        return (world, new(Guid.NewGuid(), "personnel-search", "first", 1, 2, 1, world.Clock, world.Clock, 0, 0)
            { Journey = new(1, null, 0) });
    }

    private static SimulationRules Rules(LongevitySearchEventDefinition? d = null) => new()
        { Personnel = new([new(1, new(0, 0)), new(2, new(0, 0))], searchEvents: [d ?? Definition]) };

    [Theory]
    [InlineData("eligible", true, true)]
    [InlineData("place", false, false)]
    [InlineData("automatic", false, false)]
    [InlineData("no-history", false, false)]
    [InlineData("global-history", false, false)]
    [InlineData("chance", false, true)]
    [InlineData("funds", false, true)]
    [InlineData("dead", false, true)]
    [InlineData("free", false, true)]
    [InlineData("disabled", false, true)]
    public void GatesKeepTheSourceProbabilityOrder(string variant, bool expected, bool draws)
    {
        var (world, task) = Create(variant == "chance" ? 33 : 32); var d = Definition;
        switch (variant)
        {
            case "place": task = task with { Journey = new(2, null, 0) }; break;
            case "automatic": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
            case "no-history": world.Replace(world.Forces[1] with { EventHistory = [] }); break;
            case "global-history":
                world.Replace(world.Forces[1] with { EventHistory = [] });
                world.SetSimulation(world.Simulation with { ConsumedEventKeys = [d.RequiredHistoryKey] }); break;
            case "funds": world.Replace(world.Forces[1] with { Money = 2500 }); break;
            case "dead": world.Replace(world.Officers[3] with { Status = OfficerStatus.Dead }); break;
            case "free": world.Replace(world.Officers[3] with { Status = OfficerStatus.Free }); break;
            case "disabled": d = d with { LifespanChangesAllowed = false }; break;
        }
        var random = new StableRandom(world.Simulation.RandomStreams.Single().State); if (draws) random.NextUInt64();
        Assert.Equal(expected, Longevity.Prepare(world, task, d));
        Assert.Equal(random.State, world.Simulation.RandomStreams.Single().State);
        if (expected)
        {
            Assert.True(Longevity.CanResolve(world, task, Rules(d), new(d.Id)));
            Assert.Equal(random.State, world.Simulation.RandomStreams.Single().State);
        }
    }

    [Theory]
    [InlineData(0, true, 7)]
    [InlineData(7, true, 7)]
    [InlineData(12, true, 12)]
    [InlineData(0, false, 0)]
    public void AcceptanceExtendsWithoutShorteningAuthoredLifeAndDoesNotConsumeHistory(int before, bool accept, int after)
    {
        var (world, task) = Create(); var rules = Rules();
        world.Replace(world.Officers[2] with { LifespanTier = before });
        List<DomainEvent> events = [];
        Longevity.Resolve(world, task, rules, new(Definition.Id), accept, events);
        Assert.Equal(after, world.Officers[2].LifespanTier);
        Assert.Equal(accept ? 2001u : 2501u, world.Forces[1].Money);
        Assert.Equal(Definition.RequiredHistoryKey, Assert.Single(world.Forces[1].EventHistory));
        Assert.Empty(world.Simulation.ConsumedEventKeys);
        Assert.Equal((accept, before, after), (Assert.Single(events.OfType<LongevityResolvedEvent>()).Accepted,
            events.OfType<LongevityResolvedEvent>().Single().PreviousLifespanTier, events.OfType<LongevityResolvedEvent>().Single().LifespanTier));
        world.Replace(world.Forces[1] with { Money = 2501 });
        Assert.True(Longevity.CanResolve(world, task, rules, new(Definition.Id)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(500)]
    public void IndependentCorpsQualifiesButPaymentSaturatesTheRootAccount(uint rootMoney)
    {
        var (world, task) = Create();
        world.AddCorps(new CorpsState(7, 1, 2, new(2501, 0)) { ControlMode = CorpsControlMode.Direct });
        world.Replace(world.Settlements[1] with { CorpsId = 7 });
        world.Replace(world.Forces[1] with { Money = rootMoney });
        Assert.True(Longevity.Prepare(world, task, Definition)); List<DomainEvent> events = [];
        Longevity.Resolve(world, task, Rules(), new(Definition.Id), true, events);
        var payment = Assert.Single(events.OfType<ResourceChangedEvent>());
        Assert.Equal(new ResourceAccount(ResourceOwnerKind.Force, 1), payment.Account);
        Assert.Equal((-(long)rootMoney, 500L - rootMoney), (payment.Actual, payment.Shortfall));
        Assert.Equal(0u, world.Forces[1].Money); Assert.Equal(2501u, world.Corps[7].Treasury!.Money);
        Assert.Equal(7, world.Officers[2].LifespanTier);
    }

    [Fact]
    public void LostQualificationCannotCommitAnyEffects()
    {
        var (world, task) = Create(); Assert.True(Longevity.Prepare(world, task, Definition));
        world.Replace(world.Forces[1] with { EventHistory = [] }); List<DomainEvent> events = [];
        Assert.False(Longevity.CanResolve(world, task, Rules(), new(Definition.Id)));
        Assert.Throws<InvalidOperationException>(() => Longevity.Resolve(world, task, Rules(), new(Definition.Id), true, events));
        Assert.Empty(events); Assert.Equal(2501u, world.Forces[1].Money); Assert.Equal(0, world.Officers[2].LifespanTier);
    }
}
