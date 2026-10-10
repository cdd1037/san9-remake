using San9.Application;
using San9.Content.Scenarios;
using San9.Core.Simulation;

namespace San9.Client.Godot.Tests;

public sealed class TurnPresentationTests
{
    [Fact]
    public void StrategyTitleCueOnlyRunsForCompletedXunAndEntranceDoesNotRepeatOnRepaintOrCancel()
    {
        int sounds = 0, executionSounds = 0;
        var turn = new TurnPresentation(Session()) { StrategyTitleStarted = () => sounds++, ExecutionTitleStarted = () => executionSounds++ };
        Assert.Equal(1,turn.StrategyEntrance);
        turn.Update(2);
        Assert.Equal(0,sounds);
        Assert.True(turn.Request()); turn.Cancel();
        Assert.Equal(1,turn.StrategyEntrance);
        Assert.Equal(0,sounds);
        Enter(turn);
        Finish(turn);
        Assert.Equal(TurnScreen.Strategy,turn.Screen);
        Assert.Equal(1,executionSounds);
        Assert.Equal(2,turn.StrategyEntrance);
        Assert.Equal(1,sounds);
        turn.Update(2); turn.Update(2);
        Assert.Equal(1,executionSounds);
        Assert.Equal(2,turn.StrategyEntrance);
        Assert.Equal(1,sounds);
        var restored = new TurnPresentation(Session()) { StrategyTitleStarted = () => sounds++, ExecutionTitleStarted = () => executionSounds++ };
        restored.Update(2);
        Assert.Equal(1,restored.StrategyEntrance);
        Assert.Equal(1,sounds);
    }

    [Fact]
    public void CommittedPublicationWaitsForPresentationWithoutRepeatingTheOperation()
    {
        var session=Session(); var turn=new TurnPresentation(session); Enter(turn);
        turn.CanContinue=()=>false;
        turn.Update(2);
        Assert.Equal(TurnScreen.Presenting,turn.Screen);
        long operation=session.NextOperationSequence,publication=turn.Publication;
        string hash=session.Snapshot.StateHash;
        Assert.NotNull(turn.LastResult);
        turn.Update(20);turn.Update(20);
        Assert.Equal(hash,session.Snapshot.StateHash);
        Assert.Equal(operation,session.NextOperationSequence);
        Assert.Equal(publication,turn.Publication);
        turn.CanContinue=()=>true;turn.Update(0);
        Assert.Equal(TurnScreen.Playing,turn.Screen);
        Assert.Equal(operation,session.NextOperationSequence);
    }
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
    public void UnitPositionPhaseSurvivesDaysAndStopsForPauseOrResourceWait()
    {
        var view = Session().View;
        var first = new TurnDayPresentation(view, view, []);
        var second = new TurnDayPresentation(view, view, []);
        var clock = new UnitPositionClock();
        clock.Advance(4, null);
        clock.Advance(1, first); // Prepared, but not yet playing.
        Assert.Equal(4000, clock.Milliseconds, 6);
        first.Advance(.4); clock.Advance(.4, first);
        first.Advance(2, paused: true); clock.Advance(2, first);
        Assert.Equal(4400, clock.Milliseconds, 6);
        first.Advance(.5); // Final progress may be published before the next map frame.
        clock.Advance(.5, second);
        Assert.Equal(4900, clock.Milliseconds, 6);
        second.Advance(.3); clock.Advance(.3, second);
        clock.Advance(0, null);
        clock.Advance(.2, null);
        Assert.Equal(5400, clock.Milliseconds, 6);
    }

    [Fact]
    public void StartedVolleyFinishesDuringPauseAndDoesNotRestartOnResume()
    {
        var turn=new TurnPresentation(Session()) { CreateDay = (before, after, events) => new(before, after, events, duration: 3) }; Enter(turn); turn.Update(.4);
        var day=turn.Day!;
        var attack=new San9.Rendering.MapAttackMotion(1,false,2,false,0,0,100,0,1,
            San9.Rendering.UnitVisualAction.Ranged,San9.Content.Maps.MapProjectileKind.Arrow);
        var art=new San9.Content.Maps.MapProjectile(["arrow"],64,false,0,0);
        Assert.Empty(San9.Rendering.MapProjectiles.Sample(attack,art,day.CombatMilliseconds));
        turn.Update(.5);
        Assert.Single(San9.Rendering.MapProjectiles.Sample(attack,art,day.CombatMilliseconds));
        turn.TogglePause(); turn.Update(2);
        Assert.Equal(.9,day.Elapsed,8);
        Assert.Empty(San9.Rendering.MapProjectiles.Sample(attack,art,day.CombatMilliseconds));
        turn.TogglePause(); turn.Update(.1);
        Assert.Empty(San9.Rendering.MapProjectiles.Sample(attack,art,day.CombatMilliseconds));
    }

