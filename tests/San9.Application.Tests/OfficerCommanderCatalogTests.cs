using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class OfficerCommanderCatalogTests
{
    private static Dictionary<int, CatalogCell> Read(ScenarioInitialState definition, WorldSnapshot world)
    {
        var texts = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, definition.Officers.Select(o => o.NameKey)
            .Concat(definition.Settlements.Select(s => s.NameKey)).ToDictionary(key => key, key => key));
        var table = OfficerCatalog.Read(new(definition, world), texts, 1, null, null);
        int column = Array.IndexOf(table.Columns, "大将");
        return table.Rows.ToDictionary(r => r.Key.Id, r => r.Cells[column]);
    }

    [Fact]
    public void SettlementCommanderFollowsCurrentAppointmentRatherThanAbilityOrInitialState()
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        var initial = world with { Officers = world.Officers.Select(o => o with { IsCommander = o.Id == 1 }).ToArray() };
        int commander = initial.Officers.Single(o => o.SettlementId == 1 && o.IsCommander).Id;
        int successor = initial.Officers.First(o => o.SettlementId == 1 && !o.IsCommander).Id;
        var before = Read(definition, initial);
        Assert.Equal(new CatalogCell("○", 1), before[commander]);
        Assert.Equal(new CatalogCell("", 0), before[successor]);
        var changed = initial with
        {
            Officers = initial.Officers.Select(o => o.SettlementId == 1 ? o with { IsCommander = o.Id == successor } : o).ToArray()
        };
        var after = Read(definition, changed);
        Assert.Equal(new CatalogCell("", 0), after[commander]);
        Assert.Equal(new CatalogCell("○", 1), after[successor]);
        Assert.Equal(before[commander], Read(definition, initial)[commander]);
    }

    [Fact]
    public void FieldCommanderUsesUnitAppointmentAndRefreshesAfterReplacement()
    {
        var definition = G01ScenarioTests.Fixture();
        var initial = GameSession.Start(definition, 1).Snapshot;
        var deployed = initial with
        {
            Officers = initial.Officers.Select(o => o.Id is 1 or 2
                ? o with { SettlementId = null, UnitId = 10, IsCommander = false } : o).ToArray(),
            Units = [new(10, 1, MapCoordinates.Center(new(1, 1)), 100) { FormationId = 0, CommanderId = 2 }]
        };
        var cells = Read(definition, deployed);
        Assert.Equal(new CatalogCell("", 0), cells[1]);
        Assert.Equal(new CatalogCell("○", 1), cells[2]);
        var replaced = deployed with { Units = [deployed.Units[0] with { CommanderId = 1 }] };
        cells = Read(definition, replaced);
        Assert.Equal(new CatalogCell("○", 1), cells[1]);
        Assert.Equal(new CatalogCell("", 0), cells[2]);
        Assert.All(deployed.Officers.Where(o => o.UnitId == 10), o => Assert.False(o.IsCommander));
    }

    [Fact]
    public void TravelingOfficerIsNotCommanderAndFreeOrUnlocatedOfficersAreUnavailable()
    {
        var definition = G01ScenarioTests.Fixture();
        var initial = GameSession.Start(definition, 1).Snapshot;
        var current = initial with
        {
            Officers = initial.Officers.Select(o => o.Id switch
            {
                1 => o with { SettlementId = null, IsCommander = false },
                2 => o with { Status = OfficerStatus.Free, OwnerId = null, IsCommander = false },
                3 => o with { SettlementId = null, IsCommander = false },
                _ => o
            }).ToArray(),
            Simulation = initial.Simulation with
            {
                Tasks = [new(Guid.NewGuid(), "personnel-move", "player", 1, 1, 1, initial.Clock, initial.Clock.Advance(), 0, 0)
                    { Journey = new(2, null, null) }]
            }
        };
        var cells = Read(definition, current);
        Assert.Equal(new CatalogCell("", 0), cells[1]);
        Assert.Equal(CatalogCell.Unavailable, cells[2]);
        Assert.Equal(CatalogCell.Unavailable, cells[3]);
    }
}
