using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Application.Tests;

public sealed class CountryTitleCatalogTests
{
    [Fact]
    public void CandidateRowsRetainAuthoredNamesAndDescriptionsWithoutSourceNumberAssumptions()
    {
        var scenario = MissionScenario.Create();
        scenario = scenario with { Information = scenario.Information with
        {
            CountryTitles = [new(new(5000, true, true, false, [], []), "title.a", -1) { Description = "自创国号说明" },
                new(new(7000, true, true, false, [], []), "title.b", -1) { Description = "另一国号" }]
        } };
        var session = GameSession.Start(scenario, 42);
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["title.a"] = "新国", ["title.b"] = "别国" });
        var row = Assert.Single(CountryTitleCatalog.Read(session.View, texts, [5000]));
        Assert.Equal(5000, row.Id);
        Assert.Equal(new[] { "新国", "自创国号说明" }, row.Cells.Select(c => c.Text));
    }
}
