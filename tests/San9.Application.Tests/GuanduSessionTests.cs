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

public sealed class GuanduSessionTests
{
    private static OfficerState Person(int id, int owner, int home, OfficerStatus status = OfficerStatus.Serving) =>
        new OfficerState(id, owner, home, new(50, 50, 50, 50), false) { Status = status }.WithLoyalty(owner, 89);
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "guandu", 190, 2, 1, 1,
        [new(1, "force.1", 1, 100) { LeaderId = 11, BaseTrust = 100 },
         new(2, "force.2", 2, 100) { LeaderId = 21, BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 1000, 100, 100, 60, []) { Kind = SettlementKind.City, ConsumesFood = false },
         new(2, "enemy", 2, 7, 0, 1000, 100, 100, 60, []) { Kind = SettlementKind.City, ConsumesFood = false },
         new(3, "guandu", 1, 1, 0, 1000, 100, 100, 60, []) { Kind = SettlementKind.Facility, ConsumesFood = false },
         new(4, "regional", 2, 4, 0, 30000, 100, 100, 60, []) { Kind = SettlementKind.Facility, ConsumesFood = false }],
        new() { [1] = "home", [2] = "enemy", [3] = "guandu", [4] = "regional" },
        new() { [1] = 1, [2] = 2, [3] = 1, [4] = 2 })
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false,
        Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10001)) { IsRoot = true }], []) { Relations = [new(1, 2, 19)] },
        FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false, ForceConsolidationEnabled = false,
        Officers = [new("cao", Person(11, 1, 3, OfficerStatus.Ruler)), new("actor", Person(12, 1, 1)),
            new("yuan", Person(21, 2, 2, OfficerStatus.Ruler)),
            new("xu", Person(23, 2, 1, OfficerStatus.Captive).WithLoyalty(1, 0))],
        Military = new(8, 2, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, 1, 1)],
            [new(1, 1, 0, 100000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 2), new(1, 1)), new(2, new(37, 2), new(1, 1)), new(3, new(7, 2), new(1, 1)), new(4, new(22, 2), new(1, 1))])
            { RegionIds = Enumerable.Range(0, 16).Select(i => (int?)((i % 8) switch { 0 => 1, 1 => 3, 7 => 2, _ => 4 })).ToImmutableArray() },
        Guandu = new(11, 21, 23, 3, [1, 3], [4], 90, 20, 30000, 90, 70, 50, 15, 15, new("guandu"))
    };

    [Fact]
    public void PublicRecruitmentGuaranteesSuccessBeforeTransferAndReplaysAcrossSaveBoundaries()
    {
        var definition = Definition(); var session = GameSession.Start(definition, 17);
        session.BeginRecording();
        var command = new RecruitOfficerCommand(Guid.NewGuid(), 1, 0, 12, 23);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var result = session.AdvanceXun();
        Assert.Single(result.Events.OfType<GuanduEvent>());
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Equal(3000u, session.Snapshot.Settlements.Single(s => s.Id == 4).Soldiers);
        Assert.Equal(3001u, session.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 2).Treasury.Food);
        Assert.Equal(150, session.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
        Assert.Equal(85, session.Snapshot.Forces.Single(f => f.Id == 2).BaseTrust);
        var recruit = session.Snapshot.Officers.Single(o => o.Id == 23);
        Assert.Equal((1, 100), (recruit.OwnerId, recruit.Loyalty));
        Assert.DoesNotContain(result.Events.OfType<OfficerLoyaltyChangedEvent>(), e => e.OfficerId == 23 && e.Reason == "guandu");
        Assert.True(Assert.Single(result.Events.OfType<RecruitmentResolvedEvent>()).Success);
        var saved = SaveService.Decode(SaveService.Encode(session), definition);
        StateAssert.Equal(session.Capture(), saved.Capture());
        Assert.Empty(session.AdvanceXun().Events.OfType<GuanduEvent>());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void ConsumedEventFallsBackToNormalRecruitmentWithoutDamage()
    {
        var definition = Definition() with { ConsumedEventKeys = [new("guandu")] };
        var session = GameSession.Start(definition, 17);
        Assert.True(session.Execute(new("player"), new RecruitOfficerCommand(Guid.NewGuid(), 1, 0, 12, 23)).Submission.Accepted);
        var result = session.AdvanceXun();
        Assert.Empty(result.Events.OfType<GuanduEvent>());
        Assert.False(Assert.Single(result.Events.OfType<RecruitmentResolvedEvent>()).Success);
        Assert.Equal(30000u, session.Snapshot.Settlements.Single(s => s.Id == 4).Soldiers);
        Assert.Equal(2, session.Snapshot.Officers.Single(o => o.Id == 23).OwnerId);
    }

    [Fact]
    public void EnemyDispatchOfTheTargetCancelsRecruitmentBeforeHistoricalEffects()
    {
        var basis = Definition();
        var definition = basis with { Officers = basis.Officers.Select(o => o.State.Id == 23
            ? o with { State = o.State with { Status = OfficerStatus.Serving, SettlementId = 2 } } : o).ToArray() };
        var session = GameSession.Start(definition, 17);
        var command = new RecruitOfficerCommand(Guid.NewGuid(), 1, 0, 12, 23);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        session.EndControl(new("player"));
        Assert.True(session.Execute(new("ai:2"), new MoveOfficersCommand(Guid.NewGuid(), 2,
            session.Snapshot.Simulation.NextSequence, [23], 4)).Submission.Accepted);
        var result = session.AdvanceXun();
        Assert.Contains(result.Events.OfType<OfficerMovedEvent>(), e => e.OfficerId == 23);
        Assert.Contains(result.Events.OfType<TaskCancelledEvent>(), e => e.TaskId == command.CommandId);
        Assert.Empty(result.Events.OfType<GuanduEvent>());
        Assert.DoesNotContain(new PersistentEventKey("guandu"), session.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Equal(30000u, session.Snapshot.Settlements.Single(s => s.Id == 4).Soldiers);
    }

    [Fact]
    public void DefinitionValidatesAllReferences()
    {
        var definition = Definition(); var d = definition.Guandu!;
        foreach (var invalid in new[] { d with { PatronId = 99 }, d with { OpponentId = d.PatronId }, d with { RecruitId = 99 },
            d with { RequiredPlaceId = 99 }, d with { PatronRegionIds = [] }, d with { EnemyRegionIds = [4, 4] },
            d with { EnemyRegionIds = [99] }, d with { LoyaltyExclusive = 257 }, d with { RelationExclusive = -1 },
            d with { MinimumEnemySoldiers = -1 }, d with { SoldierLossPercent = 101 }, d with { FoodLossPercent = -1 },
            d with { LoyaltyBonus = -1 }, d with { BaseTrustBonus = -1 }, d with { EnemyBaseTrustPenalty = -1 } })
            Assert.Throws<InvalidDataException>(() => (definition with { Guandu = invalid }).Validate());
        Assert.Throws<InvalidDataException>(() => (definition with { Military = definition.Military! with { RegionIds = [] } }).Validate());

        var document = JsonNode.Parse(CanonicalJson.Encode(definition))!.AsObject(); document.Remove("guandu");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(document.ToJsonString())));
        foreach (string field in new[] { "patronId", "opponentId", "recruitId", "requiredPlaceId", "patronRegionIds", "enemyRegionIds", "consumptionKey" })
        {
            var node = JsonNode.Parse(CanonicalJson.Encode(d))!.AsObject(); node.Remove(field);
            Assert.Throws<JsonException>(() => StrictJson.Deserialize<GuanduDefinition>(Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
    }
}
