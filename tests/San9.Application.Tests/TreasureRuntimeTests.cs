using San9.Application;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class TreasureRuntimeTests
{
    [Fact]
    public void RuntimePlacementAndHolderVisibilityDoNotDependOnInitialCatalogMembership()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "runtime-treasures", 190, 1, 1, 5,
            [new(5, "force", 5, 100)], [], new() { [15] = "region" }, [])
        {
            UnbuiltPlaces = [new(15, San9.Core.Domain.MapCoordinates.Center(new(10, 10)))],
            Officers = [new("visible", new(1, null, null, new(50,50,50,50), false) { Status = OfficerStatus.Free, RegionId = 15 }),
                new("hidden", new(2, null, null, new(50,50,50,50), false) { Status = OfficerStatus.Undiscovered, RegionId = 15 })],
            Information = new([], [], [new(0, "buried", 1, 10, null), new(1, "owned", 2, 10, 0)], [new San9.Core.Domain.CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }],
                [new(0, new RegionTreasurePlacement(15)), new(1, new OfficerTreasurePlacement(2))])
        };
        definition = PresentationTacticFixture.WithInfantry(definition);
        var texts = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, new[] { "force", "region", "visible", "hidden", "buried", "owned" }
            .ToDictionary(key => key, key => key));

        definition = definition with { Information = definition.Information with
        { Treasures = definition.Information.Treasures.Select(t => t with { Description = "Description" }).ToArray() } };
        var initial = GameSession.Start(definition, 1).View;
        Assert.Equal(2, initial.World.Officers.Count);
        Assert.Single(initial.Officers);
        Assert.Equal(2, initial.World.Treasures.Count);
        Assert.Equal(2, initial.Information.Treasures.Count);
        Assert.Empty(TreasureCatalog.Read(initial, texts).Rows);

        // Project a later world revision against the same original content definitions.
        var current = initial.World with
        {
            Revision = 1,
            Officers = initial.World.Officers.Select(o => o with { Status = OfficerStatus.Free }).ToArray(),
            Treasures = [initial.World.Treasures[0] with { Placement = new OfficerTreasurePlacement(1) },
                initial.World.Treasures[1]]
        };
        var view = new StrategySnapshot(definition, current);
        Assert.Equal(2, TreasureCatalog.Read(view, texts).Rows.Length);
        Assert.Equal(0, Assert.Single(TreasureCatalog.Read(view, texts, holderId: 1).Rows).Key.Id);
        Assert.Equal("visible", TreasureDetails.Read(view, texts, 0).Holder);
        Assert.Equal("Description", TreasureDetails.Read(view, texts, 0).Description);
        var transferred = new StrategySnapshot(definition, current with
        {
            Revision = 2, Treasures = current.Treasures.Select(t => t.Id == 0 ? t with { Placement = new OfficerTreasurePlacement(2) } : t).ToArray()
        });
        Assert.Empty(TreasureCatalog.Read(transferred, texts, holderId: 1).Rows);
        Assert.Equal("hidden", TreasureDetails.Read(transferred, texts, 0).Holder);
        Assert.Empty(TreasureCatalog.Read(initial, texts).Rows);
        Assert.Null(definition.Information.TreasureStates[0].HolderId);
        Assert.IsType<RegionTreasurePlacement>(definition.Information.TreasureStates[0].Placement);
    }
}
