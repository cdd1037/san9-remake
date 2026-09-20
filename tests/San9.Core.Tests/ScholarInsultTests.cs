using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ScholarInsultTests
{
    private static ScholarInsultSearchEventDefinition Definition => new(3, 30, 200, 5, 115, 2, 2, 7, 2)
        { Id = "insult", Consumption = EventConsumption.None };

    private static (WorldMutation World, PersistentTask Task) Create(int roll = 0)
    {
        var world = WorldState.Create(new(190, 1, 1), 123,
            [new(1, 2501, 0, 51) { LeaderId = 1, BaseTrust = 0, EventHistory = [new("visited")] }, new(2, 0, 0, 51) { LeaderId = 4, BaseTrust = 0 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(0, 99, 0, 0), false),
             new(3, null, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special },
             new(4, 2, 2, new(0, 99, 0, 0), false) { Status = OfficerStatus.Ruler }],
            controls: [new("first", 1, ControllerKind.Player), new("second", 2, ControllerKind.Player)]).BeginMutation();
        ulong seed = Enumerable.Range(1, 10000).Select(i => (ulong)i).First(s => new StableRandom(s).NextUInt64() % 100 == (ulong)roll);
        world.SetSimulation(world.Simulation with { RandomStreams = [new("event-search-eligibility", seed)] });
        return (world, new(Guid.NewGuid(), "personnel-search", "first", 1, 2, 1, world.Clock, world.Clock, 0, 0)
            { Journey = new(1, null, 0) });
    }

    private static SimulationRules Rules(ScholarInsultSearchEventDefinition? d = null) => new()
        { Personnel = new([new(1, new(0, 0)), new(2, new(0, 0)), new(9, new(0, 0))], new Dictionary<int, int> { [9] = 1 }, searchEvents: [d ?? Definition]) };

    [Theory]
    [InlineData("eligible", true, true)]
    [InlineData("enemy-place", false, false)]
    [InlineData("unbuilt-region", true, true)]
    [InlineData("automatic", false, false)]
    [InlineData("chance", false, true)]
    [InlineData("high-base-trust", false, true)]
    [InlineData("high-current-trust", true, true)]
    [InlineData("severe-injury", false, true)]
    [InlineData("dead", false, true)]
    [InlineData("free", true, true)]
    [InlineData("not-appeared", true, true)]
    public void EligibilityKeepsOwnershipBeforeRandomAndLaterGatesAfterIt(string variant, bool expected, bool draws)
    {
        var (world, task) = Create(variant == "chance" ? 3 : 0);
        switch (variant)
        {
            case "enemy-place": task = task with { Journey = new(2, null, 0) }; break;
            case "unbuilt-region": task = task with { Journey = new(9, null, 0) }; break;
            case "automatic": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
            case "high-base-trust": world.Replace(world.Forces[1] with { BaseTrust = 200, Trust = 0 }); break;
            case "high-current-trust": world.Replace(world.Forces[1] with { BaseTrust = 199, Trust = 1000 }); break;
            case "severe-injury": world.Replace(world.Officers[2] with { Injury = 5 }); break;
            case "dead": world.Replace(world.Officers[3] with { Status = OfficerStatus.Dead }); break;
            case "free": world.Replace(world.Officers[3] with { Status = OfficerStatus.Free }); break;
            case "not-appeared": world.Replace(world.Officers[3] with { Status = OfficerStatus.NotAppeared }); break;
        }
        var random = new StableRandom(world.Simulation.RandomStreams.Single().State); if (draws) random.NextUInt64();
        Assert.Equal(expected, ScholarInsult.Prepare(world, task, Rules().Personnel, Definition));
        Assert.Equal(random.State, world.Simulation.RandomStreams.Single().State);
    }

    [Theory]
    [InlineData(999, 0, 0, false)]
    [InlineData(0, 99, 0, true)]
    [InlineData(999, 99, 4, false)]
    public void ProbabilityUsesEffectiveIntelligence(int war, int intelligence, int injury, bool expected)
    {
        var (world, task) = Create(2);
        world.Replace(world.Officers[2] with { BaseAbilities = new(war, intelligence, 999, 999), Injury = injury });
        Assert.Equal(expected, ScholarInsult.Prepare(world, task, Rules().Personnel, Definition));
    }

    [Theory]
    [InlineData(98, 100, 0)]
    [InlineData(99, 100, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(114, 114, 0)]
    [InlineData(115, 115, 2)]
    [InlineData(150, 150, 2)]
    public void IgnoreUsesBaseIntelligenceAndDoesNotReplaceThe115ThresholdWithTheGrowthCap(int before, int after, int trust)
    {
        var (world, task) = Create();
        world.Replace(world.Officers[2] with { BaseAbilities = new(999, before, 999, 999), Injury = 4 });
        var scholar = world.Officers[3]; List<DomainEvent> events = [];
        ScholarInsult.Resolve(world, task, Rules(), new(Definition.Id), false, events);
        Assert.Equal(after, world.Officers[2].BaseAbilities.Intelligence); Assert.Equal(trust, world.Forces[1].BaseTrust);
        Assert.Equal(4, world.Officers[2].Injury); Assert.Equal(scholar, world.Officers[3]);
        Assert.Empty(world.Simulation.ConsumedEventKeys);
        Assert.True(ScholarInsult.CanResolve(world, task, Rules(), new(Definition.Id)));
        Assert.False(Assert.Single(events.OfType<ScholarInsultResolvedEvent>()).AttemptedExecution);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(199, 197)]
    public void AttemptedExecutionInjuresOnlyTheSearcherAndSaturatesTrustLoss(int before, int after)
    {
        var (world, task) = Create(); world.Replace(world.Forces[1] with { BaseTrust = before });
        var scholar = world.Officers[3]; List<DomainEvent> events = [];
        ScholarInsult.Resolve(world, task, Rules(), new(Definition.Id), true, events);
        Assert.Equal(7, world.Officers[2].Injury); Assert.Equal(after, world.Forces[1].BaseTrust);
        Assert.Equal(scholar, world.Officers[3]); Assert.Equal(99, world.Officers[2].BaseAbilities.Intelligence);
        Assert.Empty(world.Simulation.ConsumedEventKeys);
        Assert.False(ScholarInsult.CanResolve(world, task, Rules(), new(Definition.Id)));
        world.Replace(world.Officers[2] with { Injury = 0 });
        Assert.True(ScholarInsult.CanResolve(world, task, Rules(), new(Definition.Id)));
    }

    [Fact]
    public void LostPlaceOrDeadScholarCannotPartiallyCommit()
    {
        var (world, task) = Create(); Assert.True(ScholarInsult.Prepare(world, task, Rules().Personnel, Definition));
        world.Replace(world.Settlements[1] with { OwnerId = 2 }); List<DomainEvent> events = [];
        Assert.Throws<InvalidOperationException>(() => ScholarInsult.Resolve(world, task, Rules(), new(Definition.Id), true, events));
        Assert.Empty(events); Assert.Equal(0, world.Officers[2].Injury);
        world.Replace(world.Settlements[1] with { OwnerId = 1 });
        world.Replace(world.Officers[3] with { Status = OfficerStatus.Dead });
        Assert.False(ScholarInsult.CanResolve(world, task, Rules(), new(Definition.Id)));
    }
}
