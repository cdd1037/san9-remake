using San9.Tests;
using San9.Application.Persistence;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class IncrementalAdvanceTests
{
    private sealed class BlockingRules(Action entered) : SimulationRules
    {
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events) => entered();
    }
    private static SessionResult Apply(GameSession session, SessionOperation operation)
    {
        var pending = session.StartOperation(session.Request(operation));
        Assert.True(SpinWait.SpinUntil(() => pending.IsCompleted, TimeSpan.FromSeconds(10)));
        var result = pending.Complete();
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        return result;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 21)]
    [InlineData(12, 21)]
    public void BackgroundDaysMatchBatchRulesEventsAndRandomStateAndReplay(int month, int day)
    {
        var definition = G01ScenarioTests.Fixture() with { Month = month, Day = day };
        var batch = GameSession.Start(definition, 190);
        var session = GameSession.Start(definition, 190);
        var start = session.Snapshot.Clock;
        var expected = batch.AdvanceXun();
        List<DomainEvent> events = [.. Apply(session, new PrepareAdvanceOperation()).Events];
        Assert.Equal(start, session.Snapshot.Clock);
        Assert.Equal(session.Snapshot.Simulation.Controls.Length, session.Snapshot.Simulation.ActiveControl);
        for (int i = 1; i <= 10; i++)
        {
            var previous = session.Snapshot;
            events.AddRange(Apply(session, new AdvanceDayOperation()).Events);
            Assert.Equal(start.DayNumber + i, session.Snapshot.Clock.DayNumber);
            Assert.Equal(start.DayNumber + i - 1, previous.Clock.DayNumber);
            if (i == 10) continue;
            Assert.Equal(SimulationBoundary.Advancing, session.Snapshot.Simulation.Boundary);
            Assert.Throws<InvalidOperationException>(() => session.Capture());
            Assert.Equal(SessionDisposition.Rejected, session.Apply(session.Request(new AdvanceOperation())).Disposition);
            Assert.Equal(SessionDisposition.Rejected, session.Apply(session.Request(new PrepareAdvanceOperation())).Disposition);
        }
        StateAssert.Equal(batch.Snapshot, session.Snapshot);
        EventAssert.Equal(expected.Events, events);
    }

    [Fact]
    public void WorkerIsIsolatedAndOnlyOwnerPublicationAdvancesSequence()
    {
        int owner = Environment.CurrentManagedThreadId, workerThread = owner;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        bool block = false;
        var rules = new BlockingRules(() =>
        {
            if (block)
            {
                workerThread = Environment.CurrentManagedThreadId;
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
        });
        int factoryCalls = 0;
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 190, world =>
        {
            factoryCalls++;
            return new(world, rules);
        });
        var before = session.Snapshot;
        block = true;
        var pending = session.StartOperation(session.Request(new AdvanceOperation()));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.NotEqual(owner, workerThread);
            Assert.False(pending.IsCompleted);
            Assert.Same(before, session.Snapshot);
            Assert.Same(before, session.View.World);
            Assert.Equal(0, session.NextOperationSequence);
            Assert.Throws<InvalidOperationException>(() => session.StartOperation(session.Request(new AdvanceDayOperation())));
            Assert.Equal(SessionRejection.NotStable, session.Apply(session.Request(new AdvanceOperation())).Rejection);
            Assert.Throws<InvalidOperationException>(() => session.Capture());
        }
        finally { release.Set(); }
        Assert.True(SpinWait.SpinUntil(() => pending.IsCompleted, TimeSpan.FromSeconds(10)));
        Assert.Same(before, session.Snapshot);
        var preparedView = pending.PreparedView;
        Assert.Equal(SessionDisposition.Accepted, pending.Complete().Disposition);
        Assert.Same(preparedView, session.View);
        Assert.Equal(1, session.NextOperationSequence);
        Assert.Equal(1, factoryCalls);
        Assert.Throws<InvalidOperationException>(() => pending.Complete());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnexpectedCalculationFailurePreservesStateAndAllowsTheNextOperation(bool background)
    {
        bool fail = false;
        var rules = new BlockingRules(() =>
        {
            if (fail) throw new TimeoutException("Injected calculation failure");
        });
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 190, world => new(world, rules));
        session.MarkSaved(session.NextOperationSequence);
        var before = session.Capture();
        var request = session.Request(new AdvanceOperation());
        fail = true;
        if (background)
        {
            var pending = session.StartOperation(request);
            Assert.True(SpinWait.SpinUntil(() => pending.IsCompleted, TimeSpan.FromSeconds(10)));
            Assert.Throws<TimeoutException>(() => pending.Complete());
        }
        else Assert.Throws<TimeoutException>(() => session.Apply(request));
        Assert.Same(before, session.Capture());
        Assert.False(session.HasUnsavedChanges);
        Assert.Equal(0, session.NextOperationSequence);
        fail = false;
        var result = background ? Apply(session, new AdvanceOperation()) : session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Equal(1, session.NextOperationSequence);
        Assert.True(session.HasUnsavedChanges);
    }

    private sealed class FailingRules : SimulationRules
    {
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
        {
            if (stage == ResolutionStage.DailyCommands && world.Clock.Day == 4)
                throw new InvalidOperationException("day failed");
        }
    }

    [Fact]
    public void FailedBatchDiscardsEarlierDaysAndSessionChanges()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 190, world => new(world, new FailingRules()));
        var before = session.Capture();
        var error = Assert.Throws<InvalidOperationException>(() => session.Apply(session.Request(new AdvanceOperation())));
        Assert.Equal("day failed", error.Message);
        StateAssert.Equal(before, session.Capture());
    }

    [Fact]
    public void FailedDayKeepsLastPublishedDayAndReleasesOperationGuard()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 190, world => new(world, new FailingRules()));
        Apply(session, new PrepareAdvanceOperation());
        for (int i = 0; i < 3; i++) Apply(session, new AdvanceDayOperation());
        var previous = session.Snapshot;
        long sequence = session.NextOperationSequence;
        for (int retry = 0; retry < 2; retry++)
        {
            var pending = session.StartOperation(session.Request(new AdvanceDayOperation()));
            Assert.True(SpinWait.SpinUntil(() => pending.IsCompleted, TimeSpan.FromSeconds(10)));
            Assert.Equal("day failed", Assert.Throws<InvalidOperationException>(() => pending.Complete()).Message);
            Assert.Same(previous, session.Snapshot);
            Assert.Equal(sequence, session.NextOperationSequence);
        }
    }
}
