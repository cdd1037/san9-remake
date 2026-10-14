using San9.Tests;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class DivinerVisitSessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "diviner", 190, 2, 1, 1,
        [new(1, "force", 1, 500) { LeaderId = 1, BaseTrust = 500 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("actor", new(2, 1, 1, new(50, 99, 99, 50), false)),
            new("diviner", new(3, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special }),
            new("second-actor", new(4, 1, 1, new(50, 99, 99, 50), false))],
        SearchEvents = [new DivinerVisitSearchEventDefinition(1, 3, 3, 50, new("visited"))
            { Id = "diviner", Consumption = EventConsumption.None }]
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }], []) };

    private static ulong Seed()
    {
        const ulong seed = 4UL;
        Assert.True(new StableRandom(StableRandom.DeriveSeed(seed, "event-search-eligibility")).NextUInt64() % 100 < 33);
        return seed;
    }

    [Fact]
    public void FirstSameDayVisitWritesForceHistoryAndPersistsThroughSaveAndRecording()
    {
        var d = Definition(); var session = GameSession.Start(d, Seed()); session.BeginRecording();
        foreach (int actor in new[] { 2, 4 })
            Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, actor, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        var result = session.AdvanceXun(); var again = restored.AdvanceXun();
        EventAssert.Equal(result.Events, again.Events);
        Assert.Equal(2, Assert.Single(result.Events.OfType<DivinerVisitResolvedEvent>()).SearchingOfficerId);
        Assert.Single(result.Events.OfType<SearchResolvedEvent>());
        Assert.Null(session.Snapshot.Simulation.PendingDailyTask);
        Assert.Equal(new PersistentEventKey("visited"), Assert.Single(Assert.Single(session.Snapshot.Forces).EventHistory));
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Equal(0, session.Snapshot.Officers.Single(p => p.Id == 2).LifespanTier);
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), d).Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), d, GameSession.EngineFactory(d));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
        var random = session.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "event-search-eligibility");
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 2, 1)).Submission.Accepted);
        Assert.DoesNotContain(session.AdvanceXun().Events, e => e is DivinerVisitResolvedEvent);
        Assert.Equal(random, session.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "event-search-eligibility"));
    }

    [Fact]
    public void ContentRequiresValidReferencesAndSemanticHistoryKey()
    {
        var d = Definition(); var visit = (DivinerVisitSearchEventDefinition)d.SearchEvents[0];
        foreach (var invalid in new[] { visit with { PlaceId = 999 }, visit with { DivinerOfficerId = 999 },
            visit with { HistoryKey = default }, visit with { PoliticsChanceDivisor = 0 },
            visit with { MinimumTrustExclusive = -1 }, visit with { Consumption = EventConsumption.OnSuccess } })
            Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [invalid] }).Validate());

        var document = JsonNode.Parse(CanonicalJson.Encode(d))!;
        document["searchEvents"]![0]!.AsObject().Remove("historyKey");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }
}
