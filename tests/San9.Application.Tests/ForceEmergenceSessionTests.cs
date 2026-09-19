using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ForceEmergenceSessionTests
{
    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "uprising", 190, 1, 1, 80,
        [new(80, "force.player", 1, 10000, 100000, 100) { LeaderId = 80 }],
        [new(1, "city.empty", null, 0, 0, 0, 100, 100, 0, []) { Kind = SettlementKind.City, ConsumesFood = false },
         new(2, "city.player", 80, 1, 0, 10000, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false }],
        new() { [1] = "region.1", [2] = "region.2" }, new() { [1] = 1, [2] = 2 })
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, FreeOfficerMigrationEnabled = false,
        OfficeRequestsEnabled = false, ForceConsolidationEnabled = false,
        Officers = [new("person.ruler", new(80, 80, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            ..Enumerable.Range(1, 3).Select(id => new OfficerInitialState($"person.{id}",
                new(id, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Free, Ambition = id == 1 ? 10 : 0 }))]
    };

    [Fact]
    public void PublicAdvanceCreatesQueryableAutomaticForceWithSaveAndRecordingRoundTrip()
    {
        var scenario = Scenario();
        var session = GameSession.Start(scenario, 17);
        session.BeginRecording();
        Assert.Empty(session.AdvanceXun().Events.OfType<ForceFoundedEvent>());
        Assert.Empty(session.AdvanceXun().Events.OfType<ForceFoundedEvent>());
        var before = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        var result = session.AdvanceXun();
        var founded = Assert.Single(result.Events.OfType<ForceFoundedEvent>());
        Assert.Equal(81, founded.ForceId);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(before.AdvanceXun().Events));
        var view = session.View.Forces.Single(f => f.Id == 81);
        Assert.Equal("person.1", view.NameKey);
        Assert.InRange(view.ColorCode, 0, 49);
        Assert.Equal(view.ColorCode, session.View.Settlements.Single(s => s.Id == 1).CityMarkerColor);
        var restored = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Empty(restored.InitialEvents);
        Assert.Equal(view, restored.View.Forces.Single(f => f.Id == 81));
        var next = session.AdvanceXun();
        Assert.Empty(next.Events.OfType<ForceFoundedEvent>());
        Assert.Equal(CanonicalJson.Text(next.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario, GameSession.EngineFactory(scenario));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void NewForceUsesLowestExplicitRankRatherThanLowestTrustThreshold()
    {
        var scenario = Scenario();
        scenario = scenario with { Information = scenario.Information with
        { Ranks = [new(9000, "rank.low", 10000, 900, 0, 3), new(1, "rank.high", 30000, 100, 0, 20)] } };
        var session = GameSession.Start(scenario, 17);
        session.BeginRecording();
        for (int i = 0; i < 3; i++) session.AdvanceXun();
        Assert.Equal(9000, session.View.Forces.Single(f => f.Id == 81).RankId);
        var restored = SaveService.Decode(SaveService.Encode(session), scenario);
        Assert.Equal(9000, restored.View.Forces.Single(f => f.Id == 81).RankId);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario, GameSession.EngineFactory(scenario));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Theory]
    [InlineData("missing-origin")]
    [InlineData("early-origin")]
    [InlineData("unknown-founder")]
    [InlineData("player-control")]
    [InlineData("no-root")]
    [InlineData("disabled")]
    public void RestoreRejectsInvalidRuntimeForceProvenanceOrReferences(string defect)
    {
        var scenario = Scenario();
        var session = GameSession.Start(scenario, 17);
        for (int i = 0; i < 3; i++) session.AdvanceXun();
        var snapshot = session.Capture();
        var world = snapshot.World;
        if (defect is "missing-origin" or "early-origin" or "unknown-founder")
            world = world with { Forces = world.Forces.Select(f => f.Id != 81 ? f : f with
            { Origin = defect == "missing-origin" ? null : defect == "early-origin" ? f.Origin! with { Clock = new(190, 1, 1) } : f.Origin! with { FounderId = 999 } }).ToArray() };
        if (defect == "player-control") world = world with { Simulation = world.Simulation with
        { Controls = [..world.Simulation.Controls.Select(c => c.ForceId == 81 ? c with { Kind = ControllerKind.Player } : c)] } };
        if (defect == "no-root") world = world with { Corps = [] };
        if (defect == "disabled") world = world with { Simulation = world.Simulation with { ForceEmergenceEnabled = false } };
        Assert.Throws<ArgumentException>(() => GameSession.Restore(scenario, snapshot with { World = world }));
    }

    [Fact]
    public void EmergenceInputsAffectFingerprintAndSavedFieldsAreRequired()
    {
        var scenario = Scenario();
        var fingerprint = GameSession.Fingerprint(scenario);
        Assert.NotEqual(fingerprint, GameSession.Fingerprint(scenario with { ForceEmergenceEnabled = false }));
        Assert.NotEqual(fingerprint, GameSession.Fingerprint(scenario with { Officers = scenario.Officers.Select(o =>
            o.State.Id == 1 ? o with { State = o.State with { ForceFounderPriority = 1 } } : o).ToArray() }));
        var json = JsonNode.Parse(CanonicalJson.Text(GameSession.Start(scenario, 17).Snapshot.Simulation))!.AsObject();
        Assert.True(json.Remove("forceEmergenceEnabled"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SimulationState>(json.ToJsonString(), CanonicalJson.Options));
    }
}
