using San9.Tests;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ScholarInsultSessionTests
{
    internal static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "insult", 190, 2, 1, 1,
        [new(1, "force", 1, 100) { LeaderId = 1, BaseTrust = 100, EventHistory = [new("visited")] }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(0, 150, 0, 0), false) { Status = OfficerStatus.Ruler }),
            new("first", new(2, 1, 1, new(0, 150, 0, 0), false)), new("second", new(3, 1, 1, new(0, 150, 0, 0), false)),
            new("diviner", new(4, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special })],
        SearchEvents = [new ScholarInsultSearchEventDefinition(4, 1, 200, 5, 115, 2, 2, 7, 2)
            { Id = "insult", Consumption = EventConsumption.None }]
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(50000, 10000)) { IsRoot = true }], []) };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChoiceResumesDailyQueueAndStableSaveAndRecordingAgree(bool accept)
    {
        var d = Definition(); var session = GameSession.Start(d, 1); session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        session.AdvanceXun(); restored.AdvanceXun();
        var p = Assert.IsType<ScholarInsultPrompt>(session.GetScholarInsultPrompt());
        Assert.Equal(p, restored.GetScholarInsultPrompt()); Assert.Equal(4, p.ScholarOfficerId);
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        var before = session.Snapshot;
        Assert.Equal(p, session.GetScholarInsultPrompt()); StateAssert.Equal(before, session.Snapshot);
        Guid id = Guid.NewGuid();
        var answer = new RespondDailyTaskCommand(id, 1, session.Snapshot.Simulation.NextSequence, p.TaskId, p.Step, accept);
        var request = session.Request(new CommandOperation(answer), id: id);
        var first = session.Apply(request); var second = restored.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, first.Disposition);
        EventAssert.Equal(first.Events, second.Events);
        Assert.Equal(accept, Assert.Single(first.Events.OfType<ScholarInsultResolvedEvent>()).AttemptedExecution);
        Assert.DoesNotContain(first.Events, e => e is SearchResolvedEvent);
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Equal(accept ? 98 : 102, session.Snapshot.Forces.Single().BaseTrust);
        Assert.Equal(OfficerStatus.Special, session.Snapshot.Officers.Single(o => o.Id == 4).Status);
        Assert.Equal(new PersistentEventKey("visited"), Assert.Single(session.Snapshot.Forces.Single().EventHistory));
        Assert.Null(session.GetScholarInsultPrompt());
        StateAssert.Equal(session.Capture(), restored.Capture());
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), d).Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), d, GameSession.EngineFactory(d));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());

    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameDaySearchesObserveTheFirstAnswer(bool acceptFirst)
    {
        var session = GameSession.Start(Definition(), 1);
        foreach (int person in new[] { 2, 3 })
            Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, person, 1)).Submission.Accepted);
        session.AdvanceXun(); var p = session.GetScholarInsultPrompt()!;
        var firstResult = session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, p.TaskId, p.Step, acceptFirst));
        Assert.True(firstResult.Submission.Accepted);
        Assert.Equal(acceptFirst ? 7 : 0, Assert.Single(firstResult.Events.OfType<ScholarInsultResolvedEvent>()).Injury);
        var second = session.GetScholarInsultPrompt();
        Assert.NotNull(second);
        if (second is not null)
        {
            Assert.NotEqual(p.SearchingOfficerId, second.SearchingOfficerId);
            var secondResult = session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
                session.Snapshot.Simulation.NextSequence, second.TaskId, second.Step, true));
            Assert.True(secondResult.Submission.Accepted);
            Assert.Equal(7, Assert.Single(secondResult.Events.OfType<ScholarInsultResolvedEvent>()).Injury);
        }
        Assert.Equal(acceptFirst ? 96 : 100, session.Snapshot.Forces.Single().BaseTrust);
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Equal(SimulationBoundary.Stable, session.Snapshot.Simulation.Boundary);
    }

    [Fact]
    public void IgnoreTrustRewardCanExcludeTheNextSearchOnTheSameDay()
    {
        var d = Definition(); d = d with { Forces = [d.Forces[0] with { BaseTrust = 199 }] };
        var session = GameSession.Start(d, 1);
        foreach (int person in new[] { 2, 3 })
            Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1,
                session.Snapshot.Simulation.NextSequence, person, 1)).Submission.Accepted);
        session.AdvanceXun(); var prompt = session.GetScholarInsultPrompt()!;
        var result = session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, false));
        Assert.True(result.Submission.Accepted);
        Assert.Single(result.Events.OfType<ScholarInsultResolvedEvent>());
        Assert.Null(session.GetScholarInsultPrompt());
        Assert.Equal(201, session.Snapshot.Forces.Single().BaseTrust);
        Assert.Equal(SimulationBoundary.Stable, session.Snapshot.Simulation.Boundary);
    }

    [Fact]
    public void ContentReferencesThresholdsAndConsumptionPolicyAreValidated()
    {
        var d = Definition(); var insult = (ScholarInsultSearchEventDefinition)d.SearchEvents[0];
        foreach (var bad in new[] { insult with { IntelligenceChanceDivisor = 0 }, insult with { MaximumBaseTrustExclusive = 0 }, insult with { ScholarOfficerId = 999 },
            insult with { IntelligenceReward = -1 }, insult with { RetaliationInjury = 15 }, insult with { MaximumInjuryExclusive = 0 },
            insult with { Consumption = EventConsumption.OnPresentation } })
            Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [bad] }).Validate());

        var document = JsonNode.Parse(CanonicalJson.Encode(d))!;
        document["searchEvents"]![0]!.AsObject().Remove("trustRewardIntelligenceThreshold");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }
}
