using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SageVisitTests
{
    private static SageVisitSearchEventDefinition Definition => new(1, 3, [4, 5], 6, 75, 80, 3)
        { Id = "sage", Consumption = EventConsumption.None };

    private static (WorldMutation World, PersistentTask Task) Create(int roll = 0)
    {
        var world = WorldState.Create(new(190, 1, 1), 123,
            [new(1, 1000) { LeaderId = 1, BaseTrust = 76 }, new(2, 100) { LeaderId = 6 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(50, 99, 99, 50), false),
             new(3, null, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special },
             new OfficerState(4, 2, 2, new(50, 50, 50, 50), false).WithLoyalty(2, 100).WithLoyalty(1, 80),
             new OfficerState(5, null, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered },
             new(6, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
            controls: [new("player", 1, ControllerKind.Player), new("ai", 2, ControllerKind.Automatic)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(0, 0)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 6, new(0, 0)) { IsRoot = true }]).BeginMutation();
        ulong seed = TestSeeds.StateForRoll(roll);
        world.SetSimulation(world.Simulation with { RandomStreams = [new("event-search-eligibility", seed)] });
        return (world, new(Guid.NewGuid(), "personnel-search", "player", 1, 2, 1, world.Clock, world.Clock, 0, 0)
            { Journey = new(1, null, 0) });
    }

    [Theory]
    [InlineData("eligible", true, true)]
    [InlineData("place", false, false)]
    [InlineData("automatic", false, false)]
    [InlineData("trust", false, true)]
    [InlineData("chance", false, true)]
    [InlineData("dead", false, true)]
    [InlineData("free", false, true)]
    public void GatesPreserveRandomOrderAndRequireSpecialIdentity(string variant, bool expected, bool draws)
    {
        var (world, task) = Create(variant == "chance" ? 16 : 15);
        switch (variant)
        {
            case "place": task = task with { Journey = new(2, null, 0) }; break;
            case "automatic": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
            case "trust": world.Replace(world.Forces[1] with { BaseTrust = 75 }); break;
            case "dead": world.Replace(world.Officers[3] with { Status = OfficerStatus.Dead }); break;
            case "free": world.Replace(world.Officers[3] with { Status = OfficerStatus.Free }); break;
        }
        var recipient = world.Officers[4]; var force = world.Forces[1];
        var random = new StableRandom(world.Simulation.RandomStreams.Single().State);
        if (draws) random.NextUInt64();
        var events = new List<DomainEvent>();
        Assert.Equal(expected, SageVisit.TryResolve(world, task, Definition, events));
        Assert.Equal(random.State, world.Simulation.RandomStreams.Single().State);
        Assert.Equal(force, world.Forces[1]); Assert.Empty(world.Simulation.ConsumedEventKeys);
        if (expected)
        {
            Assert.Equal(83, world.Officers[4].LoyaltyTo(1)); Assert.Equal(100, world.Officers[4].LoyaltyTo(2));
            Assert.Equal(recipient with { ForceLoyalties = world.Officers[4].ForceLoyalties }, world.Officers[4]);
            Assert.Equal(new[] { 4, 5 }, Assert.Single(events.OfType<SageVisitResolvedEvent>()).LoyaltyRecipientIds);
        }
        else { Assert.Equal(recipient, world.Officers[4]); Assert.Empty(events); }
    }

    [Theory]
    [InlineData(OfficerStatus.Serving)]
    [InlineData(OfficerStatus.Captive)]
    [InlineData(OfficerStatus.Free)]
    [InlineData(OfficerStatus.Undiscovered)]
    [InlineData(OfficerStatus.NotAppeared)]
    [InlineData(OfficerStatus.Dead)]
    [InlineData(OfficerStatus.Special)]
    public void RecommendationsDoNotRequireServiceAllegianceLocationOrLife(OfficerStatus status)
    {
        var (world, task) = Create();
        world.Replace(world.Officers[4] with { Status = status });
        Assert.True(SageVisit.TryResolve(world, task, Definition, []));
        Assert.Equal(83, world.Officers[4].LoyaltyTo(1)); Assert.Equal(status, world.Officers[4].Status);
        Assert.Equal(2, world.Officers[4].OwnerId); Assert.Equal(2, world.Officers[4].SettlementId);
    }

    [Fact]
    public void RepeatedNoRewardVisitStillEndsSearchAndDoesNotConsumeLaterRandom()
    {
        var (world, task) = Create(); var random = world.Simulation.RandomStreams;
        Assert.True(SageVisit.TryResolve(world, task, Definition, []));
        world.Replace(world.Officers[5].WithLoyalty(1, 81));
        world.SetSimulation(world.Simulation with { RandomStreams = random });
        var later = new WomenVisitSearchEventDefinition([new(1, 3, [101], [], null, null, null)], 1, 0, 1001)
            { Id = "later", Priority = -1, Consumption = EventConsumption.None };
        var rules = new SimulationRules { Personnel = new([new(1, new(0, 0))], searchEvents: [later, Definition]) };
        var events = new List<DomainEvent>();
        Assert.True(SearchEvents.Prepare(world, task, rules, events, out var encounter));
        Assert.Null(encounter); Assert.Empty(Assert.Single(events.OfType<SageVisitResolvedEvent>()).LoyaltyRecipientIds);
        Assert.DoesNotContain(events, e => e is WomenVisitResolvedEvent or OfficerLoyaltyChangedEvent);
        Assert.Equal(83, world.Officers[4].LoyaltyTo(1)); Assert.Empty(world.Simulation.ConsumedEventKeys);
        var expected = new StableRandom(random.Single().State); expected.NextUInt64();
        Assert.Equal(expected.State, world.Simulation.RandomStreams.Single().State);
    }

    [Fact]
    public void DormantRecommendationsRemainUntouchedAndLargeAuthoredRewardSaturates()
    {
        var (world, task) = Create();
        world.Replace(world.Officers[5] with { IsDormant = true });
        var before = world.AllOfficers[5];
        Assert.True(SageVisit.TryResolve(world, task, Definition with { LoyaltyReward = int.MaxValue }, []));
        Assert.Equal(255, world.Officers[4].LoyaltyTo(1)); Assert.Equal(before, world.AllOfficers[5]);
    }
}
