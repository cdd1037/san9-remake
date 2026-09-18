using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PrisonerExecutionTests
{
    private static ScenarioInitialState Fixture(bool playerRuler = false)
    {
        var original = G01ScenarioTests.Fixture();
        int victim = playerRuler ? 1 : 7;
        var prisoner = new OfficerState(victim, playerRuler ? 1 : 2, playerRuler ? 2 : 1, new(50, 50, 50, 50), false)
            { Status = OfficerStatus.Captive, BloodlineId = 9000, BirthYear = 140 };
        return original with
        {
            NaturalDeathsEnabled = false, ImperialSealId = 100,
            Officers = original.Officers.Where(p => p.State.Id != victim).Append(new("prisoner", prisoner)).ToArray(),
            Information = original.Information with
            {
                Relations = [new(1, 2, 80)],
                Treasures = [new(100, "seal", 1, 0, 0, null)],
                TreasureStates = [new(100, victim, TreasureDiscovery.Discovered, null)]
            }
        };
    }

    [Fact]
    public void ExecutionAppliesKinshipPenaltiesCancelsRecruitmentAndTransfersSealExactlyOnce()
    {
        var original = Fixture();
        var definition = original with { Officers = original.Officers.Select(p => p.State.Id switch
        {
            4 => p with { State = (p.State with { BloodlineId = 9000 }).WithLoyalty(1, 80) },
            5 => p with { State = (p.State with { BloodlineId = 9000, ParentId = 7 }).WithLoyalty(1, 80) },
            6 => p with { State = (p.State with { ParentId = 7 }).WithLoyalty(1, 3) },
            7 => p with { State = p.State.WithLoyalty(1, 80) },
            _ => p
        }).ToArray() };
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new RecruitOfficerCommand(Guid.NewGuid(), 1, 0, 2, 7)).Submission.Accepted);
        var command = new ExecutePrisonerCommand(Guid.NewGuid(), 1, 1, 7);
        var result = session.Execute(new("player"), command);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(OfficerDeathCause.Execution, Assert.Single(result.Events.OfType<OfficerDiedEvent>()).Cause);
        Assert.Single(result.Events.OfType<PrisonerExecutedEvent>());
        Assert.Equal(75, session.Snapshot.Officers.Single(p => p.Id == 4).LoyaltyTo(1));
        Assert.Equal(70, session.Snapshot.Officers.Single(p => p.Id == 5).LoyaltyTo(1));
        Assert.Equal(0, session.Snapshot.Officers.Single(p => p.Id == 6).LoyaltyTo(1));
        Assert.InRange(Assert.Single(session.Snapshot.Relations).Value, 73, 75);
        Assert.Equal(1, Assert.Single(session.Snapshot.Treasures).HolderId);
        Assert.Contains(result.Events.OfType<ForceTrustChangedEvent>(), e => e.ForceId == 1);
        Assert.Single(result.Events.OfType<TaskCancelledEvent>());
        Assert.All(session.Snapshot.Simulation.Tasks, t => Assert.Equal("personnel-return", t.Kind));
        Assert.DoesNotContain(session.View.Officers, p => p.Id == 7);
        string before = session.Snapshot.StateHash;
        Assert.False(session.Execute(new("player"), command with { CommandId = Guid.NewGuid(), Sequence = 2 }).Submission.Accepted);
        Assert.Equal(before, session.Snapshot.StateHash);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExecutedComputerRulerAutomaticallySucceedsOrEliminatesWithoutCandidates(bool last)
    {
        var original = Fixture();
        var definition = original with
        {
            Forces = original.Forces.Select(f => f.Id == 2 ? f with { LeaderId = 7 } : f).ToArray(),
            Officers = original.Officers.Where(p => !last || p.State.OwnerId != 2 || p.State.Id == 7)
                .Select(p => p.State.Id == 5 ? p with { State = p.State with { ParentId = 7, BirthYear = 180 } } : p).ToArray()
        };
        var session = GameSession.Start(definition, 190);
        var result = session.Execute(new("player"), new ExecutePrisonerCommand(Guid.NewGuid(), 1, 0, 7));
        Assert.True(result.Submission.Accepted);
        Assert.Null(session.GetExecutionSuccessionPrompt());
        var force = session.Snapshot.Forces.Single(f => f.Id == 2);
        Assert.Equal(last, force.IsEliminated);
        if (last)
        {
            Assert.Null(force.LeaderId);
            Assert.Single(result.Events.OfType<ForceEliminatedEvent>());
            Assert.Equal(SimulationBoundary.Terminal, session.Snapshot.Simulation.Boundary);
            Assert.DoesNotContain(session.Snapshot.Officers, p => p.OwnerId == 2);
            Assert.DoesNotContain(session.Snapshot.Settlements, p => p.OwnerId == 2);
        }
        else Assert.Equal(5, force.LeaderId);
        Assert.Equal(session.Capture().StateHash, SaveService.Decode(SaveService.Encode(session), definition).Capture().StateHash);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(99999)]
    public void NonCaptiveOrMissingTargetRejectsWithoutEffects(int target)
    {
        var session = GameSession.Start(Fixture(), 190);
        string before = session.Snapshot.StateHash;
        var result = session.Execute(new("player"), new ExecutePrisonerCommand(Guid.NewGuid(), 1, 0, target));
        Assert.False(result.Submission.Accepted);
        Assert.Empty(result.Events);
        Assert.Equal(before, session.Snapshot.StateHash);
    }

    [Fact]
    public void UnaffiliatedPrisonerHasNoDiplomacyRandomOrRelationChange()
    {
        var original = Fixture();
        var definition = original with { Officers = original.Officers.Select(p => p.State.Id == 7
            ? p with { State = p.State with { OwnerId = null } } : p).ToArray() };
        var session = GameSession.Start(definition, 190);
        var result = session.Execute(new("player"), new ExecutePrisonerCommand(Guid.NewGuid(), 1, 0, 7));
        Assert.True(result.Submission.Accepted);
        Assert.DoesNotContain(session.Snapshot.Simulation.RandomStreams, s => s.Name == "personnel-execution-relations");
        Assert.DoesNotContain(result.Events, e => e is ForceRelationChangedEvent);
        Assert.Equal(1, Assert.Single(session.Snapshot.Treasures).HolderId);
    }

    [Fact]
    public void PlayerRulerExecutionWaitsInMemoryAndReplaysChoiceFromStableState()
    {
        var definition = Fixture(playerRuler: true);
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        session.Apply(session.Request(new EndControlOperation(), "player"));
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var execute = new ExecutePrisonerCommand(Guid.NewGuid(), 2, 0, 1);
        restored.Execute(new("ai:2"), execute);
        var execution = session.Execute(new("ai:2"), execute);
        Assert.True(execution.Submission.Accepted);
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        Assert.Empty(execution.Events.OfType<OfficerDiedEvent>());
        Assert.DoesNotContain(session.Snapshot.Simulation.RandomStreams, s => s.Name == "personnel-execution-relations");
        Assert.Equal(1, Assert.Single(session.Snapshot.Treasures).HolderId);
        var prompt = session.GetExecutionSuccessionPrompt()!;
        Assert.Equal(new[] { 2, 3 }, prompt.CandidateIds);
        string pendingHash = session.Snapshot.StateHash;
        Assert.Equal(CommandRejection.DecisionPending, session.Execute(new("ai:2"), new WaitCommand(Guid.NewGuid(), 2, 1)).Submission.Rejection);
        Assert.Equal(CommandRejection.NotActiveController, session.Execute(new("ai:2"),
            new ChooseExecutionSuccessorCommand(Guid.NewGuid(), 1, 1, 1, 3)).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidDecision, session.Execute(new("player"),
            new ChooseExecutionSuccessorCommand(Guid.NewGuid(), 1, 1, 1, 4)).Submission.Rejection);
        Assert.Equal(pendingHash, session.Snapshot.StateHash);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        Assert.Equal(CanonicalJson.Text(prompt), CanonicalJson.Text(restored.GetExecutionSuccessionPrompt()));
        var response = new ChooseExecutionSuccessorCommand(Guid.NewGuid(), 1, 1, 1, 3);
        var request = session.Request(new CommandOperation(response), "player", response.CommandId);
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.Apply(request).Events));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Equal(3, session.Snapshot.Forces.Single(f => f.Id == 1).LeaderId);
        Assert.Equal(OfficerStatus.Dead, session.Snapshot.Officers.Single(p => p.Id == 1).Status);
        Assert.Equal(4, Assert.Single(session.Snapshot.Treasures).HolderId);
        Assert.Null(session.GetExecutionSuccessionPrompt());
        Assert.Equal(SimulationBoundary.Stable, session.Snapshot.Simulation.Boundary);
        Assert.Equal(2, session.Snapshot.Simulation.Controls[session.Snapshot.Simulation.ActiveControl].ForceId);
        Assert.Empty(result.Events.OfType<StageCompletedEvent>());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void SelectedChildCorpsBecomesPrimaryAndRemainsLoadable()
    {
        var original = Fixture(playerRuler: true);
        var definition = original with
        {
            Settlements = [original.Settlements[0] with { CorpsId = 10 }, original.Settlements[1],
                original.Settlements[0] with { Id = 3, NameKey = "city.3", CorpsId = 20, X = 3, Y = 3 }],
            Officers = original.Officers.Select(p => p.State.Id == 3 ? p with
                { State = p.State with { Status = OfficerStatus.Governor, SettlementId = 3 } } : p).ToArray(),
            Information = original.Information with { Corps = [new(10, 1, 1, null), new(20, 1, 3, new(300, 400))] }
        };
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        session.EndControl(new("player"));
        Assert.True(session.Execute(new("ai:2"), new ExecutePrisonerCommand(Guid.NewGuid(), 2, 0, 1)).Submission.Accepted);
        Assert.True(session.Execute(new("player"), new ChooseExecutionSuccessorCommand(Guid.NewGuid(), 1, 1, 1, 3)).Submission.Accepted);
        var root = Assert.Single(session.Snapshot.Corps);
        Assert.Equal(20, root.Id);
        Assert.Null(root.Treasury);
        Assert.Equal(3, root.LeaderId);
        Assert.All(session.Snapshot.Settlements.Where(s => s.OwnerId == 1), s => Assert.Equal(20, s.CorpsId));
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void PendingSuccessionCannotBePersistedOrUsedAsReplayStart()
    {
        var definition = Fixture(playerRuler: true);
        var session = GameSession.Start(definition, 190);
        session.Apply(session.Request(new EndControlOperation(), "player"));
        Assert.True(session.Execute(new("ai:2"), new ExecutePrisonerCommand(Guid.NewGuid(), 2, 0, 1)).Submission.Accepted);
        var snapshot = new SessionSnapshot(session.Snapshot, [], [], session.NextOperationSequence);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        Assert.Throws<InvalidOperationException>(() => session.BeginRecording());
        var sim = snapshot.World.Simulation;
        foreach (var invalid in new[] { sim with { PendingExecutionSuccession = null },
            sim with { PendingExecutionSuccession = sim.PendingExecutionSuccession! with { ExecutingForceId = 1 } },
            sim with { PendingExecutionSuccession = sim.PendingExecutionSuccession! with { RulerId = 2 } },
            sim with { Boundary = SimulationBoundary.Stable } })
            Assert.Throws<ArgumentException>(() => GameSession.Restore(definition, snapshot with { World = snapshot.World with { Simulation = invalid } }));

    }
}
