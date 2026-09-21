using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ForceRelationCycleSessionTests
{
    [Fact]
    public void QuarterRunsOnceAcrossSaveRestoreAndReplayButNotOtherXun()
    {
        var content = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "relation-cycle", 190, 3, 1, 1,
            [new(1, "force.1", 1, 10000, 10000, 50), new(2, "force.2", 2, 10000, 10000, 50)],
            [new(1, "city.1", 1, 0, 0, 0, 100, 100, 0, []),
             new(2, "city.2", 2, 1, 0, 0, 100, 100, 0, [])], [], [])
        {
            AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false,
            FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false,
            ForceConsolidationEnabled = false,
            Information = InformationDefinitions.Empty with { Relations = [new(1, 2, 80)] }
        };
        var session = GameSession.Start(content, 7);
        session.BeginRecording();
        Assert.Equal(80, Assert.Single(session.Snapshot.Relations).Value);
        for (int i = 0; i < 2; i++) Assert.Empty(session.AdvanceXun().Events.OfType<ForceRelationChangedEvent>());
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        var quarter = session.AdvanceXun();
        Assert.Single(quarter.Events.OfType<ForceRelationChangedEvent>());
        Assert.Equal(CanonicalJson.Text(quarter.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(79, Assert.Single(session.Snapshot.Relations).Value);
        restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Empty(session.AdvanceXun().Events.OfType<ForceRelationChangedEvent>());
        restored.AdvanceXun();
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
        Assert.Single(replay.Events.OfType<ForceRelationChangedEvent>());
    }
}
