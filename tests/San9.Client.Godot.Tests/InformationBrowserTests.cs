using San9.Application;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Client.Godot.Tests;

public sealed class InformationBrowserTests
{
    private static (GameSession Session, InformationQuery Query) Fixture()
    {
        var scenario = new ScenarioInitialState(7, "information", 190, 1, 1, 5,
            [new(5, "force", 5, 100, 200, 100) { LeaderId = 1, RankId = 0 }],
            [new(15, "city", 5, 80, 60, 1000, 500, 1000, 50, []) { CorpsId = 5, Commerce = 100, MaxCommerce = 1000, Cultivation = 100, MaxCultivation = 1000, Population = 50000 }],
            new() { [15] = "city", [50] = "region" }, new() { [15] = 15, [50] = 15 })
        {
            Officers = [new("ruler", new(1, 5, 15, new(90, 60, 80, 90), false) { Status = OfficerStatus.Ruler, BirthYear = 150 }),
                new("free", new(2, null, null, new(20, 60, 40, 50), false) { Status = OfficerStatus.Free, RegionId = 50 })],
            Information = new([new(0, "rank", 60000, 1000, 5)], [], [new(0, "treasure", 1, 5, 10, null)],
                [new(5, 5, 1, null)], [new(0, 1, true)])
        };
        var text = new TextCatalog(1, new[] { "force", "city", "region", "ruler", "free", "rank", "treasure" }.ToDictionary(s => s, s => new TextEntry(s, s)));
        var session = GameSession.Start(scenario, 1);
        return (session, new(session, text));
    }

    [Fact]
    public void EveryCategoryReadsRealStateAndUnoccupiedRegionIsNotACity()
    {
        var (session, query) = Fixture();
        var before = session.Snapshot.StateHash;
        foreach (var category in Enum.GetValues<InformationCategory>())
        {
            var table = query.Read(category);
            foreach (var row in table.Rows) Assert.Equal(table.Columns.Length, row.Cells.Length);
            for (int page = 0; page < table.Pages.Length; page++)
                foreach (var row in query.Read(category, page).Rows) Assert.Equal(query.Read(category, page).Columns.Length, row.Cells.Length);
        }
        Assert.Single(query.Read(InformationCategory.Cities).Rows);
        Assert.Equal(2, query.Read(InformationCategory.Regions).Rows.Length);
        Assert.Equal(2, query.Read(InformationCategory.Officers).Rows.Length);
        Assert.Single(query.Read(InformationCategory.Officers, force: 5).Rows);
        Assert.Equal("region", query.Read(InformationCategory.Officers).Rows.Single(r => r.Key.Id == 2).Cells[2].Text);
        Assert.False(query.Available(InformationCategory.Units));
        Assert.Equal(before, session.Snapshot.StateHash);
    }

    [Fact]
    public void SuccessfulCommandRefreshesResourcesCorpsAndJournalWithoutChangingInitialChronicle()
    {
        var (session, query) = Fixture();
        string initial = query.Read(InformationCategory.Chronicle).Rows[0].Cells[1].Text;
        Assert.True(session.Develop(new(5, 15, 1, DomesticActivity.Commerce)).Accepted);
        Assert.Equal(50, query.Read(InformationCategory.Forces, 2).Rows[0].Cells[1].Number);
        Assert.Equal(50, query.Read(InformationCategory.Corps, 1).Rows[0].Cells[2].Number);
        Assert.Single(query.Read(InformationCategory.Records).Rows);
        Assert.False(session.Develop(new(5, 15, 1, DomesticActivity.Commerce)).Accepted);
        Assert.Single(session.Records);
        Assert.False(session.Develop(new(5, 15, 2, DomesticActivity.Commerce)).Accepted);
        session.AddChronicleNote("A note");
        Assert.Equal(initial, query.Read(InformationCategory.Chronicle).Rows[0].Cells[1].Text);
        Assert.Contains(query.Read(InformationCategory.Chronicle).Rows, r => r.Cells[1].Text == "A note");
        Assert.Throws<ArgumentException>(() => session.AddChronicleNote(" "));
    }

    [Fact]
    public void NumericSortAndRelatedDetailReturnPreserveIndependentListState()
    {
        var browser = new InformationBrowser(); browser.Open(InformationCategory.Officers);
        InformationRow Row(int id, int value) => new(new(InformationCategory.Officers, id), [value]);
        var table = new InformationTable("test", ["page"], ["number"], [Row(0, 100), Row(1, 9), Row(2, 20)]);
        browser.Sort(0, 1);
        Assert.Equal([1, 2, 0], browser.Order(table).Select(r => r.Key.Id));
        browser.Sort(0, 1);
        Assert.Equal([0, 2, 1], browser.Order(table).Select(r => r.Key.Id));
        browser.Scroll(10, 50); browser.ShowDetail(2);
        browser.Navigate(new("force", InformationCategory.Forces, 5));
        browser.Back();
        Assert.True(browser.IsDetail); Assert.Equal(2, browser.SelectedId); Assert.Equal(10, browser.Offset); Assert.True(browser.Descending);
        browser.Back(); Assert.False(browser.IsDetail); Assert.True(browser.IsOpen);
        browser.Back(); Assert.False(browser.IsOpen);
    }
}
