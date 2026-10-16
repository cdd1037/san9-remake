using San9.Tests;
using San9.Application.Persistence;
using San9.Application.AI;
using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CapturedRulerDispositionTests
{
    private static ScenarioInitialState Fixture(bool playerVictim = false)
    {
        var original = G01ScenarioTests.Fixture();
        int victim = playerVictim ? 1 : 4;
        return original with
        {
            NaturalDeathsEnabled = false,
            Officers = original.Officers.Select(p => p.State.Id == victim ? p with { State = p.State with
                { Status = OfficerStatus.Captive, SettlementId = playerVictim ? 2 : 1, Righteousness = 15 } } : p).ToArray()
        };
    }

    [Theory]
    [InlineData(CapturedRulerAction.Release)]
    [InlineData(CapturedRulerAction.Execute)]
    public void PlayerDispositionResumesDayAndReplaysFromStableSave(CapturedRulerAction action)
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var advance = session.Request(new AdvanceOperation(), "player");
        var result = session.Apply(advance);
        restored.Apply(advance);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        var prompt = Assert.IsType<CapturedRulerPrompt>(session.GetCapturedRulerPrompt());
        Assert.Equal(4, prompt.RulerId);
        Assert.Equal(new GameClock(190, 1, 2), session.Snapshot.Clock);
        Assert.Single(result.Events.OfType<CapturedRulerDispositionRequestedEvent>());
        Assert.DoesNotContain(result.Events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.UnitAndBuildingEvents);
        Assert.Empty(result.Events.OfType<XunAdvancedEvent>());
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        Assert.Throws<InvalidOperationException>(() => session.BeginRecording());
        var hash = session.Snapshot;
        long sequence = session.Snapshot.Simulation.NextSequence;
        Assert.Equal(CommandRejection.DecisionPending, session.Execute(new("player"), new WaitCommand(Guid.NewGuid(), 1, sequence)).Submission.Rejection);
        Assert.Equal(CommandRejection.NotActiveController, session.Execute(new("ai:2"),
            new ChooseCapturedRulerDispositionCommand(Guid.NewGuid(), 1, sequence, 4, action)).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidDecision, session.Execute(new("player"),
            new ChooseCapturedRulerDispositionCommand(Guid.NewGuid(), 1, sequence, 5, action)).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidDecision, session.Execute(new("player"),
            new ChooseCapturedRulerDispositionCommand(Guid.NewGuid(), 1, sequence, 4, (CapturedRulerAction)99)).Submission.Rejection);
        StateAssert.Equal(hash, session.Snapshot);
        var command = new ChooseCapturedRulerDispositionCommand(Guid.NewGuid(), 1, sequence, 4, action);
        var request = session.Request(new CommandOperation(command), "player", command.CommandId);
        var completed = session.Apply(request);
        var other = restored.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, completed.Disposition);
        EventAssert.Equal(completed.Events, other.Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Null(session.GetCapturedRulerPrompt());
        Assert.Equal(new GameClock(190, 1, 11), session.Snapshot.Clock);
        Assert.Single(completed.Events.OfType<XunAdvancedEvent>());
        Assert.Equal(10, completed.Events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.UnitAndBuildingEvents));
        Assert.Equal(10, result.Events.Concat(completed.Events).OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.DailyMapUpdate));
        if (action == CapturedRulerAction.Release)
        {
            Assert.Single(completed.Events.OfType<PrisonerReleasedEvent>());
            Assert.Equal(4, session.Snapshot.Forces.Single(f => f.Id == 2).LeaderId);
            Assert.Equal(OfficerStatus.Ruler, session.Snapshot.Officers.Single(p => p.Id == 4).Status);
        }
        else Assert.Single(completed.Events.OfType<PrisonerExecutedEvent>());
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), definition).Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }
    private static ScenarioInitialState AddThirdForce(ScenarioInitialState original, bool captive = false) => original with
    {
        Information = original.Information with { Corps = [.. original.Information.Corps, new(3, 3, 7, new(1000, 2000)) { IsRoot = true }] },
        Forces = [.. original.Forces, original.Forces[1] with { Id = 3, NameKey = "force.3", LeaderId = 7 }],
        Settlements = [.. original.Settlements, original.Settlements[1] with { Id = 3, NameKey = "city.3", OwnerId = 3, X = 3, Y = 3 }],
        Officers = [.. original.Officers,
            new("officer.7", new(7, 3, captive ? 1 : 3, new(60, 70, 80, 90), false) { Status = captive ? OfficerStatus.Captive : OfficerStatus.Ruler }),
            new("officer.8", new(8, 3, 3, new(50, 50, 50, 50), false))]
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllianceExecutionOfPlayerRulerWaitsForHeirAndResumesWithReplay(bool automaticHeir)
    {
        var definition = AddThirdForce(Fixture(playerVictim: true)) with { Alliance = new(1, 2, [2, 3], 10),
            ImperialSealId = 100 };
        definition = definition with { Information = definition.Information with {
            Treasures = [new(100, "seal", 1, 0, 0, null)], TreasureStates = [new(100, 1, TreasureDiscovery.Discovered, null)] } };
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        var result = session.AdvanceXun();
        Assert.Null(session.GetCapturedRulerPrompt());
        Assert.Empty(result.Events.OfType<AllianceVictoryEvent>());
        int initialTrust = session.Snapshot.Forces.Single(f => f.Id == 2).BaseTrust;
        var pending = Assert.IsType<ExecutionSuccessionPrompt>(session.GetExecutionSuccessionPrompt());
        Assert.Equal(1, pending.RulerId);
        Assert.Equal(new GameClock(190, 1, 2), session.Snapshot.Clock);
        Assert.Empty(result.Events.OfType<PrisonerExecutedEvent>());
        Assert.DoesNotContain(session.Snapshot.Simulation.RandomStreams, s => s.Name == "captured-ruler-disposition");
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        var command = new ChooseExecutionSuccessorCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 1, automaticHeir ? null : 3);
        var answer = session.Execute(new("player"), command);
        Assert.True(answer.Submission.Accepted);
        Assert.Single(answer.Events.OfType<PrisonerExecutedEvent>());
        Assert.Single(answer.Events.OfType<XunAdvancedEvent>());
        Assert.Equal(10, answer.Events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.UnitAndBuildingEvents));
        Assert.Equal(4, Assert.Single(session.Snapshot.Treasures).HolderId);
        Assert.Equal(SimulationBoundary.Stable, session.Snapshot.Simulation.Boundary);
        Assert.Null(session.Snapshot.Simulation.Alliance);
        Assert.Equal(AllianceEndReason.SubjugationSucceeded, Assert.Single(answer.Events.OfType<AllianceEndedEvent>()).Reason);
        var victory = Assert.Single(answer.Events.OfType<AllianceVictoryEvent>());
        Assert.Equal((1, 2, AllianceVictoryCause.CapturedRulerExecuted, 15),
            (victory.TargetForceId, victory.VictorForceId, victory.Cause, victory.BaseTrustGained));
        Assert.Equal(initialTrust + 15, session.Snapshot.Forces.Single(f => f.Id == 2).BaseTrust);
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), definition).Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Theory]
    [InlineData(CapturedRulerAction.Execute, true, false)]
    [InlineData(CapturedRulerAction.Execute, true, true)]
    [InlineData(CapturedRulerAction.Release, true, false)]
    [InlineData(CapturedRulerAction.Execute, false, false)]
    public void AllianceDispositionCreditsExecutionButNotReleaseOrAnOutsideCaptor(CapturedRulerAction action, bool allied, bool noHeir)
    {
        var definition = AddThirdForce(Fixture()) with { Alliance = new(2, 3, [1, 3], 10) };
        if (!allied) definition = definition with
        {
            Alliance = new(2, 3, [3, 4], 10),
            Information = definition.Information with { Corps = [.. definition.Information.Corps, new(4, 4, 9, new(1000, 2000)) { IsRoot = true }] },
            Forces = [.. definition.Forces, definition.Forces[2] with { Id = 4, NameKey = "force.4", LeaderId = 9 }],
            Settlements = [.. definition.Settlements, definition.Settlements[2] with { Id = 4, NameKey = "city.4", OwnerId = 4, X = 4, Y = 4 }],
            Officers = [.. definition.Officers, new("officer.9", new(9, 4, 4, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler })]
        };
        if (noHeir) definition = definition with { Officers = definition.Officers.Where(p => p.State.OwnerId != 2 || p.State.Id == 4).ToArray() };
        var session = GameSession.Start(definition, 190);
        int initialTrust = session.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust;
        session.BeginRecording();
        var saved = SaveService.Encode(session);
        session.AdvanceXun();
        var command = new ChooseCapturedRulerDispositionCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 4, action);
        var result = session.Execute(new("player"), command);
        Assert.True(result.Submission.Accepted);
        bool success = allied && action == CapturedRulerAction.Execute;
        Assert.Equal(success ? initialTrust + 15 : initialTrust, session.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
        if (success)
        {
            var victory = Assert.Single(result.Events.OfType<AllianceVictoryEvent>());
            Assert.Equal((2, 1, 15), (victory.TargetForceId, victory.VictorForceId, victory.BaseTrustGained));
            Assert.Equal(AllianceEndReason.SubjugationSucceeded, Assert.Single(result.Events.OfType<AllianceEndedEvent>()).Reason);
            Assert.Equal(noHeir, session.Snapshot.Forces.Single(f => f.Id == 2).IsEliminated);
        }
        else Assert.Empty(result.Events.OfType<AllianceVictoryEvent>());
        var restored = SaveService.Decode(saved, definition);
        restored.AdvanceXun();
        EventAssert.Equal(result.Events, restored.Execute(new("player"), command).Events);
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), definition).Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void MultipleRulersWaitInIdOrderAndFinishDayOnlyAfterLastAnswer()
    {
        var definition = AddThirdForce(Fixture(), captive: true);
        var session = GameSession.Start(definition, 190);
        session.AdvanceXun();
        Assert.Equal(4, session.GetCapturedRulerPrompt()!.RulerId);
        var first = session.Execute(new("player"), new ChooseCapturedRulerDispositionCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 4, CapturedRulerAction.Release));
        Assert.True(first.Submission.Accepted);
        Assert.Equal(7, session.GetCapturedRulerPrompt()!.RulerId);
        Assert.Equal(new GameClock(190, 1, 2), session.Snapshot.Clock);
        Assert.DoesNotContain(first.Events, e => e is StageCompletedEvent or XunAdvancedEvent);
        var pending = new SessionSnapshot(session.Snapshot, [], [], session.NextOperationSequence);
        foreach (var sim in new[] {
            pending.World.Simulation with { PendingCapturedRuler = null },
            pending.World.Simulation with { PendingCapturedRuler = pending.World.Simulation.PendingCapturedRuler! with { RulerId = 4 } },
            pending.World.Simulation with { PendingCapturedRuler = pending.World.Simulation.PendingCapturedRuler! with { XunStarted = new(190, 1, 11) } },
            pending.World.Simulation with { ActiveControl = 0 },
            pending.World.Simulation with { Boundary = SimulationBoundary.Stable } })
            Assert.Throws<ArgumentException>(() => GameSession.Restore(definition, pending with { World = pending.World with { Simulation = sim } }));
        var second = session.Execute(new("player"), new ChooseCapturedRulerDispositionCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 7, CapturedRulerAction.Release));
        Assert.True(second.Submission.Accepted);
        Assert.Null(session.GetCapturedRulerPrompt());
        Assert.Single(second.Events.OfType<XunAdvancedEvent>());
    }

    [Fact]
    public void ExecutionWithoutSuccessorTerminatesWithoutRunningRemainingDay()
    {
        var original = Fixture();
        var definition = original with { Officers = original.Officers.Where(p => p.State.OwnerId != 2 || p.State.Id == 4).ToArray() };
        var session = GameSession.Start(definition, 190);
        session.AdvanceXun();
        Assert.NotNull(session.GetCapturedRulerPrompt());
        var result = session.Execute(new("player"), new ChooseCapturedRulerDispositionCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 4, CapturedRulerAction.Execute));
        Assert.True(result.Submission.Accepted);
        Assert.Equal(SimulationBoundary.Terminal, session.Snapshot.Simulation.Boundary);
        Assert.Null(session.GetCapturedRulerPrompt());
        Assert.Null(session.GetExecutionSuccessionPrompt());
        Assert.Single(result.Events.OfType<ForceEliminatedEvent>());
        Assert.Single(result.Events.OfType<SimulationEndedEvent>());
        Assert.DoesNotContain(result.Events, e => e is StageCompletedEvent or XunAdvancedEvent);
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), definition).Capture());
    }
    private static ScenarioInitialState ComputerFixture(bool execute)
    {
        var original = AddThirdForce(G01ScenarioTests.Fixture());
        return original with
        {
            NaturalDeathsEnabled = false,
            Forces = original.Forces.Select(f => f.Id == 2 ? f with { BaseTrust = 1000 } : f).ToArray(),
            Officers = original.Officers.Select(p => p.State.Id == 7
                ? p with { State = p.State with { Status = OfficerStatus.Captive, SettlementId = 2 } } : p).ToArray(),
            Alliance = execute ? new(3, 1, [1, 2], 10) : null
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComputerResponsesCompleteWithExhaustedTurnBudgetAndRegenerateInReplay(bool execute)
    {
        var definition = ComputerFixture(execute);
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        var saved = SaveService.Encode(session);
        var result = session.AdvanceForPresentation();
        Assert.Equal(SessionDisposition.Accepted, result.Result.Disposition);
        Assert.Equal(new GameClock(190, 1, 11), session.Snapshot.Clock);
        Assert.Null(session.GetPendingResponse());
        Assert.Single(result.Result.Events.OfType<CapturedRulerDispositionRequestedEvent>());
        Assert.Equal(execute ? 1 : 0, result.Result.Events.OfType<ExecutionSuccessionRequestedEvent>().Count());
        Assert.Equal(execute ? 1 : 0, result.Result.Events.OfType<PrisonerExecutedEvent>().Count());
        Assert.Equal(execute ? 0 : 1, result.Result.Events.OfType<PrisonerReleasedEvent>().Count());
        Assert.Equal(execute ? 8 : 7, session.Snapshot.Forces.Single(f => f.Id == 3).LeaderId);
        Assert.DoesNotContain(session.Snapshot.Simulation.RandomStreams, r => r.Name == "captured-ruler-disposition");
        Assert.Equal(10, result.Result.Events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.UnitAndBuildingEvents));
        Assert.Equal(10, result.Frames.Count);
        Assert.All(result.Frames, f => Assert.NotEqual(SimulationBoundary.AwaitingDecision, f.World.Simulation.Boundary));
        Assert.Equal(session.Snapshot.Clock, result.Frames[^1].World.Clock);
        var finalPerson = session.Snapshot.Officers.Single(p => p.Id == 7);
        var displayedPerson = result.Frames[^1].World.Officers.Single(p => p.Id == 7);
        Assert.Equal((finalPerson.Status, finalPerson.OwnerId, finalPerson.SettlementId),
            (displayedPerson.Status, displayedPerson.OwnerId, displayedPerson.SettlementId));
        var restored = SaveService.Decode(saved, definition);
        EventAssert.Equal(result.Result.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var recording = session.EndRecording();
        Assert.Single(recording.Operations);
        var replay = RecordingService.Replay(RecordingService.Encode(recording), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComputerResponseIsIdenticalAcrossXunDailyAndExplicitControlEntries(bool execute)
    {
        var definition = ComputerFixture(execute);
        var whole = GameSession.Start(definition, 190);
        var daily = GameSession.Start(definition, 190);
        var explicitControls = GameSession.Start(definition, 190);
        var events = whole.AdvanceXun().Events;
        List<DomainEvent> dailyEvents = [.. daily.Apply(daily.Request(new PrepareAdvanceOperation())).Events];
        for (int i = 0; i < 10; i++)
        {
            var day = daily.Apply(daily.Request(new AdvanceDayOperation()));
            Assert.Equal(SessionDisposition.Accepted, day.Disposition);
            Assert.Equal(new GameClock(190, 1, i + 2), daily.Snapshot.Clock);
            dailyEvents.AddRange(day.Events);
        }
        List<DomainEvent> controlEvents = [];
        while (explicitControls.Snapshot.Simulation.ActiveControl < explicitControls.Snapshot.Simulation.Controls.Length)
        {
            var seat = explicitControls.Snapshot.Simulation.Controls[explicitControls.Snapshot.Simulation.ActiveControl];
            controlEvents.AddRange(explicitControls.EndControl(new(seat.ControllerId)).Events);
        }
        controlEvents.AddRange(explicitControls.AdvanceXun().Events);
        StateAssert.Equal(whole.Snapshot, daily.Snapshot);
        StateAssert.Equal(whole.Snapshot, explicitControls.Snapshot);
        // A command completes after its continuation, so its notification moves with the requested day/xun boundary.
        EventAssert.Equal(events.Where(e => e is not CommandResolvedEvent), dailyEvents.Where(e => e is not CommandResolvedEvent));
        EventAssert.Equal(events.OfType<CommandResolvedEvent>(), dailyEvents.OfType<CommandResolvedEvent>());
        EventAssert.Equal(events, controlEvents);
    }

    [Theory]
    [InlineData(19, 99, 0, 20)]
    [InlineData(20, 100, 0, 18)]
    [InlineData(1000, 100, 100, 0)]
    public void ResponseEvaluationUsesSeparateIntegerDivisions(int trust, int ability, int relation, int expected)
    {
        var prompt = new CapturedRulerPrompt(7, 2, 3) { CaptorBaseTrust = trust,
            RulerAbilities = new(1, 1, ability, 1), RelationValue = relation };
        Assert.Equal(expected, AutomaticResponseRunner.ExecutionChance(prompt));
    }

    [Fact]
    public void DispositionObservationUsesInjuredCurrentAbility()
    {
        int Chance(int injury)
        {
            var original = ComputerFixture(false);
            var definition = original with
            {
                Forces = original.Forces.Select(f => f.Id == 2 ? f with { BaseTrust = 0 } : f).ToArray(),
                Officers = original.Officers.Select(p => p.State.Id == 7
                    ? p with { State = p.State with { Injury = injury } } : p).ToArray()
            };
            var engine = GameSession.EngineFactory(definition)(WorldState.Restore(GameSession.Start(definition, 190).Snapshot));
            foreach (var seat in engine.Snapshot.Simulation.Controls) engine.EndControl(new(seat.ControllerId));
            engine.AdvanceXun();
            return AutomaticResponseRunner.ExecutionChance(engine.GetCapturedRulerPrompt()!);
        }
        Assert.True(Chance(5) > Chance(0));
    }

    [Fact]
    public void CoreAuthorizesComputerResponseOutsideActiveSeatAndProvidesLegalDefault()
    {
        var definition = ComputerFixture(true);
        var engine = GameSession.EngineFactory(definition)(WorldState.Restore(GameSession.Start(definition, 190).Snapshot));
        foreach (var seat in engine.Snapshot.Simulation.Controls) engine.EndControl(new(seat.ControllerId));
        engine.AdvanceXun();
        var response = Assert.IsType<ResponseRequest>(engine.GetPendingResponse());
        Assert.Equal("ai:2", response.Owner.ControllerId);
        var command = AutomaticResponseRunner.Choose(engine, response, evaluationBudget: 0);
        Assert.Equal(CapturedRulerAction.Release, Assert.IsType<ChooseCapturedRulerDispositionCommand>(command).Action);
        var before = engine.Snapshot;
        Assert.Equal(CommandRejection.NotActiveController, engine.Execute(new("player"), command).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidDecision, engine.Execute(new("ai:2"),
            ((ChooseCapturedRulerDispositionCommand)command) with { RulerId = 8 }).Submission.Rejection);
        var failure = Assert.Throws<AutomaticResponseException>(() => AutomaticResponseRunner.Choose(engine,
            response with { Kind = "unimplemented" }));
        Assert.Equal("response-unsupported", failure.Failure.Code);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.True(engine.Execute(new("ai:2"), command).Submission.Accepted);
        Assert.Null(engine.GetPendingResponse());
    }

    [Fact]
    public void DelegatedRootDoesNotOwnForcePoliticalResponse()
    {
        var original = Fixture();
        var definition = original with { Information = original.Information with { Corps = original.Information.Corps
            .Select(c => c.OwnerId == 1 ? c with { ControlMode = CorpsControlMode.Automatic } : c).ToArray() } };
        var session = GameSession.Start(definition, 190);
        session.AdvanceXun();
        var response = Assert.IsType<ResponseRequest>(session.GetPendingResponse());
        Assert.Equal(ControllerKind.Player, response.Owner.Kind);
        Assert.Equal("player", response.Owner.ControllerId);
        var command = new ChooseCapturedRulerDispositionCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 4, CapturedRulerAction.Release);
        Assert.Equal(CommandRejection.NotActiveController, session.Execute(new("corps:1"), command).Submission.Rejection);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        Assert.Null(session.GetPendingResponse());
    }

    [Fact]
    public void ComputerExecutionStopsImmediatelyWhenPlayerHasNoHeir()
    {
        var original = AddThirdForce(Fixture(playerVictim: true));
        var definition = original with { Alliance = new(1, 2, [2, 3], 10),
            Officers = original.Officers.Where(p => p.State.OwnerId != 1 || p.State.Id == 1).ToArray() };
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        var result = session.AdvanceXun();
        Assert.Equal(SimulationBoundary.Terminal, session.Snapshot.Simulation.Boundary);
        Assert.Equal(new GameClock(190, 1, 2), session.Snapshot.Clock);
        Assert.Single(result.Events.OfType<CapturedRulerDispositionRequestedEvent>());
        Assert.Single(result.Events.OfType<PrisonerExecutedEvent>());
        Assert.Single(result.Events.OfType<SimulationEndedEvent>());
        Assert.Empty(result.Events.OfType<ExecutionSuccessionRequestedEvent>());
        Assert.DoesNotContain(result.Events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.UnitAndBuildingEvents);
        Assert.Empty(result.Events.OfType<XunAdvancedEvent>());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void RepeatedResponseWithoutProgressFailsAndRollsBackOuterOperation()
    {
        var definition = ComputerFixture(false);
        var rules = new RepeatingDispositionRules { Personnel = new(definition.Settlements.Select(s => new PersonPlace(s.Id, s.Position))) };
        var session = GameSession.Start(definition, 190, w => new(w, rules));
        session.BeginRecording();
        var before = session.Capture();
        var result = session.Apply(session.Request(new AdvanceOperation()));
        Assert.Equal(SessionRejection.AutomaticResponseFailed, result.Rejection);
        Assert.Equal("response-no-progress", result.AutomaticResponseFailure!.Code);
        Assert.Equal("ai:2", result.AutomaticResponseFailure.ControllerId);
        StateAssert.Equal(before, session.Capture());
        Assert.Empty(result.Events);
        Assert.Empty(session.CaptureRecording().Operations);
        Assert.Empty(session.Records);
    }

    private sealed class RepeatingDispositionRules : SimulationRules
    {
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
        {
            if (stage != ResolutionStage.UnitAndBuildingEvents) return;
            world.Replace(world.Officers[7] with { Status = OfficerStatus.Captive, SettlementId = 2 });
            world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Where(t => t.OfficerId != 7).ToImmutableArray(),
                Boundary = SimulationBoundary.AwaitingDecision, PendingCapturedRuler = new(new(190, 1, 1), 7, 2) });
        }
    }

}
