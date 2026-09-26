using San9.Application;
using San9.Content.Scenarios;
using San9.Core.Simulation;

namespace San9.Client.Godot.Tests;

public sealed class TurnPresentationTests
{
    private static GameSession Session(int month = 1, int day = 1)
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "turn", 190, month, day, 5,
            [new(5, "player", 5, 100)], [new(15, "city", 5, 0, 0, 0, 100, 100, 0, [])], [], [])
        { Information = new([], [], [], [new San9.Core.Domain.CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], []) };
        return GameSession.Start(definition, 1);
    }

    private static void Ready(TurnPresentation turn)
    {
        Assert.True(SpinWait.SpinUntil(() => !turn.IsComputing, TimeSpan.FromSeconds(10)));
    }

    private static void Enter(TurnPresentation turn)
    {
        turn.Request(); turn.Confirm(); Ready(turn); turn.Update(2); Ready(turn);
        Assert.Equal(TurnScreen.Playing, turn.Screen);
    }

    private static void Finish(TurnPresentation turn)
    {
        for (int i = 0; i < 15 && turn.Screen != TurnScreen.Strategy; i++)
        { Ready(turn); turn.Update(2); }
    }

    [Fact]
    public void CancelAndRepeatedConfirmationCannotAdvanceTheCalendar()
    {
        var session = Session(); var turn = new TurnPresentation(session);
        string initial = session.Snapshot.StateHash;
        Assert.True(turn.CanBrowseMap);
        turn.Confirm(); turn.Update(10);
        Assert.True(turn.Request()); Assert.False(turn.Request());
        Assert.False(turn.CanBrowseMap);
        turn.Cancel(); turn.Update(10);
        Assert.True(turn.CanBrowseMap);
        Assert.Equal(initial, session.Snapshot.StateHash);
        Assert.True(turn.Request()); turn.Confirm(); turn.Confirm();
        turn.Update(0.2);
        Assert.Equal(initial, session.Snapshot.StateHash);
        Ready(turn); turn.Update(2);
        Assert.Equal(1, session.NextOperationSequence);
        Assert.False(turn.Request());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(12, 21)]
    public void PausedPlaybackKeepsItsFrameAndReturnsToNextStrategyBoundary(int month, int day)
    {
        var session = Session(month, day); var start = session.Snapshot.Clock;
        var turn = new TurnPresentation(session);
        Enter(turn);
        Assert.False(turn.CanBrowseMap);
        turn.Update(0.4);
        var frame = turn.Display;
        string settled = session.Snapshot.StateHash;
        turn.TogglePause(); turn.Update(100);
        Assert.True(turn.CanBrowseMap);
        Assert.False(turn.CanAdvance);
        Assert.Same(frame, turn.Display);
        Assert.Equal(settled, session.Snapshot.StateHash);
        turn.TogglePause();
        Assert.False(turn.CanBrowseMap);
        Finish(turn);
        Assert.Equal(TurnScreen.Strategy, turn.Screen);
        Assert.True(turn.CanBrowseMap);
        Assert.Null(turn.Display);
        Assert.Equal(start.Advance(), session.Snapshot.Clock);
        Assert.Equal(11, session.NextOperationSequence);
        Assert.True(turn.Request()); turn.Confirm();
        Finish(turn);
        Assert.Equal(start.Advance().Advance(), session.Snapshot.Clock);
    }

    [Fact]
    public void ProgressAdvancesWithinEachDayAndFreezesWhilePaused()
    {
        var turn = new TurnPresentation(Session());
        Enter(turn);
        var firstFrame = turn.Display;
        turn.Update(0.1);
        Assert.Equal(0.01, turn.Progress, 8);
        Assert.Same(firstFrame, turn.Display);
        turn.Update(0.36);
        Assert.Equal(0.05, turn.Progress, 8);
        turn.TogglePause(); turn.Update(10);
        Assert.Equal(0.05, turn.Progress, 8);
        turn.TogglePause(); turn.Update(0.45);
        Assert.Equal(0.1, turn.Progress, 8);
        Assert.NotSame(firstFrame, turn.Display);
        Ready(turn); turn.Update(0.1);
        Assert.Equal(0.11, turn.Progress, 8);
    }

    [Fact]
    public void TitleFlashesRunWhilePlanningAndPublicationWaitsForTheTransition()
    {
        var session = Session(); var turn = new TurnPresentation(session);
        turn.Request(); turn.Confirm();
        Assert.Equal(0, turn.TitleFrame);
        foreach (int frame in new[] { 1, 2, 3 })
        {
            turn.Update(0.125);
            Assert.Equal(frame, turn.TitleFrame);
            Assert.Equal(0, session.NextOperationSequence);
        }
        turn.Update(0.999);
        Assert.Equal(TurnScreen.Entering, turn.Screen);
        Ready(turn); turn.Update(0.002);
        Assert.Equal(TurnScreen.Playing, turn.Screen);
        Assert.Equal(1, session.NextOperationSequence);
    }
}
