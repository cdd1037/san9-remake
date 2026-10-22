using San9.Tests;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class SettlementDefectionSessionTests
{
    private static OfficerInitialState Person(int id, int owner, int home, OfficerStatus status) => new($"person.{id}",
        new OfficerState(id, owner, home, new(60, 60, 60, 60), false)
        { Status = status, IsCommander = true, Ambition = status == OfficerStatus.Ruler ? 15 : 5 }
        .WithLoyalty(1, 0).WithLoyalty(2, 255));

    private static ScenarioInitialState Scenario(bool occupied) => new(ScenarioInitialState.CurrentSchemaVersion,
        "settlement-defection", 190, 1, 1, 1,
        [new(1, "force.1", 1, 100) { LeaderId = 1 },
         new(2, "force.2", 2, 100) { LeaderId = 2 }],
        [new(2, "city.2", 2, 20, 0, 200000, 100, 100, 70, []) { Kind = SettlementKind.City, CorpsId = 20, ConsumesFood = false },
         new(3, "city.3", 1, 60, 0, 1000, 100, 100, 70, []) { Kind = SettlementKind.City, CorpsId = 10, ConsumesFood = false },
         new(10, "facility.10", 1, 10, 0, occupied ? 1000u : 0u, 100, 100, occupied ? 70 : 0, [])
             { Kind = SettlementKind.Facility, CorpsId = 10, ConsumesFood = false }],
        new() { [2] = "region.2", [3] = "region.3", [10] = "region.10" }, new() { [2] = 2, [3] = 3, [10] = 2 })
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, FreeOfficerMigrationEnabled = false,
        OfficeRequestsEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false, ForceConsolidationEnabled = false,
        Officers = [Person(1, 1, 3, OfficerStatus.Ruler), Person(2, 2, 2, OfficerStatus.Ruler),
            .. occupied ? new[] { Person(10, 1, 10, OfficerStatus.Serving) } : []],
        Information = new([], [], [], [new(10, 1, 1, new(100000, 200000)) { IsRoot = true }, new(20, 2, 2, new(100000, 200000)) { IsRoot = true }], [])
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void XunTransitionChangesLiveQueryAndRoundTripsSave(bool occupied)
    {
        var content = Scenario(occupied);
        var session = GameSession.Start(content, 17);
        Assert.Empty(session.InitialEvents.OfType<SettlementDefectedEvent>());
        for (int i = 0; i < 2; i++) Assert.Empty(session.AdvanceXun().Events.OfType<SettlementDefectedEvent>());
        var before = SaveService.Decode(SaveService.Encode(session), content);
        var events = session.AdvanceXun().Events;
        var result = Assert.Single(events.OfType<SettlementDefectedEvent>());
        Assert.Equal(new GameClock(190, 2, 1), result.Clock);
        Assert.Equal(occupied ? SettlementDefectionKind.Isolated : SettlementDefectionKind.Abandoned, result.Kind);
        EventAssert.Equal(events, before.AdvanceXun().Events);
        Assert.Equal(2, session.View.Settlements.Single(s => s.Id == 10).OwnerId);
        if (occupied) Assert.Equal(2, session.View.Officers.Single(p => p.Id == 10).OwnerId);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Empty(restored.InitialEvents);
        var next = session.AdvanceXun();
        Assert.Empty(next.Events.OfType<SettlementDefectedEvent>());
        EventAssert.Equal(next.Events, restored.AdvanceXun().Events);
    }

    [Fact]
    public void SettlementDefectionSwitchIsRequiredAndRetainsSavedConfiguration()
    {
        var content = Scenario(false);
        var disabled = content with { SettlementDefectionsEnabled = false };

        var session = GameSession.Start(disabled, 17);
        for (int i = 0; i < 4; i++) Assert.Empty(session.AdvanceXun().Events.OfType<SettlementDefectedEvent>());
        var restored = SaveService.Decode(SaveService.Encode(session), disabled);
        Assert.Equal(1, restored.View.Settlements.Single(s => s.Id == 10).OwnerId);
        var snapshot = session.Capture();
        Assert.False(GameSession.Restore(content, snapshot).Snapshot.Simulation.SettlementDefectionsEnabled);
        var state = JsonNode.Parse(CanonicalJson.Text(snapshot.World.Simulation))!.AsObject();
        Assert.True(state.Remove("settlementDefectionsEnabled"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SimulationState>(state.ToJsonString(), CanonicalJson.Options));
        var definition = JsonNode.Parse(CanonicalJson.Text(content))!.AsObject();
        Assert.True(definition.Remove("settlementDefectionsEnabled"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ScenarioInitialState>(definition.ToJsonString(), CanonicalJson.Options));
    }
}
