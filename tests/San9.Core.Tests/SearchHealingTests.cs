using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SearchHealingTests
{
    private static HealingSearchEventDefinition Definition => new(99, [10], 6)
        { Id = "physician", Priority = 1, Consumption = EventConsumption.None };
    private static PersonnelRulebook Rules(params SearchEventDefinition[] entries) => new(
        [new(1, new(0, 0)), new(2, new(100, 0)), new(3, new(200, 0))],
        new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }, searchEvents: entries);
    private static WorldState World(bool automatic = false) => WorldState.Create(new(190, 1, 1), 1,
        [new(1, 10000, 10000, 100) { LeaderId = 1 }, new(2, 10000, 10000, 100) { LeaderId = 10 }],
        [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100),
         new(3, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(2, 1, 1, new(600, 50, 500, 50), false),
         new(3, 1, 3, new(50, 50, 50, 50), false) { Injury = 3, LifespanTier = 4 },
         new(4, 1, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive, Injury = 7 },
         new(5, 2, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive, Injury = 7 },
         new(6, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered },
         new(10, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(99, null, null, default, false) { Status = OfficerStatus.Special }],
        controls: [new("player", 1, automatic ? ControllerKind.Automatic : ControllerKind.Player)]);
    private static PersistentTask Task(int destination = 1) => new(Guid.NewGuid(), "personnel-search", "player", 1, 2, 1,
        new(190, 1, 1), new(190, 1, 10), 9, 0)
        { Journey = new(destination, null, 0) { Route = new(new(0, 0), new(0, 0), false) } };

    [Fact]
    public void HealingUsesOriginalCaptiveAllegianceAndStopsOrdinarySearch()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { NextSequence = 1 });
        List<DomainEvent> events = [];
        var rule = new PersonnelMissionRule(new() { Personnel = Rules(Definition) }, "personnel-search");
        Assert.Null(rule.Resolve(mutation, Task(), null, events));
        var healed = Assert.Single(events.OfType<SearchHealingResolvedEvent>());
        Assert.Equal(new[] { 3, 4 }, healed.HealedOfficerIds);
        Assert.Equal(0, mutation.Officers[3].Injury);
        Assert.Equal(0, mutation.Officers[4].Injury);
        Assert.Equal(7, mutation.Officers[5].Injury);
        Assert.Equal(4, mutation.Officers[3].LifespanTier);
        Assert.Equal(OfficerStatus.Undiscovered, mutation.Officers[6].Status);
        Assert.DoesNotContain(events, e => e is SearchResolvedEvent or OfficerAbilityGrownEvent);
        Assert.Empty(mutation.Simulation.ConsumedEventKeys);
        world.Commit(mutation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedCandidateContinuesToNextDefinition(bool excluded)
    {
        var mutation = World().BeginMutation();
        var unavailable = excluded ? Definition with { ExcludedSearchingOfficerIds = [2] } : Definition with { HealerOfficerId = 6 };
        if (!excluded) mutation.Replace(mutation.Officers[6] with { Status = OfficerStatus.Dead });
        List<DomainEvent> events = [];
        Assert.True(SearchEvents.Prepare(mutation, Task(), Rules(unavailable,
            Definition with { Id = "next", Priority = 0 }), events, out _));
        Assert.Equal("next", Assert.Single(events.OfType<SearchHealingResolvedEvent>()).EventId);
        var random = new StableRandom(StableRandom.DeriveSeed(1, "event-search-eligibility"));
        random.NextUInt64(); random.NextUInt64();
        Assert.Equal(random.State, Assert.Single(mutation.Simulation.RandomStreams).State);
    }

    [Fact]
    public void UnownedLocationSkipsProbabilityAndNoInjuredPeopleDoNotConsumeKey()
    {
        var mutation = World().BeginMutation();
        List<DomainEvent> events = [];
        Assert.False(SearchEvents.Prepare(mutation, Task(2), Rules(Definition), events, out _));
        Assert.Empty(mutation.Simulation.RandomStreams);
        foreach (var person in mutation.Officers.Values.ToArray()) mutation.Replace(person with { Injury = 0 });
        Assert.False(SearchEvents.Prepare(mutation, Task(), Rules(Definition with
            { Consumption = EventConsumption.OnSuccess, ConsumptionKey = new("healing") }), events, out _));
        Assert.Empty(events);
        Assert.Empty(mutation.Simulation.ConsumedEventKeys);
        Assert.Single(mutation.Simulation.RandomStreams);
    }

    [Fact]
    public void AutomaticSearchSkipsSpecialCandidatesWithoutDrawingTheirRandom()
    {
        var mutation = World(automatic: true).BeginMutation();
        List<DomainEvent> events = [];
        var rule = new PersonnelMissionRule(new() { Personnel = Rules(Definition) }, "personnel-search");
        Assert.NotNull(rule.Resolve(mutation, Task(), null, events));
        Assert.DoesNotContain(mutation.Simulation.RandomStreams, s => s.Name == "event-search-eligibility");
        Assert.DoesNotContain(events, e => e is SearchHealingResolvedEvent);
    }

    [Fact]
    public void DelegatedCorpsOfPlayerForceUsesOrdinarySearchOnly()
    {
        var world = World();
        var original = new SimulationEngine(world).CaptureStableState();
        var delegated = WorldState.Restore(original with
        {
            Corps = [new(7, 1, 1, new(1000, 1000)) { ControlMode = CorpsControlMode.Automatic, Delegation = new GeneralDelegation() }],
            Settlements = original.Settlements.Select(s => s.Id == 1 ? s with { CorpsId = 7 } : s).ToImmutableArray()
        });
        var mutation = delegated.BeginMutation();
        List<DomainEvent> events = [];
        var rule = new PersonnelMissionRule(new() { Personnel = Rules(Definition) }, "personnel-search");
        Assert.NotNull(rule.Resolve(mutation, Task(), null, events));
        Assert.DoesNotContain(mutation.Simulation.RandomStreams, s => s.Name == "event-search-eligibility");
        Assert.DoesNotContain(events, e => e is SearchHealingResolvedEvent);
    }

    [Fact]
    public void AuthoredConsumptionPreventsAnotherHealingAndOrdinaryContentCanRepeat()
    {
        var mutation = World().BeginMutation();
        var rules = Rules(Definition with { Consumption = EventConsumption.OnSuccess, ConsumptionKey = new("once") });
        List<DomainEvent> events = [];
        Assert.True(SearchEvents.Prepare(mutation, Task(), rules, events, out _));
        Assert.Equal("once", Assert.Single(mutation.Simulation.ConsumedEventKeys).Value);
        mutation.Replace(mutation.Officers[3] with { Injury = 2 });
        Assert.False(SearchEvents.Prepare(mutation, Task(), rules, events, out _));
        Assert.Equal(2, mutation.Officers[3].Injury);
        Assert.True(SearchEvents.Prepare(mutation, Task(), Rules(Definition), events, out _));
        Assert.Equal(0, mutation.Officers[3].Injury);
    }

    [Fact]
    public void ProbabilityFailureKeepsWoundsAndFallsThroughToOrdinarySearch()
    {
        var mutation = World().BeginMutation();
        List<DomainEvent> events = [];
        var rule = new PersonnelMissionRule(new() { Personnel = Rules(Definition with { WarChanceDivisor = 1000 }) }, "personnel-search");
        Assert.NotNull(rule.Resolve(mutation, Task(), null, events));
        Assert.Equal(3, mutation.Officers[3].Injury);
        Assert.DoesNotContain(events, e => e is SearchHealingResolvedEvent);
    }
}
