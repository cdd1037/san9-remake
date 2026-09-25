using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class LongevitySessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "longevity", 190, 2, 1, 1,
        [new(1, "force", 1, 50000, 10000, 100) { LeaderId = 1, BaseTrust = 100, EventHistory = [new("visited")] }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(0, 100, 0, 0), false) { Status = OfficerStatus.Ruler }),
            new("first", new(2, 1, 1, new(0, 100, 0, 0), false)), new("second", new(3, 1, 1, new(0, 100, 0, 0), false)),
            new("diviner", new(4, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special })],
        SearchEvents = [new LongevitySearchEventDefinition(1, 4, new("visited"), 1, 2500, 500, 7, true)
            { Id = "longevity", Consumption = EventConsumption.None }]
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChoiceResumesDailyQueueAndStableSaveAndRecordingAgree(bool accept)
    {
        var d = Definition(); var session = GameSession.Start(d, 1); session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        session.AdvanceXun(); restored.AdvanceXun();
        var p = Assert.IsType<LongevityPrompt>(session.GetLongevityPrompt());
        Assert.Equal(p, restored.GetLongevityPrompt()); Assert.Equal((7, 500u), (p.MinimumLifespanTier, p.Cost));
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        string before = session.Snapshot.StateHash;
        Assert.Equal(p, session.GetLongevityPrompt()); Assert.Equal(before, session.Snapshot.StateHash);
        Guid id = Guid.NewGuid();
        var answer = new RespondDailyTaskCommand(id, 1, session.Snapshot.Simulation.NextSequence, p.TaskId, p.Step, accept);
        var request = session.Request(new CommandOperation(answer), id: id);
        var first = session.Apply(request); var second = restored.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, first.Disposition);
        Assert.Equal(CanonicalJson.Text(first.Events), CanonicalJson.Text(second.Events));
        Assert.Equal(accept, Assert.Single(first.Events.OfType<LongevityResolvedEvent>()).Accepted);
        Assert.DoesNotContain(first.Events, e => e is SearchResolvedEvent);
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Equal(accept ? 7 : 0, session.Snapshot.Officers.Single(o => o.Id == 2).LifespanTier);
        Assert.Equal(accept ? -500L : 0L, first.Events.OfType<ResourceChangedEvent>().Where(e => e.Reason == "longevity").Sum(e => e.Actual));
        Assert.Equal(new PersistentEventKey("visited"), Assert.Single(session.Snapshot.Forces.Single().EventHistory));
        Assert.Null(session.GetLongevityPrompt());
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(SaveService.Decode(SaveService.Encode(session), d)));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), d, GameSession.EngineFactory(d));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 2, 1)).Submission.Accepted);
        session.AdvanceXun(); Assert.NotNull(session.GetLongevityPrompt());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameDaySearchesObserveTheFirstAnswer(bool acceptFirst)
    {
        var session = GameSession.Start(Definition(), 1);
        foreach (int person in new[] { 2, 3 })
            Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, person, 1)).Submission.Accepted);
        session.AdvanceXun(); var p = session.GetLongevityPrompt()!;
        Assert.True(session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, p.TaskId, p.Step, acceptFirst)).Submission.Accepted);
        var second = session.GetLongevityPrompt();
        Assert.NotNull(second);
        if (second is not null)
        {
            Assert.NotEqual(p.SearchingOfficerId, second.SearchingOfficerId);
            Assert.True(session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
                session.Snapshot.Simulation.NextSequence, second.TaskId, second.Step, true)).Submission.Accepted);
        }
        Assert.Equal(acceptFirst ? 2 : 1, session.Snapshot.Officers.Count(o => o.LifespanTier == 7));
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Equal(SimulationBoundary.Stable, session.Snapshot.Simulation.Boundary);
    }

    [Fact]
    public void ContentReferencesConsumptionPolicyAndCostAreValidatedAndFingerprinted()
    {
        var d = Definition(); var longevity = (LongevitySearchEventDefinition)d.SearchEvents[0];
        foreach (var bad in new[] { longevity with { IntelligenceChanceDivisor = 0 }, longevity with { PlaceId = 999 }, longevity with { DivinerOfficerId = 999 },
            longevity with { Cost = 0 }, longevity with { MinimumLifespanTier = -1 }, longevity with { RequiredHistoryKey = default },
            longevity with { Consumption = EventConsumption.OnPresentation } })
            Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [bad] }).Validate());
        Assert.NotEqual(GameSession.Fingerprint(d), GameSession.Fingerprint(d with { SearchEvents = [longevity with { Cost = 12000 }] }));
        var document = JsonNode.Parse(CanonicalJson.Encode(d))!;
        document["searchEvents"]![0]!.AsObject().Remove("lifespanChangesAllowed");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }
}
