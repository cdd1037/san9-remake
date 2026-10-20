using San9.Tests;
using San9.Application;
using San9.Content.Scenarios;
using San9.Core.Simulation;
using San9.Client.Godot.Settings;

namespace San9.Client.Godot.Tests;

public sealed class TurnPresentationTests
{
    [Fact]
    public void MultiplePlayersReturnToStrategyBeforeAdvancingTheDate()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "turn", 190, 1, 1, 1,
            [new(1, "first", 1, 100), new(2, "second", 2, 100)],
            [new(15, "first-city", 1, 0, 0, 0, 100, 100, 0, []), new(16, "second-city", 2, 1, 0, 0, 100, 100, 0, [])], [], [])
        { Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, null, new(100, 100)) { IsRoot = true },
            new San9.Core.Domain.CorpsState(2, 2, null, new(100, 100)) { IsRoot = true }], []) };
        var session = GameSession.Start(definition, 190);
        Assert.Equal(SessionDisposition.Accepted, session.TakeOverForce(2).Disposition);
        var turn = new TurnPresentation(session);
        var clock = session.Snapshot.Clock;
        Assert.False(turn.ContinueAutomaticControls());
        Assert.True(turn.Request());
        turn.Confirm(); Ready(turn); turn.Update(2);
        Assert.Equal(TurnScreen.Returning, turn.Screen);
        Assert.Equal(clock, session.Snapshot.Clock);
        Assert.Equal(2, session.View.PlayerForceId);
        turn.Update(2);
        Assert.True(turn.CanAdvance);
        Enter(turn); Finish(turn);
        Assert.Equal(TurnScreen.Strategy, turn.Screen);
        Assert.Equal(clock.DayNumber + 10, session.Snapshot.Clock.DayNumber);
        Assert.Equal(1, session.View.PlayerForceId);
    }

    [Fact]
    public void DelegationContinuesWithoutAnotherConfirmationUntilAnObserverStop()
    {
        var session = Session();
        Assert.Equal(SessionDisposition.Accepted, session.HandOverForce(5).Disposition);
        var turn = new TurnPresentation(session);
        turn.Update(10);
        Assert.Equal(TurnScreen.Strategy, turn.Screen);
        turn.StopAtStrategyBoundary();
        long started = session.Snapshot.Clock.DayNumber;
        Assert.True(turn.ContinueAutomaticControls());
        Assert.False(turn.ContinueAutomaticControls());
        Ready(turn); turn.Update(2); Ready(turn);
        Assert.Equal(TurnScreen.Playing, turn.Screen);
        for (int i = 0; i < 40 && session.Snapshot.Clock.DayNumber < started + 11; i++)
        { Ready(turn); turn.Update(2); }
        Assert.True(session.Snapshot.Clock.DayNumber >= started + 11);
        Assert.Null(session.View.PlayerForceId);
        turn.StopAtStrategyBoundary();
        Finish(turn);
        Assert.Equal(TurnScreen.Strategy, turn.Screen);
        Assert.Equal(started + 20, session.Snapshot.Clock.DayNumber);
        Assert.True(session.Snapshot.Simulation.CanPersist);
        Assert.True(turn.CanAdvance);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 10)]
    [InlineData(3, 10)]
    public void NewlyPublishedResultsAreConsumedAndDismissedBeforeTheNextDayOrStrategyTitle(int month, int days)
    {
        var session = Session(month, days == 10 ? 21 : 1);
        int titles = 0, seasons = 0, xunStarts = 0;
        var turn = new TurnPresentation(session)
        {
            StrategyTitleStarted = () => titles++,
            StrategyReady = () => { Assert.True(session.Snapshot.Simulation.CanPersist); xunStarts++; },
            PresentSeasonChange = _ => { seasons++; return true; }
        };
        Assert.False(turn.UsesExecutionMusic);
        Assert.True(turn.Request());
        Assert.False(turn.UsesExecutionMusic);
        turn.Cancel();
        Assert.False(turn.UsesExecutionMusic);
        Enter(turn);
        Assert.True(turn.UsesExecutionMusic);
        for (int i = 1; i < days; i++) { Ready(turn); turn.Update(2); }
        Assert.True(turn.IsExecutionPresentation);
        long consumed = turn.Publication;
        bool dialogueOpen = false;
        turn.CanContinue = publication => consumed == publication && !dialogueOpen;

        // Match the HUD frame: publish first, then discover and open the result dialogue.
        Ready(turn); turn.Update(2);
        Assert.Equal(TurnScreen.Presenting, turn.Screen);
        Assert.True(turn.IsExecutionPresentation);
        Assert.True(turn.UsesExecutionMusic);
        Assert.Equal(consumed + 1, turn.Publication);
        Assert.False(turn.IsComputing);
        long operation = session.NextOperationSequence;
        turn.Update(100);
        Assert.Equal(TurnScreen.Presenting, turn.Screen);
        Assert.Equal(0, titles);
        Assert.Equal(0, seasons);
        Assert.Equal(1, turn.StrategyEntrance);
        Assert.Equal(0, xunStarts);

        consumed = turn.Publication;
        dialogueOpen = true;
        turn.Update(100);
        Assert.Equal(TurnScreen.Presenting, turn.Screen);
        Assert.Equal(operation, session.NextOperationSequence);
        Assert.Equal(consumed, turn.Publication);
        Assert.Equal(0, titles);
        Assert.Equal(0, seasons);

        dialogueOpen = false;
        turn.Update(0);
        Assert.Equal(days == 10 ? TurnScreen.Returning : TurnScreen.Playing, turn.Screen);
        Assert.Equal(days != 10, turn.IsExecutionPresentation);
        Assert.Equal(days != 10, turn.UsesExecutionMusic);
        Assert.Equal(days == 10 ? 1 : 0, titles);
        Assert.Equal(month == 3 ? 1 : 0, seasons);
        Assert.Equal(1, turn.StrategyEntrance);
        if (days == 10)
        {
            turn.Update(TurnPresentation.TransitionDuration);
            Assert.Equal(TurnScreen.Strategy, turn.Screen);
            Assert.Equal(2, turn.StrategyEntrance);
            Assert.Equal(1, xunStarts);
            turn.Update(100);
            Assert.Equal(1, titles);
            Assert.Equal(1, xunStarts);
            var rebound = new TurnPresentation(session) { StrategyReady = () => xunStarts++ };
            rebound.Update(100);
            Assert.Equal(1, xunStarts);
        }
    }

    [Fact]
    public void QuarterChangeWaitsForResultDialogueAndDoesNotReplayWhenRebindingSession()
    {
        var session = Session(3, 21);
        bool completed = false;
        int titleCount = 0, seasonMonth = 0;
        var turn = new TurnPresentation(session)
        {
            PresentSeasonChange = month => { seasonMonth = month; return completed; },
            StrategyTitleStarted = () => titleCount++
        };
        Enter(turn);
        for (int i = 0; i < 20 && turn.SeasonChangeOrigin is null; i++) { Ready(turn); turn.Update(10); }
        Assert.Equal(0, seasonMonth);
        turn.CanContinue = _ => false;
        turn.Update(100);
        Assert.Equal(0, seasonMonth);
        turn.CanContinue = _ => true;
        turn.Update(0);
        Assert.Equal(4, seasonMonth);
        Assert.Equal(new GameClock(190, 3, 30), turn.SeasonChangeOrigin);
        Assert.Equal(TurnScreen.Presenting, turn.Screen);
        Assert.Equal(new GameClock(190, 4, 1), session.Snapshot.Clock);
        Assert.Equal(0, titleCount);
        var snapshot = session.Snapshot;
        long publication = turn.Publication, sequence = session.NextOperationSequence;
        turn.Update(100);
        StateAssert.Equal(snapshot, session.Snapshot);
        Assert.Equal(publication, turn.Publication);
        Assert.Equal(sequence, session.NextOperationSequence);
        completed = true; turn.Update(0);
        Assert.Null(turn.SeasonChangeOrigin);
        Assert.Equal(TurnScreen.Returning, turn.Screen);
        Assert.Equal(1, titleCount);
        var restored = new TurnPresentation(session) { PresentSeasonChange = _ => throw new Exception("Must not replay") };
        restored.Update(100);
        Assert.Equal(TurnScreen.Strategy, restored.Screen);
    }

    [Fact]
    public void ChangingSpeedKeepsDayContinuousAndRespectsPauseAndResourceWait()
    {
        var session = Session();
        bool ready = true;
        var turn = new TurnPresentation(session)
        {
            CreateDay = (before, after, events) => new(before, after, events, duration: 10),
            CanPlayDay = () => ready
        };
        Enter(turn); Ready(turn);
        var initial = session.Snapshot;
        turn.Update(.1);
        var day = turn.Day!;
        Assert.Equal(.1, day.Elapsed, 6);
        turn.PlaybackSpeed = PlaybackSpeed.Quadruple;
        Assert.Equal(.1, day.Elapsed, 6);
        turn.Update(.1);
        Assert.Equal(.5, day.Elapsed, 6);
        turn.TogglePause(); turn.Update(100);
        turn.PlaybackSpeed = PlaybackSpeed.Double;
        turn.Update(.1);
        Assert.Equal(.5, day.Elapsed, 6);
        turn.TogglePause(); ready = false; turn.Update(100);
        Assert.Equal(.5, day.Elapsed, 6);
        ready = true; turn.Update(.1);
        Assert.Same(day, turn.Day);
        Assert.Equal(.7, day.Elapsed, 6);
        StateAssert.Equal(initial, session.Snapshot);
        Assert.Throws<ArgumentOutOfRangeException>(() => turn.PlaybackSpeed = (PlaybackSpeed)3);
    }

    [Theory]
    [InlineData(PlaybackSpeed.Double)]
    [InlineData(PlaybackSpeed.Quadruple)]
    public void FasterPlaybackPublishesTheSameDailyStatesAndEventsInLessPresentationTime(PlaybackSpeed speed)
    {
        var normal = Run(PlaybackSpeed.Normal);
        var faster = Run(speed);
        Assert.Equal(normal.States.Count, faster.States.Count);
        for (int i = 0; i < normal.States.Count; i++) StateAssert.Equal(normal.States[i], faster.States[i]);
        EventAssert.Equal(normal.Events, faster.Events);
        Assert.True(faster.Updates < normal.Updates);

        static (List<WorldSnapshot> States, List<DomainEvent> Events, int Updates) Run(PlaybackSpeed playback)
        {
            var session = Session();
            var turn = new TurnPresentation(session) { PlaybackSpeed = playback };
            turn.Request(); turn.Confirm();
            List<WorldSnapshot> states = [];
            List<DomainEvent> events = [];
            int updates = 0;
            long publication = 0;
            while (turn.Screen != TurnScreen.Strategy && updates < 1000)
            {
                Ready(turn); turn.Update(.05); updates++;
                if (turn.Publication == publication) continue;
                publication = turn.Publication;
                states.Add(turn.LastResult!.Snapshot);
                events.AddRange(turn.LastResult.Events);
            }
            Assert.Equal(TurnScreen.Strategy, turn.Screen);
            Assert.Equal(11, session.Snapshot.Clock.Day);
            return (states, events, updates);
        }
    }

    [Fact]
    public void SkippingZoomKeepsResultResponsesAndCannotPublishOrChangeTheWorld()
    {
        var session = Session();
        string[] roles = ["activation-own", "action", "hit-reaction", "damage"];
        var texts = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, [])
        { Messages = roles.ToDictionary(r => r, r => new San9.Content.Text.MessageDefinition([new San9.Content.Text.MessageLiteral(r)])) };
        var messages = roles.ToDictionary(r => r, r => new San9.Content.Interface.DecisionMessageDefinition(r, []));
        var sprite = new San9.Content.Interface.BattleSprite("sprite", 8, 100);
        var body = new San9.Content.Interface.BattleZoomBody(sprite, sprite, sprite, null)
        { BlockPoints = [new(500, 200), new(600, 250), new(700, 300)] };
        var zoom = new San9.Content.Interface.BattleZoomDefinition(body, body, sprite, sprite, "left", "right", [])
        { DamageBands = [new(null, 2)] };
        var content = new San9.Content.Interface.CommandPresentation([], null)
        { Battle = new(messages, new() { [99] = new(messages, null, zoom) }, "hit", "block") };
        var source = new San9.Core.Simulation.BattleObjectContext(10, false, 5, null, new(12, 12), 15, false);
        var target = source with { Id = 11, Position = new(24, 24) };
        var occurrence = new San9.Core.Simulation.TacticOccurrence(source, target, null, null,
            San9.Core.Domain.UnitCondition.Normal, null, null, null, false);
        San9.Core.Simulation.DomainEvent[] events = [new San9.Core.Simulation.TacticActivatedEvent(session.Snapshot.Clock,10,false,11,false,99,[new(7, 99)],false,10)
            { Occurrence=occurrence }];
        var plan = San9.Application.Presentation.BattlePresentationPlan.Create(session.View, session.View, events, session.Messages(texts), texts, content);
        var day = new TurnDayPresentation(session.View, session.View, events, battle: plan);
        var cue = Assert.Single(plan.Visuals); var hash = session.Snapshot;
        day.Advance(cue.Start + .1);
        double gate = day.Elapsed;
        Assert.NotEmpty(day.PendingDialogues);
        Assert.Equal(cue.EntranceStart, gate);
        day.Advance(100);
        Assert.Equal(gate, day.Elapsed);
        foreach (var dialogue in day.PendingDialogues) day.CompleteDialogue(dialogue.Id);
        day.Advance(.001);
        Assert.InRange(day.Elapsed, cue.EntranceStart!.Value + .0009, cue.EntranceStart.Value + .0011);
        Assert.Empty(day.PendingDialogues);
        float opacity = San9.Rendering.BattleSceneTransition.Opacity(day.Elapsed - cue.EntranceStart.Value);
        day.Advance(10, paused: true);
        Assert.Equal(opacity, San9.Rendering.BattleSceneTransition.Opacity(day.Elapsed - cue.EntranceStart.Value));
        // Skip from the darkening phase must still stop at the next response dialogue.
        day.SkipVisual();
        Assert.True(day.Elapsed < cue.End);
        Assert.Equal("hit-reaction", Assert.Single(day.PendingDialogues).Message.Message.Key);
        gate = day.Elapsed;
        day.SkipVisual(); Assert.Equal(gate, day.Elapsed);
        foreach (var dialogue in day.PendingDialogues) day.CompleteDialogue(dialogue.Id);
        day.SkipVisual();
        Assert.Equal(cue.End, day.Elapsed, 6);
        Assert.Equal(day.Duration, day.Elapsed);
        Assert.Contains(plan.Records, c => c.Record.Message.Key == "damage" && c.Time == day.Elapsed);
        day.SkipVisual(); Assert.Equal(cue.End, day.Elapsed, 6);
        Assert.Same(plan, day.Battle); StateAssert.Equal(hash, session.Snapshot);
    }
    [Fact]
    public void MapSpeechAtTheDialogueBoundaryWaitsForCompletionWithoutAnArtificialDelay()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "turn", 190, 1, 1, 5,
            [new(5, "player", 5, 100)], [new(15, "city", 5, 0, 0, 0, 100, 100, 0, [])], [], [])
        { Officers = [new("actor", new San9.Core.Domain.OfficerState(7, 5, 15, new(50, 50, 50, 50), false))],
          Information = new([], [], [], [new San9.Core.Domain.CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], []) };
        var session = GameSession.Start(definition, 1);
        string[] roles = ["activation-own", "announcement", "action"];
        var texts = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, new() { ["actor"] = "actor" })
        { Messages = roles.ToDictionary(r => r, r => new San9.Content.Text.MessageDefinition([new San9.Content.Text.MessageLiteral(r)])) };
        var messages = roles.ToDictionary(r => r, r => new San9.Content.Interface.DecisionMessageDefinition(r, [], "actor"));
        var content = new San9.Content.Interface.CommandPresentation([], null)
        { Battle = new(messages, new() { [99] = new(messages, null, null) }, "hit", "block") };
        var source = new BattleObjectContext(10, false, 5, 7, new(12, 12), 15, false);
        var target = source with { Id = 11 };
        var occurrence = new TacticOccurrence(source, target, null, null, San9.Core.Domain.UnitCondition.Normal, null, null, null, false);
        DomainEvent[] events = [new TacticActivatedEvent(session.Snapshot.Clock, 10, false, 11, false, 99, [new(7, 99)], false, 10)
        { Occurrence = occurrence }];
        var plan = San9.Application.Presentation.BattlePresentationPlan.Create(session.View, session.View, events,
            session.Messages(texts), texts, content);
        var day = new TurnDayPresentation(session.View, session.View, events, battle: plan);
        day.Advance(100);
        var dialogue = Assert.Single(day.PendingDialogues);
        Assert.Empty(day.ReadyMapSpeeches);
        Assert.Equal(1, day.Progress);
        Assert.False(day.IsComplete);
        double boundary = day.Elapsed;
        day.CompleteDialogue(dialogue.Id);
        Assert.True(day.IsComplete);
        Assert.Equal(boundary, day.Elapsed);
        Assert.Equal("action", Assert.Single(day.ReadyMapSpeeches).Message.Message.Key);
    }

    [Fact]
    public void PreparedPresentationSurvivesPauseAndResourceRetryAndReportsBeforeWorldPublication()
    {
        var session = Session(); bool ready = false, fail = false; int retries = 0, calls = 0;
        TurnDayPresentation? retained = null;
        var turn = new TurnPresentation(session)
        {
            CreateDay = (before, after, events) => new(before, after, events, duration: 3),
            CanPlayDay = () => fail ? throw new IOException("resource") : ready,
            RetryPresentation = () => { retries++; fail = false; },
            PresentDay = day =>
            {
                calls++;
                Assert.Equal(day.Before.World.Revision, session.Snapshot.Revision);
                retained ??= day; Assert.Same(retained, day);
            }
        };
        Enter(turn); turn.Update(8);
        var prepared = turn.Day!; var hash = session.Snapshot;
        Assert.Equal(0, prepared.Elapsed); Assert.Equal(0, calls);
        ready = true; turn.Update(.5);
        turn.TogglePause(); turn.Update(20);
        Assert.Same(prepared, turn.Day); Assert.Equal(.5, prepared.Elapsed); Assert.Equal(1, calls);
        fail = true; turn.Update(.2);
        Assert.Equal(TurnScreen.Failed, turn.Screen);
        StateAssert.Equal(hash, session.Snapshot);
        turn.Confirm(); Assert.Equal(1, retries);
        turn.Update(3); Assert.Same(prepared, turn.Day); Assert.Equal(.5, prepared.Elapsed);
        turn.TogglePause();
        turn.Update(3);
        Assert.Equal(2, calls); Assert.Null(turn.Day);
        StateAssert.NotEqual(hash, session.Snapshot);
    }
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
        turn.CanContinue=_=>false;
        turn.Update(2);
        Assert.Equal(TurnScreen.Presenting,turn.Screen);
        long operation=session.NextOperationSequence,publication=turn.Publication;
        var hash=session.Snapshot;
        Assert.NotNull(turn.LastResult);
        turn.Update(20);turn.Update(20);
        StateAssert.Equal(hash, session.Snapshot);
        Assert.Equal(operation,session.NextOperationSequence);
        Assert.Equal(publication,turn.Publication);
        turn.CanContinue=_=>true;turn.Update(0);
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
        var initial = session.Snapshot;
        Assert.True(turn.CanBrowseMap);
        turn.Confirm(); turn.Update(10);
        Assert.True(turn.Request()); Assert.False(turn.Request());
        Assert.False(turn.CanBrowseMap);
        turn.Cancel(); turn.Update(10);
        Assert.True(turn.CanBrowseMap);
        StateAssert.Equal(initial, session.Snapshot);
        Assert.True(turn.Request()); turn.Confirm(); turn.Confirm();
        turn.Update(0.2);
        StateAssert.Equal(initial, session.Snapshot);
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
        var settled = session.Snapshot;
        turn.TogglePause(); turn.Update(100);
        Assert.True(turn.CanBrowseMap);
        Assert.False(turn.CanAdvance);
        Assert.Same(frame, turn.Display);
        StateAssert.Equal(settled, session.Snapshot);
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
        var hash = session.Snapshot;
        turn.CanPlayDay = () => false;
        turn.Update(20);
        Assert.NotNull(turn.Day);
        Assert.Equal(0, turn.Day.Elapsed);
        StateAssert.Equal(hash, session.Snapshot);
        turn.CanPlayDay = () => true;
        turn.Update(.4);
        Assert.Equal(.4, turn.Day.Elapsed);
        turn.TogglePause(); turn.Update(20);
        Assert.Equal(.4, turn.Day.Elapsed);
        StateAssert.Equal(hash, session.Snapshot);
        turn.TogglePause(); turn.Update(.6);
        Assert.Null(turn.Day);
        StateAssert.NotEqual(hash, session.Snapshot);
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
