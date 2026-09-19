using System.Collections.Immutable;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class EventSelectionContractTests
{
    [Fact]
    public void ConsumingCandidateRequiresAValidKey()
    {
        Assert.Throws<ArgumentException>(() => new EventCandidate("history", EventTrigger.Quarter, 1, EventConsumption.OnSuccess));
        Assert.Throws<ArgumentException>(() => new EventCandidate("history", EventTrigger.Quarter, 1, EventConsumption.None, new("history")));
        Assert.Throws<ArgumentException>(() => new EventCandidate("history", EventTrigger.Quarter, 1, EventConsumption.OnPresentation, default(PersistentEventKey)));
    }

    [Fact]
    public void OrderingIsStableAndConsumedCandidatesAreExcluded()
    {
        var consumed = new HashSet<PersistentEventKey> { new("used") };
        var result = EventSelection.Order([
            new EventCandidate("zeta", EventTrigger.Quarter, 2),
            new EventCandidate("alpha", EventTrigger.Quarter, 2),
            new EventCandidate("used", EventTrigger.Quarter, 9, EventConsumption.OnSuccess, new("used")),
            new EventCandidate("search", EventTrigger.Search, 99)], EventTrigger.Quarter, consumed);

        Assert.Equal(["alpha", "zeta"], result.Select(c => c.Id));
    }

    [Theory]
    [InlineData(EventConsumption.None, EventResult.Succeeded, false)]
    [InlineData(EventConsumption.None, EventResult.NoReward, false)]
    [InlineData(EventConsumption.OnSuccess, EventResult.Succeeded, true)]
    [InlineData(EventConsumption.OnSuccess, EventResult.Declined, false)]
    [InlineData(EventConsumption.OnSuccess, EventResult.Escaped, false)]
    [InlineData(EventConsumption.OnSuccess, EventResult.NoReward, false)]
    [InlineData(EventConsumption.OnSuccess, EventResult.Defeated, false)]
    [InlineData(EventConsumption.OnPresentation, EventResult.Succeeded, true)]
    [InlineData(EventConsumption.OnPresentation, EventResult.Declined, true)]
    [InlineData(EventConsumption.OnPresentation, EventResult.Escaped, true)]
    [InlineData(EventConsumption.OnPresentation, EventResult.NoReward, true)]
    [InlineData(EventConsumption.OnPresentation, EventResult.Defeated, true)]
    public void ConsumptionFollowsDeclaredPolicy(EventConsumption policy, EventResult result, bool consumes)
    {
        var candidate = new EventCandidate("history", EventTrigger.Search, 1, policy,
            policy == EventConsumption.None ? null : new PersistentEventKey("history"));
        var keys = EventSelection.AddConsumedKey([], candidate,
            new("history", EventOutcomeKind.Completed, false, result));
        Assert.Equal(consumes, keys.Contains(new("history")));
    }

    [Theory]
    [InlineData(EventOutcomeKind.Failed)]
    [InlineData(EventOutcomeKind.Invalidated)]
    public void UncommittedResultsNeverConsumeEvenWithPresentationPolicy(EventOutcomeKind kind)
    {
        var candidate = new EventCandidate("history", EventTrigger.Search, 1, EventConsumption.OnPresentation, new("history"));
        Assert.Empty(EventSelection.AddConsumedKey([], candidate, new("history", kind, false)));
        Assert.Throws<ArgumentException>(() => new EventOutcome("history", kind, false, EventResult.Succeeded));
    }

    [Fact]
    public void ForeignOutcomeDoesNotMutateConsumption()
    {
        var candidate = new EventCandidate("history", EventTrigger.Quarter, 1, EventConsumption.OnSuccess, new("history"));
        var keys = new HashSet<PersistentEventKey>();
        Assert.Throws<ArgumentException>(() => EventSelection.Consume(candidate,
            new("other", EventOutcomeKind.Completed, false, EventResult.Succeeded), keys));
        Assert.Empty(keys);
    }

    [Fact]
    public void ConsumptionPublishesWithWorldCommitAndSurvivesRestoration()
    {
        var world = WorldState.Create(new(190, 1, 1), 1);
        _ = new SimulationEngine(world);
        var before = world.CreateSnapshot();
        var candidate = new EventCandidate("history", EventTrigger.Search, 1, EventConsumption.OnPresentation, new("history"));
        var mutation = world.BeginMutation();
        mutation.ConsumeEvent(candidate, new("history", EventOutcomeKind.Completed, false, EventResult.Escaped));
        Assert.Empty(world.Simulation.ConsumedEventKeys);
        world.Commit(mutation);
        Assert.Single(world.Simulation.ConsumedEventKeys);
        Assert.NotEqual(before.StateHash, world.CreateSnapshot().StateHash);
        var restored = WorldState.Restore(world.CreateSnapshot());
        Assert.Empty(EventSelection.Order([candidate], EventTrigger.Search, restored.Simulation.ConsumedEventKeys.ToHashSet()));
    }

    [Fact]
    public void InvalidWorldEffectDoesNotPublishItsConsumptionKey()
    {
        var world = WorldState.Create(new(190, 1, 1), 1, [new(1, 0, 0, 100)], [], []);
        var engine = new SimulationEngine(world);
        string before = engine.Snapshot.StateHash;
        var mutation = world.BeginMutation();
        mutation.ConsumeEvent(new("history", EventTrigger.Search, 1, EventConsumption.OnSuccess, new("history")),
            new("history", EventOutcomeKind.Completed, false, EventResult.Succeeded));
        mutation.Replace(mutation.Forces[1] with { Trust = -1 });
        Assert.Throws<ArgumentException>(() => world.Commit(mutation));
        Assert.Empty(world.Simulation.ConsumedEventKeys);
        Assert.Equal(before, world.CreateSnapshot().StateHash);
    }

    [Fact]
    public void RestoreRejectsBlankDuplicateOrUnorderedKeys()
    {
        var snapshot = new SimulationEngine(WorldState.Create(new(190, 1, 1), 1)).CaptureStableState();
        ImmutableArray<PersistentEventKey>[] invalid = [default, [default], [new("same"), new("same")], [new("z"), new("a")]];
        foreach (var keys in invalid)
            Assert.Throws<ArgumentException>(() => WorldState.Restore(snapshot with
            { Simulation = snapshot.Simulation with { ConsumedEventKeys = keys } }));
    }
}
