using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class SurrenderSessionTests
{
    private static ScenarioInitialState Content() => new(ScenarioInitialState.CurrentSchemaVersion, "surrender", 190, 1, 1, 1,
        [new(1, "first", 1, 100) { LeaderId = 11 },
         new(2, "second", 2, 100) { LeaderId = 21 },
         new(3, "third", 3, 100) { LeaderId = 31 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, []), new(2, "away", 2, 18, 0, 0, 100, 100, 0, []),
         new(3, "third", 3, 36, 0, 0, 100, 100, 0, [])], [], [])
    {
        Officers = [new("ruler", new(11, 1, 1, new(50, 50, 100, 50), false) { Status = OfficerStatus.Ruler, AutomaticSurrenderAllowed = false,
                ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 255) }),
            new("ruler2", new(21, 2, 2, new(50, 50, 100, 50), false) { Status = OfficerStatus.Ruler }),
            new("ruler3", new(31, 3, 3, new(50, 50, 100, 50), false) { Status = OfficerStatus.Ruler })],
        Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 31, new(10000, 10000)) { IsRoot = true }], []) { Relations = [new(1, 2, 80), new(1, 3, 0), new(2, 3, 0)] }
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PlayerDecisionOverridesAutomaticExclusionAndReplaysFromOutboundSave(bool accept)
    {
        var content = Content();
        // Keep the unrelated third force from recruiting and changing the surrender balance before arrival.
        content = content with { Information = content.Information with { Corps = content.Information.Corps
            .Select(c => c.OwnerId == 3 ? c with { Treasury = new(0, 0) } : c).ToArray() } };
        var session = GameSession.Start(content, 42);
        session.BeginRecording();
        session.EndControl(new("player"));
        Assert.True(session.Execute(new("ai:2"), new DemandSurrenderCommand(Guid.NewGuid(), 2, 0, 21, 1)).Submission.Accepted);
        var outbound = SaveService.Decode(SaveService.Encode(session), content);
        Assert.Equal(CanonicalJson.Text(session.AdvanceXun().Events), CanonicalJson.Text(outbound.AdvanceXun().Events));
        var task = Assert.Single(session.Snapshot.Simulation.Tasks, t => t.Surrender is not null);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        var restored = outbound;
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, task.Id, 0, accept);
        var result = session.Execute(new("player"), response);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.Execute(new("player"), response).Events));
        Assert.Equal(accept, Assert.Single(result.Events.OfType<SurrenderResolvedEvent>()).Accepted);
        Assert.Equal(accept, session.Snapshot.Forces.Single(f => f.Id == 1).IsEliminated);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void DiplomacyTraitsAreRequiredValidatedAndPartOfContentFingerprint()
    {
        var content = Content();
        var fingerprint = GameSession.Fingerprint(content);
        Assert.NotEqual(fingerprint, GameSession.Fingerprint(content with { Officers = content.Officers.Select(o =>
            o.State.Id == 11 ? o with { State = o.State with { Ambition = 15 } } : o).ToArray() }));
        Assert.NotEqual(fingerprint, GameSession.Fingerprint(content with { Officers = content.Officers.Select(o =>
            o.State.Id == 11 ? o with { State = o.State with { AutomaticSurrenderAllowed = true } } : o).ToArray() }));
        Assert.False((content.Officers[0].State with { Ambition = 16 }).IsValid());
        foreach (string field in new[] { "ambition", "automaticSurrenderAllowed" })
        {
            var json = JsonNode.Parse(CanonicalJson.Text(content.Officers[0].State))!.AsObject();
            Assert.True(json.Remove(field));
            Assert.Throws<System.Text.Json.JsonException>(() => json.Deserialize<OfficerState>(CanonicalJson.Options));
        }
    }
}
