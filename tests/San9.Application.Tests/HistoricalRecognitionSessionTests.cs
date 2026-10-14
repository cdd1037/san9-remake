using San9.Tests;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class HistoricalRecognitionSessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "recognition", 190, 3, 1, 1,
        [new(1, "force.1", 1, 600) { LeaderId = 11, BaseTrust = 600, RankId = 500, CountryTitleId = 42 },
         new(2, "force.2", 2, 100) { LeaderId = 21, BaseTrust = 100, RankId = 200 }],
        [new(1, "city.1", 1, 0, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false },
         new(2, "city.2", 1, 1, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false },
         new(3, "city.3", 2, 2, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false }],
        new() { [1] = "city.1", [2] = "city.2", [3] = "city.3" }, [])
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false,
        FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false, ForceConsolidationEnabled = false,
        Officers = Enumerable.Range(11, 7).Select(id => new OfficerInitialState($"person.{id}",
            new OfficerState(id, 1, 1, new(50, 50, 50, 50), false)
            { Status = id == 11 ? OfficerStatus.Ruler : OfficerStatus.Serving }.WithLoyalty(1, 100)))
            .Append(new("ruler.2", new(21, 2, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, BloodlineId = 900 })).ToArray(),
        CityProvinceIds = new() { [1] = 12, [2] = 12, [3] = 13 },
        PoliticalEvents = new(1000, new("court-ended")),
        Information = InformationDefinitions.Empty with
        {
            Corps = [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }],
            Ranks = [new(500, "rank.low", 10000, 0, 0, 10), new(200, "rank.king", 20000, 900, 50, 30), new(100, "rank.emperor", 30000, 1000, 60, 50)],
            CountryTitles = [new(new(42, true, true, false, [], []), "title.42", 42)],
            Relations = [new(1, 2, 80)]
        },
        HanzhongKing = new(11, 1, 200, 30, 600, 20, new("hanzhong-king")),
        FiveTigers = new([12, 13, 14, 15, 16], 30, 15, 200, [101, 102], new("five-tigers")),
        ExpeditionMemorial = new(17, 42, 12, 3, 10, 15, [103, 104], new("expedition-memorial")),
        Military = new(4, 1, [0, 0, 0, 0], [new(0, 1, null)], [],
            [new(1, new(2, 2), new(1, 1)), new(2, new(7, 2), new(1, 1)), new(3, new(12, 2), new(1, 1))])
        { Tactics = Enumerable.Range(101, 4).Select(id => new TacticRule(id, 7, TacticEffect.CounterConfusion, 0, 0, 0, false, false, [])).ToImmutableArray() }
    };

    [Fact]
    public void SuccessiveQuartersCommitOneHistoricalEventAndKeepPublicSaveAndReplayIdentical()
    {
        var definition = Definition(); var session = GameSession.Start(definition, 17); session.BeginRecording();
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        List<DomainEvent> events = [];
        for (int i = 0; i < 30; i++)
        {
            var next = session.AdvanceXun(); events.AddRange(next.Events);
            EventAssert.Equal(next.Events, restored.AdvanceXun().Events);
            if (i == 2)
            {
                Assert.Equal(200, session.View.Forces.Single(f => f.Id == 1).RankId);
                Assert.Empty(events.OfType<FiveTigersRecognizedEvent>()); Assert.Empty(events.OfType<ExpeditionMemorialEvent>());
                Assert.Equal("hanzhong-king", Assert.Single(session.Snapshot.Simulation.ConsumedEventKeys).Value);
            }
            if (i is 2 or 11 or 20) restored = SaveService.Decode(SaveService.Encode(session), definition);
        }
        Assert.Equal(new GameClock(190, 4, 1), Assert.Single(events.OfType<PeerageChangedEvent>(), e => e.Kind == PeerageChangeKind.HanzhongKing).Clock);
        Assert.Equal(new GameClock(190, 7, 1), Assert.Single(events.OfType<FiveTigersRecognizedEvent>()).Clock);
        Assert.Equal(new GameClock(190, 10, 1), Assert.Single(events.OfType<ExpeditionMemorialEvent>()).Clock);
        Assert.Equal(new[] { 101, 102, 103, 104 }, session.Snapshot.Officers.Single(p => p.Id == 12).LearnedTactics);
        Assert.Equal(new[] { 103, 104 }, session.Snapshot.Officers.Single(p => p.Id == 11).LearnedTactics);
        Assert.Equal(3, session.Snapshot.Simulation.ConsumedEventKeys.Length);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void HistoricalKingFeedsIndependentPoliticalTailInSameQuarter()
    {
        var definition = Definition();
        definition = definition with { ImperialCourt = new([99], []),
            Officers = [.. definition.Officers, new("emperor", new(99, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Free })],
            Forces = [definition.Forces[0] with { BaseTrust = 995, Trust = 1000 }, definition.Forces[1]] };
        var session = GameSession.Start(definition, 17); session.BeginRecording(); session.AdvanceXun(); session.AdvanceXun();
        var result = session.AdvanceXun();
        Assert.Equal(new[] { PeerageChangeKind.HanzhongKing, PeerageChangeKind.Abdication }, result.Events.OfType<PeerageChangedEvent>().Select(e => e.Kind));
        Assert.Equal(100, session.View.Forces.Single(f => f.Id == 1).RankId);
        Assert.True(session.View.ImperialCourtEnded); Assert.Empty(result.Events.OfType<FiveTigersRecognizedEvent>());
        Assert.Contains(new PersistentEventKey("hanzhong-king"), session.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Contains(new PersistentEventKey("court-ended"), session.Snapshot.Simulation.ConsumedEventKeys);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void SunMarriageSuccessDefersHistoricalRecognition()
    {
        var definition = Definition() with { PoliticalEvents = null,
            SunMarriage = new(11, 12, 900, 2, new(209, 12, 30), 70, 20, 150, [101], new("sun-marriage")) };
        definition = definition with { Officers = definition.Officers.Select(p => p.State.Id == 12 ? p with { State = p.State with { OwnerId = 2, SettlementId = 3 } } : p).ToArray() };
        var session = GameSession.Start(definition, 17); session.AdvanceXun(); session.AdvanceXun();
        var first = session.AdvanceXun(); Assert.Single(first.Events.OfType<SunMarriageEvent>()); Assert.Empty(first.Events.OfType<PeerageChangedEvent>());
        for (int i = 0; i < 8; i++) session.AdvanceXun();
        Assert.Equal(PeerageChangeKind.HanzhongKing, Assert.Single(session.AdvanceXun().Events.OfType<PeerageChangedEvent>()).Kind);
    }

    [Fact]
    public void InitialConsumptionSkipsEvents()
    {
        var definition = Definition();

        var session = GameSession.Start(definition with { ConsumedEventKeys = [new("hanzhong-king"), new("five-tigers"), new("expedition-memorial")] }, 17);
        session.AdvanceXun(); session.AdvanceXun(); var result = session.AdvanceXun();
        Assert.DoesNotContain(result.Events.OfType<PeerageChangedEvent>(), e => e.Kind == PeerageChangeKind.HanzhongKing);
        Assert.Empty(result.Events.OfType<FiveTigersRecognizedEvent>()); Assert.Empty(result.Events.OfType<ExpeditionMemorialEvent>());
    }

    [Fact]
    public void MissingFieldsAndInvalidAuthoredReferencesAreRejected()
    {
        var definition = Definition();
        ScenarioInitialState[] invalid = [
            definition with { HanzhongKing = definition.HanzhongKing! with { RulerId = 999 } },
            definition with { HanzhongKing = definition.HanzhongKing! with { CityId = 999 } },
            definition with { HanzhongKing = definition.HanzhongKing! with { RankId = 999 } },
            definition with { FiveTigers = definition.FiveTigers! with { OfficerIds = [] } },
            definition with { FiveTigers = definition.FiveTigers! with { OfficerIds = [12, 12] } },
            definition with { FiveTigers = definition.FiveTigers! with { GrantedTacticIds = [999] } },
            definition with { ExpeditionMemorial = definition.ExpeditionMemorial! with { CountryTitleId = 999 } },
            definition with { ExpeditionMemorial = definition.ExpeditionMemorial! with { ProvinceId = 999 } },
            definition with { ExpeditionMemorial = definition.ExpeditionMemorial! with { OpposingCityId = 999 } },
            definition with { ExpeditionMemorial = definition.ExpeditionMemorial! with { GrantedTacticIds = [999] } }];
        foreach (var item in invalid) Assert.Throws<InvalidDataException>(() => item.Validate());
        foreach (string field in new[] { "hanzhongKing", "fiveTigers", "expeditionMemorial" })
        {
            var json = JsonNode.Parse(CanonicalJson.Text(definition))!.AsObject(); json.Remove(field);
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ScenarioInitialState>(json.ToJsonString(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
    }
}
