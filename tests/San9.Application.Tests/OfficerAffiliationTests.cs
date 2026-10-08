using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class OfficerAffiliationTests
{
    [Theory]
    [InlineData(SettlementKind.City)]
    [InlineData(SettlementKind.Facility)]
    public void OutgoingPeopleRemainInServicePlaceListsWithoutBecomingAvailable(SettlementKind kind)
    {
        var definition = G01ScenarioTests.Fixture();
        definition = definition with
        {
            RegionNameKeys = new Dictionary<int, string> { [1] = "city.1", [2] = "city.2" }
        };
        var world = GameSession.Start(definition, 1).Snapshot;
        var unit = new UnitState(77, 1, new(120, 120), 100) { CommanderId = 1, CorpsId = 1, ServicePlaceId = 1 };
        var task = new PersistentTask(Guid.NewGuid(), "personnel-search", "player", 1, 3, 1,
            world.Clock, world.Clock.Advance(), 0, 0)
        { Journey = new(2, null, null) };
        world = world with
        {
            Settlements = world.Settlements.Select(s => s.Id == 1 ? s with { Kind = kind } : s).ToArray(),
            Units = [unit],
            Officers = world.Officers.Select(o => o.Id is 1 or 2 or 5 ? o with
            {
                SettlementId = null,
                UnitId = 77,
                IsCommander = false,
                Status = o.Id == 5 ? OfficerStatus.Captive : o.Status
            } : o).ToArray(),
            Simulation = world.Simulation with { Tasks = [task] }
        };
        var view = new StrategySnapshot(definition, world);
        var texts = Texts(definition);
        var hash = world.StateHash;
        int[] Ids(StrategySnapshot snapshot, int place) => OfficerCatalog.Read(snapshot, texts, 0, null, null, regionId: place)
            .Rows.Select(r => r.Key.Id).Order().ToArray();

        Assert.Equal([1, 2, 3, 5], Ids(view, 1));
        Assert.Equal([1, 2, 3, 5], OfficerCatalog.Read(view, texts, 0, null, 1).Rows.Select(r => r.Key.Id).Order());
        Assert.True(SettlementContextMenu.Read(view, 1)[1].Enabled);
        var catalog = new AdministrativeCatalog(view, texts);
        var category = kind == SettlementKind.City ? InformationCategory.Cities : InformationCategory.Regions;
        Assert.Equal("3", catalog.Cell(category, 1, "serving").Text);
        Assert.Equal("1", catalog.Cell(category, 1, "captives").Text);
        Assert.Equal("—", catalog.Cell(category, 1, "commander").Text);
        Assert.Equal("0/3", MapHoverInfo.Resolve(view, texts, 1)!.Rows.Single(r => r.Label == "在任").Value.Replace(" ", ""));
        Assert.Equal("1", MapCommandSummary.Settlement(view, texts, 1).Rows.Single(r => r.Label == "俘虏").Value);
        Assert.All(view.Officers.Where(o => o.OwnerId == 1), o => Assert.False(o.IsAvailable));
        var link = AdministrativeDetails.Read(view, texts, new(category, 1)).Links.Single(l => l.Category == InformationCategory.Officers);
        Assert.Equal(1, link.RegionId);

        var transferred = new StrategySnapshot(definition, world with { Units = [unit with { ServicePlaceId = 2 }] });
        Assert.Equal([3], Ids(transferred, 1));
        Assert.Equal([1, 2, 4, 5, 6], Ids(transferred, 2));
        Assert.Equal([1, 2, 3, 5], Ids(view, 1));
        var returned = new StrategySnapshot(definition, world with
        {
            Units = [],
            Officers = world.Officers.Select(o => o.UnitId == 77 ? o with { UnitId = null, SettlementId = 1 } : o).ToArray(),
            Simulation = world.Simulation with { Tasks = [] }
        });
        Assert.Equal([1, 2, 3, 5], Ids(returned, 1));
        Assert.Equal(hash, world.StateHash);
    }

    [Fact]
    public void CatalogAndDetailsSeparateHomeUnitNameAndHiddenForeignTravel()
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        PersistentTask Trip(int officer, int force, int home) => new(Guid.NewGuid(), "personnel-move", "player", force,
            officer, home, world.Clock, world.Clock.Advance(), 0, officer)
        {
            Journey = new(2, null, null)
            {
                Route = new(new(0, 0), new(30, 0), false)
            }
        };
        world = world with
        {
            Units = [new(77, 1, new(120, 120), 100) { CommanderId = 1, CorpsId = 1, ServicePlaceId = 1 }],
            Officers = world.Officers.Select(o => o.Id is 1 or 2 ? o with { SettlementId = null, UnitId = 77 }
                : o.Id == 6 ? o with { Status = OfficerStatus.Free, OwnerId = null } : o).ToArray(),
            Simulation = world.Simulation with { Tasks = [Trip(3, 1, 1), Trip(4, 2, 2)] }
        };
        var view = new StrategySnapshot(definition, world);
        var texts = Texts(definition);
        CatalogCell Cell(int officer, string column)
        {
            var table = OfficerCatalog.Read(view, texts, 1, null, null, officer);
            return Assert.Single(table.Rows).Cells[Array.IndexOf(table.Columns, column)];
        }
        Assert.Equal("city.1", Cell(2, "所属").Text);
        Assert.Equal("officer.1队", Cell(2, "所在").Text);
        Assert.Equal("city.1", Cell(3, "所属").Text);
        Assert.Equal("移动中", Cell(3, "所在").Text);
        Assert.True(Cell(4, "所在").IsUnavailable);
        Assert.Contains("＊", Cell(4, "所在").Text);
        Assert.Equal("city.2", Cell(6, "所属").Text);
        Assert.Equal("city.2", Cell(6, "所在").Text);
        foreach (int id in new[] { 2, 3, 4, 6 })
        {
            var fields = OfficerDetails.Read(view, texts, id).Fields.SelectMany(f => f).ToArray();
            Assert.Equal(Cell(id, "所属").Text, fields.Single(f => f.Label == "所属").Value);
            Assert.Equal(Cell(id, "所在").Text, fields.Single(f => f.Label == "所在").Value);
        }
    }

    private static TextCatalog Texts(San9.Content.Scenarios.ScenarioInitialState definition) =>
        new(TextCatalog.CurrentSchemaVersion, definition.Officers.Select(o => o.NameKey)
            .Concat(definition.Settlements.Select(s => s.NameKey)).Concat(definition.Forces.Select(f => f.NameKey))
            .ToDictionary(k => k, k => k));
}
