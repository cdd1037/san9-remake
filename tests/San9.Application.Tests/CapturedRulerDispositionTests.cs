using San9.Application.Persistence;
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
        string hash = session.Snapshot.StateHash;
        long sequence = session.Snapshot.Simulation.NextSequence;
        Assert.Equal(CommandRejection.DecisionPending, session.Execute(new("player"), new WaitCommand(Guid.NewGuid(), 1, sequence)).Submission.Rejection);
        Assert.Equal(CommandRejection.NotActiveController, session.Execute(new("ai:2"),
            new ChooseCapturedRulerDispositionCommand(Guid.NewGuid(), 1, sequence, 4, action)).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidDecision, session.Execute(new("player"),
            new ChooseCapturedRulerDispositionCommand(Guid.NewGuid(), 1, sequence, 5, action)).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidDecision, session.Execute(new("player"),
            new ChooseCapturedRulerDispositionCommand(Guid.NewGuid(), 1, sequence, 4, (CapturedRulerAction)99)).Submission.Rejection);
        Assert.Equal(hash, session.Snapshot.StateHash);
        var command = new ChooseCapturedRulerDispositionCommand(Guid.NewGuid(), 1, sequence, 4, action);
        var request = session.Request(new CommandOperation(command), "player", command.CommandId);
        var completed = session.Apply(request);
        var other = restored.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, completed.Disposition);
        Assert.Equal(CanonicalJson.Text(completed.Events), CanonicalJson.Text(other.Events));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
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
        Assert.Equal(session.Capture().StateHash, SaveService.Decode(SaveService.Encode(session), definition).Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
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
        Assert.Equal(session.Capture().StateHash, SaveService.Decode(SaveService.Encode(session), definition).Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
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
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.Execute(new("player"), command).Events));
        Assert.Equal(session.Capture().StateHash, SaveService.Decode(SaveService.Encode(session), definition).Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
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
        Assert.Equal(session.Capture().StateHash, SaveService.Decode(SaveService.Encode(session), definition).Capture().StateHash);
    }
}
