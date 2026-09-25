using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ImperialCeremonyTests
{
    private static ImperialCeremonySearchEventDefinition Definition => new(1, 99, 5, 50, 10, new("ceremony"))
        { Id = "ceremony", Priority = 10, Consumption = EventConsumption.OnSuccess };
    private static WorldMutation World()
    {
        var world = WorldState.Create(new(190, 2, 1), 17,
            [new(1, 999) { LeaderId = 1, RankId = 99, BaseTrust = 999 },
             new(2, 500) { LeaderId = 10, RankId = 5, BaseTrust = 500 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(98, 100, 120, 10), false) { Status = OfficerStatus.Ruler },
             new OfficerState(2, 1, 1, new(50, 50, 50, 50), false).WithLoyalty(1, 250),
             new OfficerState(3, 1, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive }.WithLoyalty(1, 100),
             new(4, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special },
             new(10, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
            controls: [new("player", 1, ControllerKind.Player), new("ai", 2, ControllerKind.Automatic)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(0, 0)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 10, new(0, 0)) { IsRoot = true }]);
        var m = world.BeginMutation(); m.SetSimulation(m.Simulation with { Boundary = SimulationBoundary.Stable }); return m;
    }
    private static PersistentTask Task() => new(Guid.NewGuid(), "personnel-search", "player", 1, 1, 1,
        new(190, 2, 1), new(190, 2, 1), 0, 0)
        { Journey = new(1, null, 0) { Route = new(new(0, 0), new(0, 0), false) } };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CeremonyNeedsNoCourtEndDateFundsOrRandomAndRewardsOnlyEligiblePeople(bool courtEnded)
    {
        var m = World(); m.SetSimulation(m.Simulation with { ImperialCourtEnded = courtEnded });
        List<DomainEvent> events = [];
        Assert.True(ImperialCeremony.TryResolve(m, Task(), Definition, EconomyRulebook.Empty, events));
        Assert.Equal(new OfficerAbilities(100, 100, 120, 15), m.Officers[1].BaseAbilities);
        Assert.Equal(1000, m.Forces[1].BaseTrust); Assert.Equal(0u, m.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Money);
        Assert.Equal(255, m.Officers[2].LoyaltyTo(1)); Assert.Equal(100, m.Officers[3].LoyaltyTo(1));
        Assert.Equal(2, Assert.Single(Assert.Single(events.OfType<ImperialCeremonyResolvedEvent>()).LoyaltyRecipientIds));
        Assert.Empty(m.Simulation.RandomStreams); Assert.Equal(Definition.ConsumptionKey, Assert.Single(m.Simulation.ConsumedEventKeys));
        int count = events.Count;
        Assert.False(ImperialCeremony.TryResolve(m, Task(), Definition, EconomyRulebook.Empty, events));
        Assert.Equal(count, events.Count);
    }

    [Theory]
    [InlineData("rank")]
    [InlineData("place")]
    [InlineData("officer")]
    [InlineData("control")]
    [InlineData("consumed")]
    public void FailedQualificationHasNoSideEffects(string failure)
    {
        var m = World(); var task = Task();
        switch (failure)
        {
            case "rank": m.Replace(m.Forces[1] with { RankId = 5 }); break;
            case "place": task = task with { Journey = task.Journey! with { DestinationPlaceId = 2 } }; break;
            case "officer": task = task with { OfficerId = 2 }; break;
            case "control": m.SetSimulation(m.Simulation with { Controls = m.Simulation.Controls.Select(c => c with { Kind = ControllerKind.Automatic }).ToImmutableArray() }); break;
            case "consumed": m.SetSimulation(m.Simulation with { ConsumedEventKeys = [Definition.ConsumptionKey] }); break;
        }
        var ruler = m.Officers[1]; var force = m.Forces[1]; List<DomainEvent> events = [];
        Assert.False(ImperialCeremony.TryResolve(m, task, Definition, EconomyRulebook.Empty, events));
        Assert.Empty(events); Assert.Empty(m.Simulation.RandomStreams); Assert.Equal(ruler, m.Officers[1]); Assert.Equal(force, m.Forces[1]);
    }

    [Fact]
    public void SaturatedRewardsStillConsumeAndEarlierFailureCanRetry()
    {
        var m = World(); var task = Task(); List<DomainEvent> events = [];
        m.Replace(m.Forces[1] with { RankId = 5 });
        Assert.False(ImperialCeremony.TryResolve(m, task, Definition, EconomyRulebook.Empty, events));
        m.Replace(m.Forces[1] with { RankId = 99, BaseTrust = 1000 });
        m.Replace(m.Officers[1] with { BaseAbilities = new(100, 100, 100, 100) });
        m.Replace(m.Officers[2].WithLoyalty(1, 255));
        Assert.True(ImperialCeremony.TryResolve(m, task, Definition, EconomyRulebook.Empty, events));
        Assert.DoesNotContain(events, e => e is OfficerAbilityGrownEvent);
        Assert.Single(m.Simulation.ConsumedEventKeys);
    }

    [Fact]
    public void CeremonyPrecedesHealingAndReturnsSearcherWithoutOrdinaryOutcome()
    {
        var m = World(); m.Replace(m.Officers[1] with { Injury = 3 });
        var rules = new SimulationRules
        {
            Personnel = new([new(1, new(0, 0))], searchEvents: [Definition,
                new HealingSearchEventDefinition(4, [], 1) { Id = "healing", Priority = 0 }])
        };
        List<DomainEvent> events = [];
        Assert.Null(new PersonnelMissionRule(rules, "personnel-search").Resolve(m, Task(), null, events));
        Assert.Single(events.OfType<ImperialCeremonyResolvedEvent>());
        Assert.DoesNotContain(events, e => e is SearchHealingResolvedEvent or SearchResolvedEvent);
        Assert.Equal(3, m.Officers[1].Injury); Assert.Empty(m.Simulation.RandomStreams);
    }
}
