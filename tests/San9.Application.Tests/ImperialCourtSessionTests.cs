using San9.Tests;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ImperialCourtSessionTests
{
    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "court", 190, 3, 21, 1,
        [new(1, "force.1", 1, 100) { BaseTrust = 100, LeaderId = 1 },
         new(2, "force.2", 2, 100) { BaseTrust = 100, LeaderId = 2 }],
        [new(1, "city.1", 1, 0, 0, 100, 100, 100, 50, ["emperor"]) { MerchantPresenceChance = 100, Kind = SettlementKind.City, ConsumesFood = false },
         new(2, "city.2", 2, 1, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false }],
        new() { [1] = "city.1", [2] = "city.2" }, [])
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false,
        FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false,
        ForceConsolidationEnabled = false,
        Officers = [new("ruler.1", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("ruler.2", new(2, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("emperor.first", new(12, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Free }),
            new("emperor.next", new(11, null, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Free })],
        ImperialCourt = new([12, 11], [new(12, new(190, 4, 1), new(190, 4, 1), 0)])
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 2, new(10000, 10000)) { IsRoot = true }], []) };

    [Fact]
    public void QuarterPreludeDeathUpdatesCurrentQueryTrustAndIconsAndRoundTrips()
    {
        var definition = Scenario(); var session = GameSession.Start(definition, 17);
        Assert.Equal(12, session.View.EmperorId); Assert.Equal(1, session.View.EmperorCityId);
        Assert.DoesNotContain(session.InitialEvents, e => e is EmperorDeathEvent);
        var before = SaveService.Decode(SaveService.Encode(session), definition);
        session.BeginRecording();
        var result = session.AdvanceXun();
        Assert.Single(result.Events.OfType<EmperorDeathEvent>());
        EventAssert.Equal(result.Events, before.AdvanceXun().Events);
        var ordered = result.Events.ToList();
        Assert.True(ordered.FindIndex(e => e is EmperorDeathEvent) < ordered.FindIndex(e => e is StageCompletedEvent { Stage: ResolutionStage.QuarterPrelude }));
        Assert.Equal(11, session.View.EmperorId); Assert.Equal(2, session.View.EmperorCityId);
        Assert.False(session.View.ImperialCourtEnded);
        Assert.DoesNotContain("emperor", session.View.Settlements.Single(s => s.Id == 1).Icons);
        Assert.Contains("merchant", session.View.Settlements.Single(s => s.Id == 1).Icons);
        Assert.Contains("emperor", session.View.Settlements.Single(s => s.Id == 2).Icons);
        Assert.Equal(100, session.View.Forces.Single(f => f.Id == 1).Trust);
        Assert.Equal(200, session.View.Forces.Single(f => f.Id == 2).Trust);
        var hash = session.Capture();
        _ = session.View.EmperorId; _ = session.View.EmperorCityId;
        StateAssert.Equal(hash, session.Capture());
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        StateAssert.Equal(hash, restored.Capture());
        Assert.Equal(11, restored.View.EmperorId);
        for (int i = 0; i < 9; i++)
        {
            var next = session.AdvanceXun();
            Assert.Empty(next.Events.OfType<EmperorDeathEvent>());
            EventAssert.Equal(next.Events, restored.AdvanceXun().Events);
        }
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void SuccessfulPreludeDefersSecondEligibleDeathUntilNextQuarterAcrossSaveAndReplay()
    {
        var definition = Scenario();
        definition = definition with { ImperialCourt = definition.ImperialCourt with
        { DeathEvents = [definition.ImperialCourt.DeathEvents[0], new(11, new(190, 4, 1), new(190, 4, 1), 0)] } };
        var session = GameSession.Start(definition, 17); session.BeginRecording();
        var first = session.AdvanceXun();
        Assert.Equal(12, Assert.Single(first.Events.OfType<EmperorDeathEvent>()).OfficerId);
        Assert.Equal(11, session.View.EmperorId);
        Assert.Equal(OfficerStatus.Free, session.Snapshot.Officers.Single(p => p.Id == 11).Status);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        for (int i = 0; i < 9; i++)
        {
            var next = session.AdvanceXun();
            EventAssert.Equal(next.Events, restored.AdvanceXun().Events);
            if (i < 8) Assert.Empty(next.Events.OfType<EmperorDeathEvent>());
            else Assert.Equal(11, Assert.Single(next.Events.OfType<EmperorDeathEvent>()).OfficerId);
        }
        Assert.Equal(new GameClock(190, 7, 1), session.Snapshot.Clock);
        Assert.Null(session.View.EmperorId); Assert.False(session.View.ImperialCourtEnded);
        Assert.All(session.View.Forces, f => Assert.Equal(100, f.Trust));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void EndedCourtSuppressesResolutionIconsTrustAndDeathWithoutDiscardingCandidates()
    {
        var definition = Scenario() with { ImperialCourtEnded = true }; var session = GameSession.Start(definition, 17);
        Assert.Null(session.View.EmperorId); Assert.Null(session.View.EmperorCityId);
        Assert.All(session.View.Settlements, s => Assert.DoesNotContain("emperor", s.Icons));
        var result = session.AdvanceXun(); Assert.Empty(result.Events.OfType<EmperorDeathEvent>());
        Assert.All(session.View.Forces, f => Assert.Equal(100, f.Trust));
        Assert.Equal(OfficerStatus.Free, session.Snapshot.Officers.Single(p => p.Id == 12).Status);
        var snapshot = session.Capture();
        var reopened = GameSession.Restore(definition, snapshot with { World = snapshot.World with
        { Simulation = snapshot.World.Simulation with { ImperialCourtEnded = false } } });
        Assert.False(reopened.Snapshot.Simulation.ImperialCourtEnded);
        Assert.NotNull(reopened.View.EmperorId);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("unlisted-death")]
    [InlineData("probability")]
    public void InvalidCourtContentIsRejected(string defect)
    {
        var definition = Scenario(); var court = definition.ImperialCourt;
        court = defect switch
        {
            "unknown" => court with { SuccessionOrder = [12, 999] },
            "duplicate" => court with { SuccessionOrder = [12, 12] },
            "unlisted-death" => court with { DeathEvents = [court.DeathEvents[0] with { OfficerId = 1 }] },
            _ => court with { DeathEvents = [court.DeathEvents[0] with { SurvivalChancePercent = 101 }] }
        };
        Assert.Throws<InvalidDataException>(() => GameSession.Start(definition with { ImperialCourt = court }, 17));
    }

    [Fact]
    public void InstitutionFieldsAreRequired()
    {
        var definition = Scenario();

        var json = JsonNode.Parse(CanonicalJson.Text(definition))!.AsObject(); Assert.True(json.Remove("imperialCourt"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ScenarioInitialState>(json.ToJsonString(), CanonicalJson.Options));
        json = JsonNode.Parse(CanonicalJson.Text(GameSession.Start(definition, 17).Snapshot.Simulation))!.AsObject();
        Assert.True(json.Remove("imperialCourtEnded"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SimulationState>(json.ToJsonString(), CanonicalJson.Options));
    }
}
