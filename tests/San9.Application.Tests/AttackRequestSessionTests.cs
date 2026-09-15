using System.Text;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AttackRequestSessionTests
{
    [Fact]
    public void RequestExpirySurvivesSaveAndReplayAndMissingStateIsRejected()
    {
        var content = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "request-expiry", 190, 1, 1, 1,
            Enumerable.Range(1, 3).Select(id => new ForceInitialState(id, $"force.{id}", id, 10000, 100000, 100) { BaseTrust = 100 }).ToArray(),
            Enumerable.Range(1, 3).Select(id => new SettlementInitialState(id * 10, $"city.{id}", id, id, 0, 0, 100, 100, 0, [])).ToArray(), [], []);
        var original = GameSession.Start(content, 1).Capture();
        var session = GameSession.Restore(content, original with { World = original.World with
        {
            Simulation = original.World.Simulation with { AttackRequests = [new(1, 2, 30, 3, 2)] }
        } });
        session.BeginRecording();
        session.AdvanceXun();
        Assert.Equal(1, Assert.Single(session.Snapshot.Simulation.AttackRequests).RemainingXun);
        var saved = SaveService.Encode(session);
        var restored = SaveService.Decode(saved, content);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        session.AdvanceXun();
        restored.AdvanceXun();
        Assert.Empty(session.Snapshot.Simulation.AttackRequests);
        Assert.Equal(80, session.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
        Assert.Equal(AttackRequestEndReason.Expired, Assert.Single(replay.Events.OfType<AttackRequestEndedEvent>()).Reason);
        var json = JsonNode.Parse(Encoding.UTF8.GetString(saved))!;
        Assert.True(json["session"]!["world"]!["simulation"]!.AsObject().Remove("attackRequests"));
        var failure = Assert.Throws<SaveException>(() => SaveService.Decode(Encoding.UTF8.GetBytes(json.ToJsonString()), content));
        Assert.Equal(SaveFailure.InvalidFormat, failure.Failure);
    }
}
