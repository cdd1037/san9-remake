using San9.Tests;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CityIndependenceSessionTests
{
    private static OfficerInitialState Person(int id, int force, int city, int loyalty, OfficerStatus status) =>
        new($"person.{id}", new OfficerState(id, force, city, new(50, 50, 50, 50), false)
        { Status = status, Ambition = 15, IsCommander = status is OfficerStatus.Ruler or OfficerStatus.Governor }.WithLoyalty(force, loyalty));

    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "independence", 190, 1, 1, 1,
        [new(1, "force.1", 1, 100) { LeaderId = 1 }, new(2, "force.2", 2, 100) { LeaderId = 20 }],
        Enumerable.Range(1, 3).Select(i => new SettlementInitialState(i, $"city.{i}", i == 3 ? 2 : 1, i, 0,
            1000, 100, 100, 70, []) { Kind = SettlementKind.City, CorpsId = i == 1 ? 11 : i == 2 ? 10 : 20, ConsumesFood = false }).ToArray(),
        new() { [1] = "region.1", [2] = "region.2", [3] = "region.3" }, new() { [1] = 1, [2] = 2, [3] = 3 })
    {
        NaturalDeathsEnabled = false, AnnualAppearancesEnabled = false, FreeOfficerMigrationEnabled = false,
        OfficeRequestsEnabled = false, ForceEmergenceEnabled = false, ForceConsolidationEnabled = false,
        Officers = [Person(1, 1, 2, 100, OfficerStatus.Ruler), Person(10, 1, 1, 0, OfficerStatus.Governor),
            Person(11, 1, 1, 0, OfficerStatus.Serving), Person(12, 1, 1, 255, OfficerStatus.Serving), Person(20, 2, 3, 100, OfficerStatus.Ruler)],
        Information = new([], [], [], [new(10, 1, 1, new(10000, 100000)) { IsRoot = true }, new(11, 1, 10, new(500, 700)), new(20, 2, 20, new(10000, 100000)) { IsRoot = true }], [])
    };

    [Fact]
    public void PublicXunCreatesIndependentForceAndPreservesSaveReplayAndQuery()
    {
        var content = Scenario();
        var session = GameSession.Start(content, 17);
        session.BeginRecording();
        for (int i = 0; i < 2; i++) Assert.Empty(session.AdvanceXun().Events.OfType<CityIndependenceEvent>());
        var before = SaveService.Decode(SaveService.Encode(session), content);
        var result = session.AdvanceXun();
        Assert.Single(result.Events.OfType<CityIndependenceEvent>());
        EventAssert.Equal(result.Events, before.AdvanceXun().Events);
        Assert.Equal(1, session.Snapshot.Forces.Single(f => f.Id == 3).Origin!.PredecessorId);
        Assert.Equal("person.10", session.View.Forces.Single(f => f.Id == 3).NameKey);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Empty(restored.InitialEvents);
        EventAssert.Equal(session.AdvanceXun().Events, restored.AdvanceXun().Events);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Theory]
    [InlineData("unknown-predecessor")]
    [InlineData("missing-predecessor")]
    [InlineData("wrong-kind")]
    [InlineData("disabled")]
    public void RestoreChecksTheIndependentOriginAndGate(string defect)
    {
        var content = Scenario();
        var session = GameSession.Start(content, 17);
        for (int i = 0; i < 3; i++) session.AdvanceXun();
        var snapshot = session.Capture();
        var world = snapshot.World;
        if (defect == "disabled") world = world with { Simulation = world.Simulation with { CityIndependenceEnabled = false } };
        else world = world with { Forces = world.Forces.Select(f => f.Id != 3 ? f : f with { Origin = defect switch
        {
            "unknown-predecessor" => f.Origin! with { PredecessorId = 500 },
            "missing-predecessor" => f.Origin! with { PredecessorId = null },
            _ => f.Origin! with { Kind = ForceCreationKind.EmptyCityUprising }
        } }).ToArray() };
        Assert.Throws<ArgumentException>(() => GameSession.Restore(content, snapshot with { World = world }));
    }

    [Fact]
    public void ContentAndSaveRequireExplicitIndependenceConfiguration()
    {
        var content = Scenario();

        var json = JsonNode.Parse(CanonicalJson.Text(GameSession.Start(content, 17).Snapshot.Simulation))!.AsObject();
        Assert.True(json.Remove("cityIndependenceEnabled"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SimulationState>(json.ToJsonString(), CanonicalJson.Options));
        var origin = JsonNode.Parse(CanonicalJson.Text(new ForceOrigin(ForceCreationKind.CityIndependence, 10, 1, new(190, 2, 1))
            { PredecessorId = 1 }))!.AsObject();
        Assert.True(origin.Remove("predecessorId"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ForceOrigin>(origin.ToJsonString(), CanonicalJson.Options));
    }

    [Fact]
    public void FailedIndependenceDepartsOnlySuccessfulNonCommanderAndReplays()
    {
        var content = Scenario();
        content = content with { CityConnections = [new(1, 2)], Officers = content.Officers.Select(p =>
            p.State.Id == 10 ? p with { State = p.State.WithLoyalty(1, 255) } : p).ToArray() };
        var session = GameSession.Start(content, 17);
        session.BeginRecording();
        session.AdvanceXun(); session.AdvanceXun();
        var result = session.AdvanceXun();
        Assert.Empty(result.Events.OfType<CityIndependenceEvent>());
        var departure = Assert.Single(result.Events.OfType<OfficerDepartedEvent>());
        Assert.Equal(11, departure.OfficerId);
        Assert.False(departure.ServicePlaceLost);
        Assert.Equal(OfficerStatus.Free, session.Snapshot.Officers.Single(p => p.Id == 11).Status);
        Assert.Equal(1, session.Snapshot.Settlements.Single(s => s.Id == 1).OwnerId);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }
}
