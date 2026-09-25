using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class RedCliffsSessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "red-cliffs", 190, 2, 1, 1,
        [new(1, "force.1", 1, 100) { LeaderId = 11, BaseTrust = 100 },
         new(2, "force.2", 2, 100) { LeaderId = 21, BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 1000, 100, 100, 60, []) { Kind = SettlementKind.City, ConsumesFood = false },
         new(2, "enemy", 2, 5, 0, 1000, 100, 100, 60, []) { Kind = SettlementKind.City, ConsumesFood = false },
         new(3, "event", 1, 1, 0, 1000, 100, 100, 60, []) { Kind = SettlementKind.Facility, ConsumesFood = false }],
        new() { [1] = "home", [2] = "enemy", [3] = "event" }, new() { [1] = 1, [2] = 2, [3] = 1 })
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false,
        FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false, ForceConsolidationEnabled = false,
        Officers = [new("ruler", Officer(11, 1, 1, status: OfficerStatus.Ruler)), new("commander", Officer(12, 1, 1)),
            new("sailor", Officer(13, 1, null, 10)), new("enemy.ruler", Officer(21, 2, 2, status: OfficerStatus.Ruler)),
            new("enemy.sailor", Officer(22, 2, null, 20))],
        Units = [new(10, 1, new(7, 7), 1000) { FormationId = 1, ServicePlaceId = 1, CommanderId = 13, Morale = 60 },
            new(20, 2, new(12, 7), 30000) { FormationId = 1, ServicePlaceId = 2, CommanderId = 22, Morale = 60 }],
        Military = new(6, 2, Enumerable.Repeat(1, 12).ToImmutableArray(), [new TerrainPassage(1, 1, 1) { IsWater = true }],
            [new(1, 1, 0, 100000, 5, new(1, 1), TravelMode.Water)],
            [new(1, new(2, 2), new(1, 1)), new(2, new(27, 2), new(1, 1)), new(3, new(7, 2), new(1, 1))])
            { RegionIds = Enumerable.Range(0, 12).Select(i => (int?)(i % 6 == 0 ? 1 : i % 6 == 5 ? 2 : 3)).ToImmutableArray() },
        RedCliffs = new(12, 2, [11], 3, [3], 20, 1, 30000, 90, 50, 15, 15, new("red-cliffs"))
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }], []) };
    private static OfficerState Officer(int id, int force, int? home, int? unit = null, OfficerStatus status = OfficerStatus.Serving) =>
        new OfficerState(id, force, home, new(50, 50, 50, 50), false) { Status = status, UnitId = unit }.WithLoyalty(force, 100);

    [Fact]
    public void MovingCommanderIntoTheRegionTriggersAtNextXunAndReplaysThroughSaveBoundaries()
    {
        var definition = Definition(); var session = GameSession.Start(definition, 17);
        Assert.Empty(session.InitialEvents.OfType<RedCliffsEvent>()); session.BeginRecording();
        var command = new MoveOfficersCommand(Guid.NewGuid(), 1, 0, [12], 3);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var result = session.AdvanceXun();
        Assert.Single(result.Events.OfType<RedCliffsEvent>());
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Equal(3000u, session.Snapshot.Units.Single(u => u.Id == 20).Soldiers);
        Assert.Equal(150, session.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
        Assert.Equal(85, session.Snapshot.Forces.Single(f => f.Id == 2).BaseTrust);
        Assert.Equal(115, session.Snapshot.Officers.Single(p => p.Id == 12).Loyalty);
        var saved = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Empty(saved.InitialEvents); Assert.Equal(session.Capture().StateHash, saved.Capture().StateHash);
        Assert.Empty(session.AdvanceXun().Events.OfType<RedCliffsEvent>());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    [Fact]
    public void InitialConsumptionBlocksAnOtherwiseEligibleNewSession()
    {
        var basis = Definition();
        var definition = basis with { ConsumedEventKeys = [basis.RedCliffs!.ConsumptionKey],
            Officers = basis.Officers.Select(o => o.State.Id == 12 ? o with { State = o.State with { SettlementId = 3 } } : o).ToArray() };
        var session = GameSession.Start(definition, 17);
        Assert.Empty(session.InitialEvents.OfType<RedCliffsEvent>());
        Assert.Equal(30000u, session.Snapshot.Units.Single(u => u.Id == 20).Soldiers);
        Assert.Equal(session.Capture().StateHash, SaveService.Decode(SaveService.Encode(session), definition).Capture().StateHash);
    }

    [Fact]
    public void ContentRequiresGeographyAndValidReferencesAndCanonicalizesSets()
    {
        var definition = Definition(); var battle = definition.RedCliffs!;
        foreach (var invalid in new[] { battle with { OfficerId = 999 }, battle with { OpposingCityId = 3 },
            battle with { ExcludedRulerIds = [999] }, battle with { OfficerRegionId = 999 }, battle with { RegionIds = [] },
            battle with { RegionIds = [3, 3] }, battle with { RequiredRelationExclusive = 102 }, battle with { MinimumFriendlyWaterUnits = 0 },
            battle with { MinimumEnemySoldiers = -1 }, battle with { SoldierLossPercent = 101 }, battle with { LoyaltyBonus = -1 } })
            Assert.Throws<InvalidDataException>(() => (definition with { RedCliffs = invalid }).Validate());
        Assert.Throws<InvalidDataException>(() => (definition with { Military = definition.Military! with { RegionIds = [] } }).Validate());
        Assert.NotEqual(GameSession.Fingerprint(definition), GameSession.Fingerprint(definition with { RedCliffs = battle with { SoldierLossPercent = 80 } }));
        Assert.Equal(GameSession.Fingerprint(definition with { RedCliffs = battle with { RegionIds = [1, 3], ExcludedRulerIds = [11, 21] } }),
            GameSession.Fingerprint(definition with { RedCliffs = battle with { RegionIds = [3, 1], ExcludedRulerIds = [21, 11] } }));
        var document = JsonNode.Parse(CanonicalJson.Encode(definition))!.AsObject(); document.Remove("redCliffs");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(document.ToJsonString())));
        foreach (string field in new[] { "officerId", "regionIds", "soldierLossPercent", "consumptionKey" })
        {
            var node = JsonNode.Parse(CanonicalJson.Encode(battle))!.AsObject(); node.Remove(field);
            Assert.Throws<JsonException>(() => StrictJson.Deserialize<RedCliffsDefinition>(Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
    }
}