    [Fact]
    public void PauseRunsVisualLifetimesWithoutAdvancingOrStartingFutureCombat()
    {
        var turn = new TurnPresentation(Session()) { CreateDay = (before, after, events) => new(before, after, events, duration: 2.8) }; Enter(turn); turn.Update(.36);
        var day = turn.Day!;
        turn.TogglePause(); turn.Update(2);
        Assert.Equal(.36,day.Elapsed,8);
        Assert.Equal(2.36,day.VisualElapsed,8);
        Assert.True(day.CombatMilliseconds < 0);
        Assert.Equal(2180,day.VisualAge(180),6);
        turn.TogglePause(); turn.Update(.54);
        Assert.Equal(0,day.CombatMilliseconds,6);
        Assert.Equal(90,day.VisualAge(810),6);
        turn.TogglePause(); turn.Update(.5);
        Assert.Equal(.9,day.Elapsed,8);
        Assert.Equal(500,day.CombatMilliseconds,6);
        turn.CanPlayDay = () => false;
        turn.Update(10);
        Assert.Equal(500,day.CombatMilliseconds,6);
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
        Assert.True(turn.CanBrowseMap);
        Assert.False(turn.CanAdvance);
        turn.Update(0.4);
        var frame = turn.Display;
        string settled = session.Snapshot.StateHash;
        turn.TogglePause(); turn.Update(100);
        Assert.True(turn.CanBrowseMap);
        Assert.False(turn.CanAdvance);
        Assert.Same(frame, turn.Display);
        Assert.Equal(settled, session.Snapshot.StateHash);
        turn.TogglePause();
        Assert.True(turn.CanBrowseMap);
        Assert.False(turn.CanAdvance);
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
        turn.Update(0.4);
        Assert.Equal(0.05, turn.Progress, 8);
        turn.TogglePause(); turn.Update(10);
        Assert.Equal(0.05, turn.Progress, 8);
        turn.TogglePause(); turn.Update(0.5);
        Assert.Equal(0.1, turn.Progress, 8);
        Assert.NotSame(firstFrame, turn.Display);
        Ready(turn); turn.Update(0.1);
        Assert.Equal(0.11, turn.Progress, 8);
    }

    [Fact]
    public void ResourcePreparationDoesNotConsumePlaybackTimeOrPublishTheDay()
    {
        var session = Session(); var turn = new TurnPresentation(session);
        Enter(turn);
        string hash = session.Snapshot.StateHash;
        turn.CanPlayDay = () => false;
        turn.Update(20);
        Assert.NotNull(turn.Day);
        Assert.Equal(0, turn.Day.Elapsed);
        Assert.Equal(hash, session.Snapshot.StateHash);
        turn.CanPlayDay = () => true;
        turn.Update(.4);
        Assert.Equal(.4, turn.Day.Elapsed);
        turn.TogglePause(); turn.Update(20);
        Assert.Equal(.4, turn.Day.Elapsed);
        Assert.Equal(hash, session.Snapshot.StateHash);
        turn.TogglePause(); turn.Update(.6);
        Assert.Null(turn.Day);
        Assert.NotEqual(hash, session.Snapshot.StateHash);
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

    [Fact]
    public void PreparationRunsOnceAcrossResourceWaitAndRetryCannotPublishAnUnseenDay()
    {
        var session = Session();
        int preparations = 0;
        bool fail = true;
        var turn = new TurnPresentation(session)
        {
            CanPlayDay = () => false,
            CreateDay = (before, after, events) =>
            {
                preparations++;
                if (fail) throw new InvalidOperationException("Controlled presentation failure");
                return new(before, after, events, duration: 3);
            }
        };
        Enter(turn);
        long day = session.Snapshot.Clock.DayNumber;
        turn.Update(.1);
        Assert.Equal(TurnScreen.Failed, turn.Screen);
        Assert.Equal(day, session.Snapshot.Clock.DayNumber);
        Assert.False(turn.CanCancelFailure);
        fail = false; turn.Confirm(); turn.Update(10);
        Assert.Equal(2, preparations);
        var prepared = turn.Day;
        turn.Update(10);
        Assert.Same(prepared, turn.Day);
        Assert.Equal(2, preparations);
        Assert.Equal(0, turn.Day!.Elapsed);
        turn.CanPlayDay = () => true;
        turn.Update(.5);
        Assert.Equal(.5, turn.Day.Elapsed);
        Assert.Equal(3, turn.Day.Duration);
        Assert.Equal(day, session.Snapshot.Clock.DayNumber);
    }
}
