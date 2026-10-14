using San9.Tests;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PrisonerCycleTests
{
    [Fact]
    public void CaptiveYearEscapesAndPreservesOriginalAllegianceAcrossSaveAndReplay()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            Officers = [.. baseline.Officers, new OfficerInitialState("captive",
                new OfficerState(7, 2, 1, new(50, 50, 50, 50), false)
                { Status = OfficerStatus.Captive, Affinity = 50, Righteousness = 15 }.WithLoyalty(1, 130).WithLoyalty(2, 80))]
        };
        var session = GameSession.Start(definition, 190);
        Assert.Equal(79, session.Snapshot.Officers.Single(p => p.Id == 7).Loyalty);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        session.BeginRecording();
        List<PrisonerEscapedEvent> escapes = [];
        for (int xun = 0; xun < 36; xun++)
        {
            var request = session.Request(new AdvanceOperation());
            var result = session.Apply(request);
            escapes.AddRange(result.Events.OfType<PrisonerEscapedEvent>());
            Assert.Equal(SessionDisposition.Accepted, result.Disposition);
            EventAssert.Equal(result.Events, restored.Apply(request).Events);
            while (session.Snapshot.Simulation.Boundary == SimulationBoundary.AwaitingDecision)
            {
                var answer = DecisionResponses.Decline(session);
                var answered = session.Apply(answer);
                Assert.Equal(SessionDisposition.Accepted, answered.Disposition);
                escapes.AddRange(answered.Events.OfType<PrisonerEscapedEvent>());
                EventAssert.Equal(answered.Events, restored.Apply(answer).Events);
            }
            restored = SaveService.Decode(SaveService.Encode(restored), definition);
            StateAssert.Equal(session.Capture(), restored.Capture());
        }
        var captive = session.Snapshot.Officers.Single(p => p.Id == 7);
        Assert.Equal(2, captive.OwnerId);
        Assert.Equal(2, captive.SettlementId);
        Assert.Equal(OfficerStatus.Serving, captive.Status);
        Assert.Single(escapes);
        Assert.InRange(captive.LoyaltyTo(2), 44, 79);
        Assert.Equal(126, captive.LoyaltyTo(1)); // Four quarterly interest updates toward 50.
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }
}
