using San9.Application;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class TreasureRuntimeTests
{
    [Fact]
    public void RuntimeDiscoveryAndHolderVisibilityDoNotDependOnInitialCatalogMembership()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "runtime-treasures", 190, 1, 1, 5,
            [new(5, "force", 5, 100)], [], new() { [15] = "region" }, [])
        {
            UnbuiltPlaces = [new(15, San9.Core.Domain.MapCoordinates.Center(new(10, 10)))],
            Officers = [new("visible", new(1, null, null, new(50,50,50,50), false) { Status = OfficerStatus.Free, RegionId = 15 }),
                new("hidden", new(2, null, null, new(50,50,50,50), false) { Status = OfficerStatus.Undiscovered, RegionId = 15 })],
            Information = new([], [], [new(0, "buried", 1, 5, 10, null), new(1, "owned", 2, 3, 10, 0)], [new San9.Core.Domain.CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }],
                [new(0, null, TreasureDiscovery.Buried, 15), new(1, 2, TreasureDiscovery.Discovered, 15),
                 new(100, null, TreasureDiscovery.Disabled, 15)])
        };
        var texts = new TextCatalog(3, new[] { "force", "region", "visible", "hidden", "buried", "owned" }
            .ToDictionary(key => key, key => new TextEntry(key, key)));
        foreach (int id in new[] { 0, 1 }) texts.Entries.Add(TreasureText.DescriptionKey(id), new("Description", "Description"));
        var initial = GameSession.Start(definition, 1).View;
        Assert.Equal(2, initial.World.Officers.Count);
        Assert.Single(initial.Officers);
        Assert.Equal(3, initial.World.Treasures.Count);
        Assert.Equal(2, initial.Information.Treasures.Count);
        Assert.Empty(TreasureCatalog.Read(initial, texts).Rows);

        // Project a later world revision against the same original content definitions.
        var current = initial.World with
        {
            Revision = 1,
            Officers = initial.World.Officers.Select(o => o with { Status = OfficerStatus.Free }).ToArray(),
            Treasures = [initial.World.Treasures[0] with { Discovery = TreasureDiscovery.Discovered, HolderId = 1 },
                initial.World.Treasures[1], initial.World.Treasures[2]]
        };
        var view = new StrategySnapshot(definition, current);
        Assert.Equal(2, TreasureCatalog.Read(view, texts).Rows.Length);
        Assert.Equal(0, Assert.Single(TreasureCatalog.Read(view, texts, holderId: 1).Rows).Key.Id);
        Assert.Equal("visible", TreasureDetails.Read(view, texts, 0).Holder);
        Assert.Equal("Description", TreasureDetails.Read(view, texts, 0).Description);
        var transferred = new StrategySnapshot(definition, current with
        {
            Revision = 2, Treasures = current.Treasures.Select(t => t.Id == 0 ? t with { HolderId = 2 } : t).ToArray()
        });
        Assert.Empty(TreasureCatalog.Read(transferred, texts, holderId: 1).Rows);
        Assert.Equal("hidden", TreasureDetails.Read(transferred, texts, 0).Holder);
        Assert.Empty(TreasureCatalog.Read(initial, texts).Rows);
        Assert.Null(definition.Information.TreasureStates[0].HolderId);
        Assert.Equal(TreasureDiscovery.Buried, definition.Information.TreasureStates[0].Discovery);
    }
}
