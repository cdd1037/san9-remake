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

public sealed class SageVisitSessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "sage", 190, 2, 1, 1,
        [new(1, "force", 1, 500) { LeaderId = 1, BaseTrust = 500 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("actor", new(2, 1, 1, new(50, 99, 99, 50), false)),
            new("sage", new(3, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special }),
            new("target", new OfficerState(4, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered }.WithLoyalty(1, 80))],
        SearchEvents = [new SageVisitSearchEventDefinition(1, 3, [4], 6, 75, 80, 3)
            { Id = "sage", Consumption = EventConsumption.None }]
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }], []) };

    private static ulong Seed()
    {
        const ulong seed = 37UL;
        var random = new StableRandom(StableRandom.DeriveSeed(seed, "event-search-eligibility"));
        Assert.True(random.NextUInt64() % 100 < 16 && random.NextUInt64() % 100 < 16);
        return seed;
    }

    [Fact]
    public void RecommendationPersistsWithoutDiscoveringOrRecruitingAndNoRewardRepeatEndsSearch()
    {
        var d = Definition(); var session = GameSession.Start(d, Seed()); session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, [2], 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        var result = session.AdvanceXun(); var again = restored.AdvanceXun();
        EventAssert.Equal(result.Events, again.Events);
        Assert.Equal(4, Assert.Single(Assert.Single(result.Events.OfType<SageVisitResolvedEvent>()).LoyaltyRecipientIds));
        Assert.DoesNotContain(result.Events, e => e is SearchResolvedEvent);
        Assert.Null(session.GetSearchRecruitmentPrompt()); Assert.Null(session.Snapshot.Simulation.PendingDailyTask);
        var target = session.Snapshot.Officers.Single(p => p.Id == 4);
        Assert.Equal(83, target.LoyaltyTo(1)); Assert.Equal(OfficerStatus.Undiscovered, target.Status); Assert.Null(target.OwnerId);
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, [2], 1)).Submission.Accepted);
        var repeat = session.AdvanceXun();
        Assert.Empty(Assert.Single(repeat.Events.OfType<SageVisitResolvedEvent>()).LoyaltyRecipientIds);
        Assert.DoesNotContain(repeat.Events, e => e is SearchResolvedEvent);
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), d).Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), d, GameSession.EngineFactory(d));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void AuthoredReferencesAreValidated()
    {
        var d = Definition(); var sage = (SageVisitSearchEventDefinition)d.SearchEvents[0];
        foreach (var invalid in new[] { sage with { PlaceId = 999 }, sage with { SageOfficerId = 999 },
            sage with { RecommendedOfficerIds = [999] }, sage with { RecommendedOfficerIds = [] },
            sage with { RecommendedOfficerIds = [4, 4] }, sage with { PoliticsChanceDivisor = 0 },
            sage with { MinimumBaseTrustExclusive = -1 }, sage with { MaximumRecipientLoyaltyInclusive = 256 },
            sage with { LoyaltyReward = 0 }, sage with { Consumption = EventConsumption.OnSuccess } })
            Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [invalid] }).Validate());

        var document = JsonNode.Parse(CanonicalJson.Encode(d))!;
        document["searchEvents"]![0]!.AsObject().Remove("recommendedOfficerIds");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }
}
