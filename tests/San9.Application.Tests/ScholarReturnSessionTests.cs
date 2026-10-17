using San9.Tests;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ScholarReturnSessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "scholar", 195, 1, 1, 1,
        [new(1, "force", 1, 500) { LeaderId = 1, BaseTrust = 500 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("actor", new(2, 1, 1, new(50, 99, 99, 50), false)),
            new("scholar", new(3, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special })],
        SearchEvents = [new ScholarReturnSearchEventDefinition(3, 1, new(195, 1, 1), new(207, 12, 30),
            115, 3, 300, 1000, 3000, 2, 2, 2, [101, 102], new("scholar"), new("scholar.accepted"))
            { Id = "scholar", Consumption = EventConsumption.OnPresentation }],
        Military = new(2, 1, [0, 0], [new(0, 1, null)], [], [new(1, new(2, 2), new(1, 1))])
        { Tactics = [new(101, 7, TacticEffect.CounterConfusion, 0, 0, 0, false, false, []),
            new(102, 7, TacticEffect.CounterConfusion, 0, 0, 0, false, false, [])] }
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }], []) };
    private static ulong Seed()
    {
        const ulong seed = 4UL;
        Assert.True(new StableRandom(StableRandom.DeriveSeed(seed, "event-search-eligibility")).NextUInt64() % 100 < 33);
        return seed;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChoicesPersistHistoryConsumptionAndReplayThroughPublicSession(bool accept)
    {
        var definition = Definition();
        var session = GameSession.Start(definition, Seed()); session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, [2], 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        session.AdvanceXun(); restored.AdvanceXun();
        var prompt = Assert.IsType<ScholarReturnPrompt>(session.GetScholarReturnPrompt());
        Assert.Equal(prompt, restored.GetScholarReturnPrompt());
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        var before = session.Snapshot;
        Assert.Equal(prompt, session.GetScholarReturnPrompt()); StateAssert.Equal(before, session.Snapshot);
        Assert.Empty(Assert.Single(session.Snapshot.Forces).EventHistory);
        Guid id = Guid.NewGuid();
        var command = new RespondDailyTaskCommand(id, 1, session.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, accept);
        var request = session.Request(new CommandOperation(command), id: id);
        var first = session.Apply(request); var second = restored.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, first.Disposition);
        EventAssert.Equal(first.Events, second.Events);
        Assert.Equal(accept, Assert.Single(first.Events.OfType<ScholarReturnResolvedEvent>()).Accepted);
        Assert.Equal(accept, Assert.Single(session.Snapshot.Forces).EventHistory.Contains(new("scholar.accepted")));
        Assert.Equal("scholar", Assert.Single(session.Snapshot.Simulation.ConsumedEventKeys).Value);
        Assert.Null(session.GetScholarReturnPrompt());
        StateAssert.Equal(session.Capture(), restored.Capture());
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), definition).Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void ContentValidatesReferencesAndPersistsExpandableForceHistory()
    {
        var definition = Definition(); var d = (ScholarReturnSearchEventDefinition)definition.SearchEvents[0];
        foreach (var bad in new[] { d with { ScholarOfficerId = 999 }, d with { PlaceId = 999 },
            d with { RewardTacticIds = [999] }, d with { PoliticsChanceDivisor = 0 },
            d with { EarliestDate = default }, d with { EarliestDate = new(208, 1, 1) }, d with { SuccessHistoryKey = default },
            d with { RewardTacticIds = [101, 101] }, d with { ConsumptionKey = default } })
            Assert.Throws<InvalidDataException>(() => (definition with { SearchEvents = [bad] }).Validate());

        var history = Enumerable.Range(0, 100).Select(i => new PersistentEventKey($"custom.{i}")).ToImmutableArray();
        var expanded = definition with { Forces = definition.Forces.Select(f => f with { EventHistory = history }).ToArray() };
        var reversed = expanded with { Forces = expanded.Forces.Select(f => f with { EventHistory = history.Reverse().ToImmutableArray() }).ToArray() };

        var session = GameSession.Start(reversed, Seed());
        Assert.Equal(100, Assert.Single(session.Snapshot.Forces).EventHistory.Length);
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), expanded).Capture());
        var document = JsonNode.Parse(CanonicalJson.Encode(expanded))!;
        document["forces"]![0]!.AsObject().Remove("eventHistory");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }
}
