using San9.Tests;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ForceConsolidationSessionTests
{
    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "consolidation", 190, 1, 1, 1,
        Enumerable.Range(1, 3).Select(i => new ForceInitialState(i, $"force.{i}", i, 100) { LeaderId = i }).ToArray(),
        Enumerable.Range(1, 4).Select(i => new SettlementInitialState(i, $"city.{i}", i <= 2 ? 1 : i - 1, i - 1, 0,
            i <= 2 ? 50000u : 10000u, 100, 100, 50, []) { ConsumesFood = false }).ToArray(),
        new() { [1] = "region.1", [2] = "region.2", [3] = "region.3", [4] = "region.4" },
        new() { [1] = 1, [2] = 2, [3] = 3, [4] = 4 })
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, FreeOfficerMigrationEnabled = false, OfficeRequestsEnabled = false,
        Officers = Enumerable.Range(1, 3).Select(i => new OfficerInitialState($"person.{i}",
            new OfficerState(i, i, i == 1 ? 1 : i + 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler })).ToArray(),
        Military = new(4, 1, [0, 0, 0, 0], [new(0, 1, null)], [],
            Enumerable.Range(1, 4).Select(i => new BattlefieldPlace(i, new(5 * (i - 1) + 2, 2), new(1, 1))).ToImmutableArray())
            { RegionIds = [1, 2, 3, 4], TerritoryExits = [TerritoryExits.All, TerritoryExits.All, TerritoryExits.All, TerritoryExits.All] },
        Information = new([], [], [], Enumerable.Range(1, 3).Select(i => new San9.Core.Domain.CorpsState(i, i, i, new(1000, 1000)) { IsRoot = true }).ToArray(), []) { Relations = [new(1, 2, 0), new(1, 3, 0), new(2, 3, 100)] }
    };

    [Fact]
    public void PendingEventSurvivesSaveAndPublicXunMergesOnceWithCompleteReplay()
    {
        var scenario = Scenario();
        var initial = GameSession.Start(scenario, 17).Capture();
        var session = GameSession.Restore(scenario, initial with
        { World = initial.World with { Simulation = initial.World.Simulation with { ForceConsolidationPending = true } } });
        var restored = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        Assert.True(restored.Snapshot.Simulation.ForceConsolidationPending);
        var result = session.AdvanceXun();
        Assert.Single(result.Events.OfType<ForcesConsolidatedEvent>());
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        Assert.False(session.Snapshot.Simulation.ForceConsolidationPending);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Empty(session.AdvanceXun().Events.OfType<ForcesConsolidatedEvent>());
        var saved = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        StateAssert.Equal(session.Capture(), saved.Capture());
        Assert.Empty(saved.InitialEvents);
    }

    [Fact]
    public void SavedConsolidationSwitchSurvivesChangedInitialConfiguration()
    {
        var scenario = Scenario();
        var session = GameSession.Start(scenario, 17);

        var snapshot = session.Capture();
        Assert.True(GameSession.Restore(scenario with { ForceConsolidationEnabled = false }, snapshot)
            .Snapshot.Simulation.ForceConsolidationEnabled);
    }

    [Theory]
    [InlineData("forceConsolidationEnabled")]
    [InlineData("forceConsolidationPending")]
    public void MissingSavedStateIsRejected(string field)
    {
        var json = JsonNode.Parse(CanonicalJson.Text(GameSession.Start(Scenario(), 17).Snapshot.Simulation))!.AsObject();
        Assert.True(json.Remove(field));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SimulationState>(json.ToJsonString(), CanonicalJson.Options));
    }
}
