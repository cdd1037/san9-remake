using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Application.Tests;

public sealed class CommandForceCatalogTests
{
    [Fact]
    public void DelegationReusesPublicDiplomacyCellsAndKeepsDefaultOrderingAcrossPages()
    {
        var definition = MissionScenario.Create();
        var session = GameSession.Start(definition,42);
        var view = new StrategySnapshot(definition,session.View.World with {
            Relations = [new(1,2,80),new(1,3,19)],
            Simulation = session.View.World.Simulation with { Alliance = new(2,1,[3],10) }
        });
        var texts = MissionScenario.Texts(view);
        var catalog = new CommandForceCatalog(view,texts,[2,3]);
        var shared = new AdministrativeCatalog(view,texts);
        Assert.Equal(new[] { "委任","状态","战力","资金","兵粮","外交" },catalog.Pages.Select(p => p.Title));
        foreach (string field in new[] { "relation","alliance" })
        {
            var column = AdministrativeCatalog.Pages(InformationCategory.Forces).SelectMany(p => p.Columns).First(c => c.Id == field);
            Assert.Same(column,catalog.Pages[0].Columns.Single(c => c.Id == field));
            int index = Array.FindIndex(catalog.Pages[0].Columns,c => c.Id == field);
            foreach (int id in new[] {2,3}) Assert.Equal(shared.Cell(InformationCategory.Forces,id,field),catalog.Read(0).Single(r => r.Id == id).Cells[index]);
        }
        for (int page = 0; page < catalog.Pages.Count; page++) Assert.Equal(new[] {3,2},catalog.Read(page).Select(r => r.Id));
    }

    [Theory]
    [InlineData(0,"险恶")]
    [InlineData(20,"不和")]
    [InlineData(40,"中立")]
    [InlineData(60,"好意")]
    [InlineData(80,"信赖")]
    public void SharedFriendshipLabelPreservesNumericSorting(int value, string expected)
    {
        var definition = MissionScenario.Create();
        var session = GameSession.Start(definition,42);
        var view = new StrategySnapshot(definition,session.View.World with { Relations = [new(1,2,value)] });
        var catalog = new AdministrativeCatalog(view,new(TextCatalog.CurrentSchemaVersion,new()));
        var cell = catalog.Cell(InformationCategory.Forces,2,"relation");
        Assert.Equal(expected,cell.Text);
        Assert.Equal(value,cell.Number);
        Assert.True(catalog.Cell(InformationCategory.Forces,1,"relation").IsUnavailable);
        Assert.True(catalog.Cell(InformationCategory.Forces,3,"relation").IsUnavailable);
    }
}
