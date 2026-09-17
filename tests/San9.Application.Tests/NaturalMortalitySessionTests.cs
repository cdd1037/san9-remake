using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class NaturalMortalitySessionTests
{
    [Fact]
    public void QuarterlyDeathUpdatesVisibilityAndRoundTripsSaveAndRecording()
    {
        var fixture = G01ScenarioTests.Fixture();
        var scenario = fixture with { Year = 190, Month = 1, Day = 1,
            Officers = fixture.Officers.Append(new OfficerInitialState("elder", new(9000, null,
                fixture.Settlements[0].Id, new(50, 50, 50, 50), false)
                { BirthYear = 90, Status = OfficerStatus.Free })).ToArray() };
        var session = GameSession.Start(scenario, 190);
        for (int i = 0; i < 35; i++) session.AdvanceXun();
        Assert.Contains(session.View.Officers, p => p.Id == 9000);
        var restored = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        session.BeginRecording();
        var result = session.AdvanceXun();
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Equal(9000, Assert.Single(result.Events.OfType<OfficerDiedEvent>()).OfficerId);
        Assert.DoesNotContain(session.View.Officers, p => p.Id == 9000);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario, GameSession.EngineFactory(scenario));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
        var after = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        Assert.Empty(after.InitialEvents);
        Assert.Equal(session.Capture().StateHash, after.Capture().StateHash);
        Assert.NotEqual(session.ContentFingerprint, GameSession.Start(scenario with { NaturalDeathsEnabled = false }, 190).ContentFingerprint);
        Assert.NotEqual(session.ContentFingerprint, GameSession.Start(scenario with
        {
            Officers = scenario.Officers[..^1].Append(scenario.Officers[^1] with
                { State = scenario.Officers[^1].State with { LifespanTier = 8, SuccessionPriority = 4000 } }).ToArray()
        }, 190).ContentFingerprint);
    }
}
