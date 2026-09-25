using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AttackRequestNegotiationSessionTests
{
    private static ScenarioInitialState Content() => new(ScenarioInitialState.CurrentSchemaVersion, "attack-request", 190, 1, 1, 1,
        [new(1, "first", 1, 100) { LeaderId = 11 },
         new(2, "second", 2, 100) { LeaderId = 21 },
         new(3, "third", 3, 100) { LeaderId = 31 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, []), new(2, "away", 2, 18, 0, 0, 100, 100, 0, []),
         new(3, "target", 3, 36, 0, 0, 100, 100, 0, [])], [], [])
    {
        Officers = [new("ruler", new(11, 1, 1, new(50, 50, 100, 50), false) { Status = OfficerStatus.Ruler }),
            new("envoy", new(12, 1, 1, new(50, 50, 100, 50), false)),
            new("ruler2", new(21, 2, 2, new(50, 50, 100, 50), false) { Status = OfficerStatus.Ruler, Righteousness = 5 }),
            new("ruler3", new(31, 3, 3, new(50, 50, 100, 50), false) { Status = OfficerStatus.Ruler })],
        Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 11, new(100000, 100000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(100000, 100000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 31, new(100000, 100000)) { IsRoot = true }], []) { Relations = [new(1, 2, 60), new(1, 3, 0), new(2, 3, 0)] }
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SavedOutboundAndQuotedCommandsReplayExactEvents(bool accept)
    {
        var content = Content();
        var session = GameSession.Start(content, 42);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new RequestAttackCommand(Guid.NewGuid(), 1, 0, 12, 2, 3)).Submission.Accepted);
        var outbound = SaveService.Decode(SaveService.Encode(session), content);
        var arrived = session.AdvanceXun();
        Assert.Equal(CanonicalJson.Text(arrived.Events), CanonicalJson.Text(outbound.AdvanceXun().Events));
        var prompt = session.GetAttackRequestPrompt()!;
        Assert.Equal(CompensationKind.Money, prompt.Offer!.Kind);
        Assert.Equal(1400u, prompt.Offer.Money);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        var restored = outbound;
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, accept);
        var result = session.Execute(new("player"), response);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.Execute(new("player"), response).Events));
        Assert.Equal(accept ? AttackRequestOutcome.Accepted : AttackRequestOutcome.Refused,
            Assert.Single(result.Events.OfType<AttackRequestResolvedEvent>()).Outcome);
        DecisionResponses.DeclineAll(session);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }
}
