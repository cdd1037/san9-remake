using San9.Tests;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Simulation;

namespace San9.Application.Tests;

internal static class AutomaticResponseReplay
{
    internal static IReadOnlyList<DomainEvent> Advance(GameSession session, ScenarioInitialState definition, int xun)
    {
        session.BeginRecording();
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        List<DomainEvent> events = [];
        for (int i = 0; i < xun; i++)
        {
            var result = session.Apply(session.Request(new AdvanceOperation()));
            Assert.Equal(SessionDisposition.Accepted, result.Disposition);
            Assert.Null(session.GetPendingResponse());
            EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
            StateAssert.Equal(session.Capture(), restored.Capture());
            events.AddRange(result.Events);
        }
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
        return events;
    }
}
