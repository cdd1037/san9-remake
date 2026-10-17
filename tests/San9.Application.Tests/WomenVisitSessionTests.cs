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

public sealed class WomenVisitSessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "visit", 208, 2, 1, 1,
        [new(1, "force", 1, 500) { LeaderId = 1, BaseTrust = 500 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("actor", new(2, 1, 1, new(50, 99, 99, 50), false)),
            new("visitor", new(3, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special }),
            new("second-actor", new(4, 1, 1, new(50, 99, 99, 50), false))],
        SearchEvents = [new WomenVisitSearchEventDefinition([new(1, 3, [101, 102], [], null, new(208, 1, 1), new("returned"))], 8, 50, 1000)
            { Id = "visit", Consumption = EventConsumption.None }],
        Military = new(2, 1, [0, 0], [new(0, 1, null)], [], [new(1, new(2, 2), new(1, 1))])
        { Tactics = [new(101, 7, TacticEffect.CounterConfusion, 0, 0, 0, false, false, []),
            new(102, 7, TacticEffect.CounterConfusion, 0, 0, 0, false, false, [])] }
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }], []) };

    private static ulong Seed()
    {
        const ulong seed = 50UL;
        var random = new StableRandom(StableRandom.DeriveSeed(seed, "event-search-eligibility"));
        Assert.True(random.NextUInt64() % 100 < 12 && random.NextUInt64() % 100 < 12 && random.NextUInt64() % 100 < 12);
        return seed;
    }

    [Fact]
    public void SameDayVisitsCompleteWithoutChoiceAndRepeatThroughSaveAndRecording()
    {
        var d = Definition(); var session = GameSession.Start(d, Seed()); session.BeginRecording();
        foreach (int actor in new[] { 2, 4 })
            Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, [actor], 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        var result = session.AdvanceXun(); var again = restored.AdvanceXun();
        EventAssert.Equal(result.Events, again.Events);
        Assert.Equal(new[] { 2, 4 }, result.Events.OfType<WomenVisitResolvedEvent>().Select(e => e.SearchingOfficerId));
        Assert.All(result.Events.OfType<WomenVisitResolvedEvent>(), e => Assert.Equal(101, e.LearnedTacticId));
        Assert.DoesNotContain(result.Events, e => e is SearchResolvedEvent);
        Assert.Null(session.Snapshot.Simulation.PendingDailyTask);
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Equal(500, Assert.Single(session.Snapshot.Forces).BaseTrust);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, [2], 1)).Submission.Accepted);
        Assert.Equal(102, Assert.Single(session.AdvanceXun().Events.OfType<WomenVisitResolvedEvent>()).LearnedTacticId);
        Assert.Equal(new[] { 101, 102 }, session.Snapshot.Officers.Single(p => p.Id == 2).LearnedTactics);
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), d).Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), d, GameSession.EngineFactory(d));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void ContentRejectsBrokenReferences()
    {
        var d = Definition(); var visit = (WomenVisitSearchEventDefinition)d.SearchEvents[0]; var site = visit.Sites[0];
        foreach (var invalid in new[] { site with { PlaceId = 999 }, site with { VisitorOfficerId = 999 },
            site with { RequiredLeaderIds = [999] }, site with { RewardTacticIds = [999] }, site with { RewardTacticIds = [] },
            site with { RewardTacticIds = [101, 101] }, site with { RequiredWorldKey = default(PersistentEventKey) },
            site with { EarliestScenarioStart = default(GameClock) }, site with { EarliestScenarioStart = null } })
            Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [visit with { Sites = [invalid] }] }).Validate());
        foreach (var invalid in new[] { visit with { Sites = [site, site] }, visit with { PoliticsChanceDivisor = 0 },
            visit with { Consumption = EventConsumption.OnSuccess } })
            Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [invalid] }).Validate());

        var document = JsonNode.Parse(CanonicalJson.Encode(d))!;
        document["searchEvents"]![0]!["sites"]![0]!.AsObject().Remove("earliestScenarioStart");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }
}
