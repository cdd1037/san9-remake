using San9.Tests;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PresentedAdvanceTests
{
    private sealed class FailingRules : SimulationRules
    {
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
        {
            if (stage == ResolutionStage.BeforeDailyCommands && world.Clock.Day == 4)
                throw new InvalidOperationException("fixture failure after sampled days");
        }
    }

    [Fact]
    public void FailedTransactionDiscardsDailyFramesAndWorldChanges()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 190, world => new(world, new FailingRules()));
        var before = session.Capture();
        var advance = session.AdvanceForPresentation();
        Assert.Equal(SessionDisposition.Rejected, advance.Result.Disposition);
        Assert.Empty(advance.Frames);
        StateAssert.Equal(before, session.Capture());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 21)]
    [InlineData(12, 21)]
    public void DailyPresentationDoesNotChangeSettlementEventsOrRandomState(int month, int day)
    {
        var definition = G01ScenarioTests.Fixture() with { Month = month, Day = day };
        var plain = GameSession.Start(definition, 190);
        var presented = GameSession.Start(definition, 190);
        var start = presented.Snapshot.Clock;
        var expected = plain.AdvanceXun();
        var actual = presented.AdvanceForPresentation();
        Assert.Equal(SessionDisposition.Accepted, actual.Result.Disposition);
        StateAssert.Equal(plain.Capture(), presented.Capture());
        EventAssert.Equal(expected.Events, actual.Result.Events);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => start.DayNumber + i), actual.Frames.Select(f => f.World.Clock.DayNumber));
        var first = actual.Frames[0].World;
        presented.AdvanceXun();
        StateAssert.Equal(first, actual.Frames[0].World);
    }

    [Fact]
    public void RejectedAdvanceDoesNotPublishPresentationFrames()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 190);
        var save = session.Capture();
        var terminal = save with { World = save.World with { Simulation = save.World.Simulation with
            { Boundary = SimulationBoundary.Terminal, TerminalReason = "fixture-end" } } };
        session = GameSession.Restore(G01ScenarioTests.Fixture(), terminal);
        var result = session.AdvanceForPresentation();
        Assert.Equal(SessionRejection.Terminal, result.Result.Rejection);
        Assert.Empty(result.Frames);
        StateAssert.Equal(terminal, session.Capture());
    }
}
