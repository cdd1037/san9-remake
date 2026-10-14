using San9.Tests;
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
    public void InitialRequestOrderingDoesNotChangeContentOrWorldAndContentStaysUnchanged()
    {
        var content = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "request-order", 190, 1, 1, 1,
            Enumerable.Range(1, 3).Select(id => new ForceInitialState(id, $"force.{id}", id, 100)).ToArray(),
            [new(30, "target", 3, 0, 0, 0, 100, 100, 0, [])], [], [])
        { AttackRequests = [new(2, 1, 30, 19), new(1, 2, 30, 12)] , Information = new([], [], [], Enumerable.Range(1, 3).Select(id => new San9.Core.Domain.CorpsState(id, id, null, new(10000, 100000)) { IsRoot = true }).ToArray(), []) };
        var reordered = content with { AttackRequests = [content.AttackRequests[1], content.AttackRequests[0]] };
        var session = GameSession.Start(content, 1);
        var other = GameSession.Start(reordered, 1);

        StateAssert.Equal(session.Capture(), other.Capture());
        Assert.Equal(new[] { 1, 2 }, session.Snapshot.Simulation.AttackRequests.Select(r => r.RequestedForceId));
        Assert.Equal(new[] { 11, 18 }, session.Snapshot.Simulation.AttackRequests.Select(r => r.RemainingXun));
        Assert.Equal(19, content.AttackRequests[0].RemainingXun);
        Assert.Equal(12, content.AttackRequests[1].RemainingXun);
    }

    [Fact]
    public void RequestExpirySurvivesSaveAndReplayAndMissingStateIsRejected()
    {
        var content = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "request-expiry", 190, 1, 1, 1,
            Enumerable.Range(1, 3).Select(id => new ForceInitialState(id, $"force.{id}", id, 100) { BaseTrust = 100 }).ToArray(),
            Enumerable.Range(1, 3).Select(id => new SettlementInitialState(id * 10, $"city.{id}", id, id, 0, 0, 100, 100, 0, [])).ToArray(), [], [])
        { AttackRequests = [new(1, 2, 30, 3)] , Information = new([], [], [], Enumerable.Range(1, 3).Select(id => new San9.Core.Domain.CorpsState(id, id, null, new(10000, 100000)) { IsRoot = true }).ToArray(), []) };
        var session = GameSession.Start(content, 1);
        Assert.Equal(new(1, 2, 30, 3, 2), Assert.Single(session.Snapshot.Simulation.AttackRequests));
        session.BeginRecording();
        session.AdvanceXun();
        Assert.Equal(1, Assert.Single(session.Snapshot.Simulation.AttackRequests).RemainingXun);
        var saved = SaveService.Encode(session);
        var changed = content with { AttackRequests = [new(1, 2, 30, 4)] };
        StateAssert.Equal(session.Capture(), SaveService.Decode(saved, changed).Capture());
        var restored = SaveService.Decode(saved, content);
        StateAssert.Equal(session.Capture(), restored.Capture());
        session.AdvanceXun();
        restored.AdvanceXun();
        Assert.Empty(session.Snapshot.Simulation.AttackRequests);
        Assert.Equal(80, session.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
        Assert.Equal(AttackRequestEndReason.Expired, Assert.Single(replay.Events.OfType<AttackRequestEndedEvent>()).Reason);
        var json = JsonNode.Parse(Encoding.UTF8.GetString(saved))!;
        Assert.True(json["session"]!["world"]!["simulation"]!.AsObject().Remove("attackRequests"));
        var failure = Assert.Throws<SaveException>(() => SaveService.Decode(Encoding.UTF8.GetBytes(json.ToJsonString()), content));
        Assert.Equal(SaveFailure.InvalidFormat, failure.Failure);
    }
}
