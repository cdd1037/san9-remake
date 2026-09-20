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
        [new(1, "force", 1, 10000, 10000, 500) { LeaderId = 1, BaseTrust = 500 }],
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
    };
    private static ulong Seed() => Enumerable.Range(1, 10000).Select(i => (ulong)i).First(seed =>
        new StableRandom(StableRandom.DeriveSeed(seed, "event-search-eligibility")).NextUInt64() % 100 < 33);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChoicesPersistHistoryConsumptionAndReplayThroughPublicSession(bool accept)
    {
        var definition = Definition();
        var session = GameSession.Start(definition, Seed()); session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        session.AdvanceXun(); restored.AdvanceXun();
        var prompt = Assert.IsType<ScholarReturnPrompt>(session.GetScholarReturnPrompt());
        Assert.Equal(prompt, restored.GetScholarReturnPrompt());
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        string before = session.Snapshot.StateHash;
        Assert.Equal(prompt, session.GetScholarReturnPrompt()); Assert.Equal(before, session.Snapshot.StateHash);
        Assert.Empty(Assert.Single(session.Snapshot.Forces).EventHistory);
        foreach (var bad in new[]
        {
            new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, Guid.NewGuid(), prompt.Step, accept),
            new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step + 1, accept),
            new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, accept) { CountryTitleId = 1 }
        })
        {
            Assert.False(session.Execute(new("player"), bad).Submission.Accepted);
            Assert.Equal(before, session.Snapshot.StateHash);
        }
        Guid id = Guid.NewGuid();
        var command = new RespondDailyTaskCommand(id, 1, session.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, accept);
        var request = session.Request(new CommandOperation(command), id: id);
        var first = session.Apply(request); var second = restored.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, first.Disposition);
        Assert.Equal(CanonicalJson.Text(first.Events), CanonicalJson.Text(second.Events));
        Assert.Equal(accept, Assert.Single(first.Events.OfType<ScholarReturnResolvedEvent>()).Accepted);
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        Assert.Equal(accept, Assert.Single(session.Snapshot.Forces).EventHistory.Contains(new("scholar.accepted")));
        Assert.Equal("scholar", Assert.Single(session.Snapshot.Simulation.ConsumedEventKeys).Value);
        Assert.Null(session.GetScholarReturnPrompt());
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Equal(session.Capture().StateHash, SaveService.Decode(SaveService.Encode(session), definition).Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
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
        Assert.NotEqual(GameSession.Fingerprint(definition), GameSession.Fingerprint(definition with { SearchEvents = [d with { RewardTacticIds = [102, 101] }] }));
        var history = Enumerable.Range(0, 100).Select(i => new PersistentEventKey($"custom.{i}")).ToImmutableArray();
        var expanded = definition with { Forces = definition.Forces.Select(f => f with { EventHistory = history }).ToArray() };
        var reversed = expanded with { Forces = expanded.Forces.Select(f => f with { EventHistory = history.Reverse().ToImmutableArray() }).ToArray() };
        Assert.Equal(GameSession.Fingerprint(expanded), GameSession.Fingerprint(reversed));
        Assert.NotEqual(GameSession.Fingerprint(expanded), GameSession.Fingerprint(definition));
        var session = GameSession.Start(reversed, Seed());
        Assert.Equal(100, Assert.Single(session.Snapshot.Forces).EventHistory.Length);
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(SaveService.Decode(SaveService.Encode(session), expanded)));
        var document = JsonNode.Parse(CanonicalJson.Encode(expanded))!;
        document["forces"]![0]!.AsObject().Remove("eventHistory");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }
}
