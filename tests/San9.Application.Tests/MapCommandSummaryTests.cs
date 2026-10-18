using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class MapCommandSummaryTests
{
    [Theory]
    [InlineData(SettlementKind.City)]
    [InlineData(SettlementKind.Facility)]
    public void ServingTotalIncludesAllFieldMembersAssignedToPlaceButAvailabilityDoesNot(SettlementKind kind)
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        world = world with {
            Settlements = world.Settlements.Select(s => s.Id == 1 ? s with { Kind = kind } : s).ToArray(),
            Officers = world.Officers.Select(o => o.Id is 1 or 2
                ? o with { SettlementId = null, UnitId = 81 } : o).ToArray(),
            Units = [new UnitState(81, 1, new(12, 12), 1000) { CommanderId = 1, ServicePlaceId = 1 }]
        };
        var texts = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, definition.Settlements.ToDictionary(s => s.NameKey, s => s.NameKey));
        string Serving()
        {
            var view = new StrategySnapshot(definition, world);
            var summary = MapCommandSummary.Settlement(view, texts, 1).Rows.Single(r => r.Label == "在任").Value;
            Assert.Equal(summary, MapHoverInfo.Resolve(view, texts, 1, MapInformationContext.Strategy)!.Rows.Single(r => r.Label == "在任").Value);
            return summary;
        }

        Assert.Equal("  1/  3", Serving());
        world = world with { Officers = world.Officers.Select(o => o.Id == 3 ? o with { Acted = true } : o).ToArray() };
        Assert.Equal("  0/  3", Serving());
        world = world with { Officers = world.Officers.Select(o => o.Id == 3 ? o with { Acted = false } : o).ToArray() };
        world = world with { Units = world.Units.Select(u => u with { ServicePlaceId = 2 }).ToArray() };
        Assert.Equal("  1/  1", Serving());
        world = world with { Units = world.Units.Select(u => u with { ServicePlaceId = 1, OwnerId = 2 }).ToArray() };
        Assert.Equal("  1/  1", Serving());
    }

    [Theory]
    [InlineData(SettlementKind.City)]
    [InlineData(SettlementKind.Facility)]
    public void ExecutionCountsActedResidentsAndRefreshesAfterTravelAndReturn(SettlementKind kind)
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        world = world with
        {
            Settlements = world.Settlements.Select(s => s.Id == 1 ? s with { Kind = kind } : s).ToArray(),
            Officers = world.Officers.Select(o => o.Id is 1 or 2
                ? o with { SettlementId = null, UnitId = 81, Acted = true }
                : o.Id == 3 ? o with { Acted = true } : o).ToArray(),
            Units = [new UnitState(81, 1, new(12, 12), 1000) { CommanderId = 1, ServicePlaceId = 1 }]
        };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, definition.Settlements.ToDictionary(s => s.NameKey, s => s.NameKey));
        void Counts(string execution)
        {
            var view = new StrategySnapshot(definition, world);
            Assert.Equal("  0/  3", MapHoverInfo.Resolve(view, texts, 1, MapInformationContext.Strategy)!.Rows.Single(r => r.Label == "在任").Value);
            Assert.Equal(execution, MapHoverInfo.Resolve(view, texts, 1, MapInformationContext.Execution)!.Rows.Single(r => r.Label == "在任").Value);
        }

        Counts("  1/  3");
        var trip = new PersistentTask(Guid.NewGuid(), "personnel-search", "player", 1, 3, 1,
            world.Clock, world.Clock.Advance(), 0, 0) { Journey = new(2, null, null) };
        world = world with { Simulation = world.Simulation with { Tasks = [trip] } };
        Counts("  0/  3");
        world = world with { Simulation = world.Simulation with { Tasks = [] } };
        Counts("  1/  3");
        world = world with
        {
            Units = [],
            Officers = world.Officers.Select(o => o.UnitId == 81 ? o with { SettlementId = 1, UnitId = null } : o).ToArray()
        };
        Counts("  3/  3");
    }
}
