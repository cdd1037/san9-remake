using San9.Tests;
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
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Equal(9000, Assert.Single(result.Events.OfType<OfficerDiedEvent>()).OfficerId);
        Assert.DoesNotContain(session.View.Officers, p => p.Id == 9000);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario, GameSession.EngineFactory(scenario));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
        var after = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        Assert.Empty(after.InitialEvents);
        StateAssert.Equal(session.Capture(), after.Capture());

    }
}
