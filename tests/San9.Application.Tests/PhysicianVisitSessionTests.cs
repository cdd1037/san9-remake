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

public sealed class PhysicianVisitSessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "physician", 190, 2, 1, 1,
        [new(1, "force", 1, 500) { LeaderId = 1, BaseTrust = 500 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("actor", new(2, 1, 1, new(50, 99, 99, 50), false)),
            new("physician", new(3, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special }),
            new("second-actor", new(4, 1, 1, new(50, 99, 99, 50), false))],
        SearchEvents = [new PhysicianVisitSearchEventDefinition([new(1, 3, 500)], 6, 75)
            { Id = "physician", Consumption = EventConsumption.None }],
        Information = InformationDefinitions.Empty with
        {
            Corps = [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }],
            Treasures = [new(500, "book", 2, 10, 50, null)],
            TreasureStates = [new(500, 3, TreasureDiscovery.Discovered, null)]
        }
    };

    private static ulong Seed()
    {
        const ulong seed = 37UL;
        var random = new StableRandom(StableRandom.DeriveSeed(seed, "event-search-eligibility"));
        Assert.True(random.NextUInt64() % 100 < 16 && random.NextUInt64() % 100 < 16);
        return seed;
    }

    [Fact]
    public void FirstSameDaySearcherReceivesTheUniqueBookAndSaveReplayPreserveOwnership()
    {
        var d = Definition(); var session = GameSession.Start(d, Seed());         foreach (int actor in new[] { 2, 4 })
            Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, [actor], 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        var result = session.AdvanceXun(); var again = restored.AdvanceXun();
        EventAssert.Equal(result.Events, again.Events);
        Assert.Equal(2, Assert.Single(result.Events.OfType<PhysicianVisitResolvedEvent>()).SearchingOfficerId);
        Assert.Single(result.Events.OfType<TreasureTransferredEvent>());
        Assert.Single(result.Events.OfType<SearchResolvedEvent>());
        Assert.Null(session.Snapshot.Simulation.PendingDailyTask);
        Assert.Equal(2, Assert.Single(session.Snapshot.Treasures).HolderId);
        Assert.Empty(session.Snapshot.Officers.Single(p => p.Id == 2).LearnedTactics);
        Assert.DoesNotContain(result.Events, e => e is OfficerLoyaltyChangedEvent);
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys);
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), d).Capture());
    }

    [Fact]
    public void AuthoredSitesAndReferencesAreValidated()
    {
        var d = Definition(); var visit = (PhysicianVisitSearchEventDefinition)d.SearchEvents[0]; var site = visit.Sites[0];
        foreach (var invalid in new[] { visit with { Sites = [site with { PlaceId = 999 }] },
            visit with { Sites = [site with { PhysicianOfficerId = 999 }] }, visit with { Sites = [site with { TreasureId = 999 }] },
            visit with { Sites = [site, site] }, visit with { Sites = [] }, visit with { PoliticsChanceDivisor = 0 },
            visit with { MinimumBaseTrustExclusive = -1 }, visit with { Consumption = EventConsumption.OnSuccess } })
            Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [invalid] }).Validate());

        var document = JsonNode.Parse(CanonicalJson.Encode(d))!;
        document["searchEvents"]![0]!["sites"]![0]!.AsObject().Remove("treasureId");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }
}
