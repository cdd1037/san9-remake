using San9.Tests;
using San9.Application.Persistence;

namespace San9.Application.Tests;

public sealed class PkSettingsTests
{
    [Fact]
    public void SettingsAreOneRecordedWorldChangeAndSurviveSaveAndReplay()
    {
        var scenario = G01ScenarioTests.Fixture();
        var session = GameSession.Start(scenario, 190);
        session.BeginRecording();
        var request = session.Request(new PkSettingsOperation(false, false, true));
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        var state = session.Snapshot.Simulation;
        Assert.False(state.OtherForceOrdersVisible);
        Assert.False(state.OtherForceTacticsVisible);
        Assert.True(state.BattleDeathsEnabled);
        Assert.Equal(1, session.NextOperationSequence);
        Assert.True(session.HasUnsavedChanges);
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        Assert.Equal(1, session.NextOperationSequence);
        var loaded = SaveService.Decode(SaveService.Encode(session), scenario,
            GameSession.EngineFactory(scenario)).Snapshot.Simulation;
        Assert.Equal((state.OtherForceOrdersVisible, state.OtherForceTacticsVisible, state.BattleDeathsEnabled),
            (loaded.OtherForceOrdersVisible, loaded.OtherForceTacticsVisible, loaded.BattleDeathsEnabled));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario,
            GameSession.EngineFactory(scenario));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
        var fresh = GameSession.Start(scenario, 190).Snapshot.Simulation;
        Assert.True(fresh.OtherForceOrdersVisible);
        Assert.True(fresh.OtherForceTacticsVisible);
        Assert.False(fresh.BattleDeathsEnabled);
    }

    [Fact]
    public void NonPlayerCannotChangeSettingsOrConsumeAnOperation()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 190);
        var before = session.Capture();
        var result = session.Apply(session.Request(new PkSettingsOperation(false, false, true), "computer"));
        Assert.Equal(SessionDisposition.Rejected, result.Disposition);
        Assert.Equal(SessionRejection.InvalidRequest, result.Rejection);
        StateAssert.Equal(before, session.Capture());
    }
}
