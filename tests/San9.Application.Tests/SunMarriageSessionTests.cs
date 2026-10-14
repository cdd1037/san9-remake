using San9.Tests;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class SunMarriageSessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "sun", 190, 3, 21, 1,
        [new(1, "force.1", 1, 100) { LeaderId = 11 }, new(2, "force.2", 2, 100) { LeaderId = 21 },
         new(3, "force.3", 3, 100) { LeaderId = 31 }],
        [new(1, "city.1", 1, 0, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false },
         new(2, "city.2", 2, 1, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false },
         new(3, "city.3", 3, 2, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false }],
        new() { [1] = "city.1", [2] = "city.2", [3] = "city.3" }, [])
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false,
        FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false, ForceConsolidationEnabled = false,
        Officers = [new("ruler.1", new(11, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("ruler.2", new(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, BloodlineId = 900 }),
            new("bride", new(22, 2, 2, new(50, 50, 50, 50), false)),
            new("ruler.3", new(31, 3, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler })],
        Information = InformationDefinitions.Empty with { Corps = [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 31, new(10000, 10000)) { IsRoot = true }], Relations = [new(1, 2, 80), new(1, 3, 50), new(2, 3, 50)] },
        SunMarriage = new(11, 22, 900, 3, new(209, 12, 30), 70, 20, 150, [101, 102], new("sun-marriage")),
        Military = new(4, 1, [0, 0, 0, 0], [new(0, 1, null)], [],
            [new(1, new(2, 2), new(1, 1)), new(2, new(7, 2), new(1, 1)), new(3, new(12, 2), new(1, 1))])
        { Tactics = [new(101, 7, TacticEffect.CounterConfusion, 0, 0, 0, false, false, []), new(102, 7, TacticEffect.CounterConfusion, 0, 0, 0, false, false, [])] }
    };

    [Fact]
    public void QuarterTransfersAndGrantsOnceAcrossPublicQuerySaveAndReplay()
    {
        var definition = Definition(); var session = GameSession.Start(definition, 17);
        session.BeginRecording(); var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var result = session.AdvanceXun(); Assert.Single(result.Events.OfType<SunMarriageEvent>());
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        var person = session.Snapshot.Officers.Single(p => p.Id == 22);
        Assert.Equal((1, 1, 150), (person.OwnerId, person.SettlementId, person.Loyalty));
        Assert.Equal(new[] { 101, 102 }, person.LearnedTactics);
        Assert.Equal(100, session.Snapshot.Relations.Single(r => r.FirstId == 1 && r.SecondId == 2).Value);
        var after = SaveService.Decode(SaveService.Encode(session), definition);
        for (int i = 0; i < 9; i++)
        {
            var next = session.AdvanceXun(); Assert.Empty(next.Events.OfType<SunMarriageEvent>());
            EventAssert.Equal(next.Events, after.AdvanceXun().Events);
        }
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
        Assert.Single(replay.Events.OfType<SunMarriageEvent>());
    }

    [Fact]
    public void EarlierQiaoEventConsumesOnlyItsKeyAndDefersSunMarriageToNextQuarter()
    {
        var definition = Definition() with { CityProvinceIds = new() { [1] = 9, [2] = 8, [3] = 7 },
            QiaoMarriage = new(11, 12, [22], 21, 9, [], new(199, 12, 30), new(209, 12, 30), 3, 7, 20, 25, new("qiao-marriage")) };
        definition = definition with { Officers = [.. definition.Officers, new("companion", new(12, 1, 1, new(50, 50, 50, 50), false))],
            // A Qiao relation penalty would invalidate the following marriage; use an independent authored rival.
            QiaoMarriage = definition.QiaoMarriage with { RivalRulerId = 31 } };
        var session = GameSession.Start(definition, 17); session.BeginRecording();
        var first = session.AdvanceXun(); Assert.Single(first.Events.OfType<QiaoMarriageEvent>()); Assert.Empty(first.Events.OfType<SunMarriageEvent>());
        for (int i = 0; i < 9; i++)
        {
            var next = session.AdvanceXun();
            if (i < 8) Assert.Empty(next.Events.OfType<SunMarriageEvent>());
            else Assert.Single(next.Events.OfType<SunMarriageEvent>());
        }
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void DefinitionAndInitialConsumptionValidate()
    {
        var definition = Definition();
        Assert.Throws<InvalidDataException>(() => (definition with { SunMarriage = definition.SunMarriage! with { BrideId = 999 } }).Validate());
        Assert.Throws<InvalidDataException>(() => (definition with { SunMarriage = definition.SunMarriage! with { GrantedTacticIds = [999] } }).Validate());

        Assert.Empty(GameSession.Start(definition with { ConsumedEventKeys = [new("sun-marriage")] }, 17).AdvanceXun().Events.OfType<SunMarriageEvent>());
        var json = JsonNode.Parse(CanonicalJson.Text(definition))!.AsObject(); json.Remove("sunMarriage");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ScenarioInitialState>(json.ToJsonString(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
