using San9.Tests;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CorpsControlTests
{
    [Fact]
    public void DelegationRevokesOldContextAndRunsRealCommandsWithReplay()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            Settlements = [baseline.Settlements[0] with { CorpsId = 10 }, baseline.Settlements[1],
                baseline.Settlements[0] with { Id = 3, CorpsId = 11 }],
            Officers = baseline.Officers.Select(p => p.State.Id is 2 or 3 ? p with { State = p.State with { SettlementId = 3 } } : p)
                .Append(new OfficerInitialState("free", new OfficerState(7, null, 3, new(30, 30, 30, 30), false)
                    { Status = OfficerStatus.Free }.WithLoyalty(1, 100))).ToArray(),
            Information = baseline.Information with { Corps = [.. baseline.Information.Corps.Where(c => c.OwnerId != 1), new(10, 1, 1, new(1000, 2000)) { IsRoot = true }, new(11, 1, 2, new(1000, 2000))] }
        };
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SetCorpsControlCommand(Guid.NewGuid(), 1, 0, 11, CorpsControlMode.Automatic)).Submission.Accepted);
        var command = new RecruitOfficerCommand(Guid.NewGuid(), 1, 1, 2, 7);
        var hash = session.Snapshot;
        Assert.False(session.Execute(new("player"), command).Submission.Accepted);
        StateAssert.Equal(hash, session.Snapshot);
        Assert.True(session.Validate(new(CorpsControl.ControllerId(11)), command).Accepted);
        Assert.False(session.Validate(new(CorpsControl.ControllerId(11)), command with { OfficerId = 1 }).Accepted);
        Assert.False(session.Validate(new(CorpsControl.ControllerId(11)), new DissolveCorpsCommand(Guid.NewGuid(), 1, 1, 11)).Accepted);
        Assert.True(session.Execute(new("corps:11"), command).Submission.Accepted);
        var result = session.Apply(session.Request(new AdvanceOperation()));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Contains(result.Events, e => e is RecruitmentResolvedEvent);
        Assert.Contains(result.Events, e => e is DevelopmentCompletedEvent { Command.SettlementId: 3 });
        Assert.DoesNotContain(result.Events, e => e is DevelopmentCompletedEvent { Command.SettlementId: 1 });
        var saveState = session.Capture();
        var save = SaveService.Encode(session);
        StateAssert.Equal(saveState, SaveService.Decode(save, definition).Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(saveState, replay.Session.Capture());
        long sequence = session.Snapshot.Simulation.NextSequence;
        Assert.True(session.Execute(new("player"), new SetCorpsControlCommand(Guid.NewGuid(), 1, sequence, 11, CorpsControlMode.Direct)).Submission.Accepted);
        Assert.False(session.Validate(new(CorpsControl.ControllerId(11)), new WaitCommand(Guid.NewGuid(), 1, sequence + 1)).Accepted);
    }
}
