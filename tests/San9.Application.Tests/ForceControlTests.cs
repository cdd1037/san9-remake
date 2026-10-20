using San9.Application.Persistence;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class ForceControlTests
{
    [Fact]
    public void TransferPreservesOrdersAndDelegationAndRoundTripsEveryProtocol()
    {
        var scenario = G01ScenarioTests.Fixture();
        var session = GameSession.Start(scenario, 190);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new DevelopCommand(Guid.NewGuid(), 1, 0,
            new(1, 1, [2], DomesticActivity.Commerce))).Submission.Accepted);
        var before = session.Snapshot;
        var request = session.Request(new ForceControlOperation(1, ControllerKind.Automatic));
        Assert.Equal(SessionDisposition.Accepted, session.Apply(request).Disposition);
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        Assert.Empty(session.Snapshot.Simulation.Controls.Where(c => c.Kind == ControllerKind.Player));
        Assert.Null(session.View.PlayerForceId);
        Assert.Equal(before.Officers, session.Snapshot.Officers);
        Assert.Equal(before.Corps, session.Snapshot.Corps);
        Assert.Equal(before.Simulation.Tasks, session.Snapshot.Simulation.Tasks);
        Assert.Equal(before.Simulation.RandomStreams, session.Snapshot.Simulation.RandomStreams);
        Assert.Equal(before.Simulation.NextSequence, session.Snapshot.Simulation.NextSequence);
        var unauthorized = session.Execute(new("player"), new WaitCommand(Guid.NewGuid(), 1, before.Simulation.NextSequence));
        Assert.False(unauthorized.Submission.Accepted);
        Assert.Equal(SessionDisposition.Accepted, session.TakeOverForce(2).Disposition);
        Assert.Equal(2, session.View.PlayerForceId);
        Assert.Equal(2, session.View.PlayerCorps!.OwnerId);
        Assert.Equal(new ControlSeat("player", 2, ControllerKind.Player), session.Snapshot.Simulation.Controls[0]);
        Assert.True(session.Execute(new("player"), new WaitCommand(Guid.NewGuid(), 2,
            session.Snapshot.Simulation.NextSequence)).Submission.Accepted);
        session.AddChronicleNote("接管后的记录");
        var restored = SaveService.Decode(SaveService.Encode(session), scenario);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Equal(2, restored.View.PlayerForceId);
        session.AdvanceXun();
        restored.AdvanceXun();
        StateAssert.Equal(session.Capture(), restored.Capture());
        string path = Path.Combine(Path.GetTempPath(), $"san9-control-{Guid.NewGuid():N}.jsonl");
        try
        {
            CommandLogService.Write(path, session);
            var replay = GameSession.Start(scenario, 190);
            Assert.Null(CommandLogService.Replay(path, replay).FirstFailure);
            StateAssert.Equal(session.Capture(), replay.Capture());
            var recorded = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario,
                GameSession.EngineFactory(scenario), verifyEvents: true);
            StateAssert.Equal(session.Capture(), recorded.Session.Capture());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TakingOverPreservesOtherPlayersAndCannotReopenCompletedSeats()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 190);
        Assert.Equal(SessionDisposition.Accepted, session.TakeOverForce(2).Disposition);
        Assert.Equal(ControllerKind.Player, session.Snapshot.Simulation.Controls.Single(c => c.ForceId == 1).Kind);
        session.EndControl(new("player"));
        Assert.Equal(2, session.View.PlayerForceId);
        Assert.True(session.CanChangeForceControl);
        Assert.Equal(SessionDisposition.Accepted, session.HandOverForce(1).Disposition);
        Assert.Equal(SessionDisposition.Accepted, session.TakeOverForce(1).Disposition);
        Assert.Equal(2, session.View.PlayerForceId);
        Assert.False(session.Execute(new("player"), new WaitCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence)).Submission.Accepted);
        session.AdvanceXun();
        Assert.Equal(1, session.View.PlayerForceId);
        Assert.True(session.Execute(new("player"), new WaitCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence)).Submission.Accepted);
    }

    [Fact]
    public void ObserverCanAdvanceRestoreAndTakeBackControl()
    {
        var scenario = G01ScenarioTests.Fixture();
        var session = GameSession.Start(scenario, 190);
        session.TakeOverForce(2);
        session.HandOverForce(2);
        session.HandOverForce(1);
        session = SaveService.Decode(SaveService.Encode(session), scenario);
        Assert.Null(session.View.PlayerForceId);
        session.AdvanceXun();
        Assert.Null(session.View.PlayerForceId);
        Assert.NotEmpty(session.Records);
        Assert.Equal(SessionDisposition.Accepted, session.TakeOverForce(2).Disposition);
        Assert.Equal(2, session.Snapshot.Simulation.Controls[0].ForceId);
    }

    [Theory]
    [InlineData(999, ControllerKind.Player, "player")]
    [InlineData(1, ControllerKind.Player, "player")]
    [InlineData(2, ControllerKind.Automatic, "player")]
    [InlineData(2, (ControllerKind)999, "player")]
    [InlineData(2, ControllerKind.Player, "ai:2")]
    public void InvalidTransitionsAreAtomic(int force, ControllerKind kind, string controller)
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 190);
        var before = session.Capture();
        Assert.Equal(SessionDisposition.Rejected, session.Apply(session.Request(new ForceControlOperation(force, kind), controller)).Disposition);
        StateAssert.Equal(before, session.Capture());
    }

    [Fact]
    public void DailyAdvancementCannotChangeControl()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 190);
        session.Apply(session.Request(new PrepareAdvanceOperation()));
        session.Apply(session.Request(new AdvanceDayOperation()));
        Assert.Equal(SimulationBoundary.Advancing, session.Snapshot.Simulation.Boundary);
        var before = session.CaptureCurrentState();
        Assert.Equal(SessionRejection.NotStable, session.TakeOverForce(2).Rejection);
        StateAssert.Equal(before, session.CaptureCurrentState());
    }
}
