using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class QiaoMarriageSessionTests
{
    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "qiao", 190, 3, 21, 1,
        [new(1, "force.1", 1, 10000, 10000, 100) { BaseTrust = 100, LeaderId = 11 },
         new(2, "force.2", 2, 10000, 10000, 100) { BaseTrust = 100, LeaderId = 21 }],
        [new(1, "city.1", 1, 0, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false },
         new(2, "city.2", 2, 1, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false }],
        new() { [1] = "city.1", [2] = "city.2" }, [])
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false,
        FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false,
        ForceConsolidationEnabled = false, CityProvinceIds = new() { [1] = 9, [2] = 8 },
        Officers = [new("ruler.1", new(11, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, LifespanTier = 2 }),
            new("companion", new(12, 1, 1, new(50, 50, 50, 50), false) { LifespanTier = 6 }),
            new("ruler.2", new(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("participant.1", new(31, null, null, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special }),
            new("participant.2", new(32, null, null, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special })],
        Information = InformationDefinitions.Empty with { Relations = [new(1, 2, 80)] },
        QiaoMarriage = new(11, 12, [31, 32], 21, 9, [], new(199, 12, 30), new(209, 12, 30), 3, 7, 20, 25, new("qiao-marriage"))
    };

    [Fact]
    public void PublicQuarterAdvancementUpdatesQuerySavesAndFullReplayWithoutRepeating()
    {
        var definition = Scenario(); var session = GameSession.Start(definition, 17);
        Assert.Empty(session.InitialEvents.OfType<QiaoMarriageEvent>());
        var restored = SaveService.Decode(SaveService.Encode(session), definition); session.BeginRecording();
        var result = session.AdvanceXun();
        Assert.Single(result.Events.OfType<QiaoMarriageEvent>());
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(120, session.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
        Assert.Equal(120, session.View.Forces.Single(f => f.Id == 1).Trust);
        Assert.Equal(5, session.Snapshot.Officers.Single(p => p.Id == 11).LifespanTier);
        Assert.Contains(new PersistentEventKey("qiao-marriage"), session.Snapshot.Simulation.ConsumedEventKeys);
        var after = SaveService.Decode(SaveService.Encode(session), definition);
        for (int i = 0; i < 9; i++)
        {
            var next = session.AdvanceXun();
            Assert.Empty(next.Events.OfType<QiaoMarriageEvent>());
            Assert.Equal(CanonicalJson.Text(next.Events), CanonicalJson.Text(after.AdvanceXun().Events));
        }
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
        Assert.Single(replay.Events.OfType<QiaoMarriageEvent>());
    }

    [Fact]
    public void ConsumedInitialStateAndDisabledContentDoNotProduceTheEvent()
    {
        foreach (var definition in new[] { Scenario() with { QiaoMarriage = null },
            Scenario() with { ConsumedEventKeys = [new("qiao-marriage")] } })
            Assert.Empty(GameSession.Start(definition, 17).AdvanceXun().Events.OfType<QiaoMarriageEvent>());
    }

    [Fact]
    public void ContentReferencesAndRequiredFieldAreValidatedAndRewardsAffectFingerprint()
    {
        var definition = Scenario();
        Assert.Throws<InvalidDataException>(() => (definition with { QiaoMarriage = definition.QiaoMarriage! with { CompanionId = 999 } }).Validate());
        Assert.Throws<InvalidDataException>(() => (definition with { QiaoMarriage = definition.QiaoMarriage! with { ProvinceId = 999 } }).Validate());
        Assert.NotEqual(GameSession.Fingerprint(definition), GameSession.Fingerprint(definition with
            { QiaoMarriage = definition.QiaoMarriage! with { LifespanBonus = 2 } }));
        var json = JsonNode.Parse(CanonicalJson.Text(definition))!.AsObject(); json.Remove("qiaoMarriage");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ScenarioInitialState>(json.ToJsonString(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
