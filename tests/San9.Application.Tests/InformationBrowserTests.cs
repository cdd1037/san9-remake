using San9.Application;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class InformationBrowserTests
{
    private static (GameSession Session, InformationQuery Query) Fixture(bool adjusted = false)
    {
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "information", 190, 1, 1, 5,
            [new(5, "force", 5, 100, 200, 100) { LeaderId = 1, RankId = 0, ProvidedTactics = [1] }],
            [new(15, "city", 5, 80, 60, 1000, 500, 1000, 50, []) { CorpsId = 5, Commerce = 100, MaxCommerce = 1000, Cultivation = 100, MaxCultivation = 1000, Population = 50000 }],
            new() { [15] = "city", [50] = "region" }, new() { [15] = 15, [50] = 15 })
        {
            UnbuiltPlaces = [new(50, San9.Core.Domain.MapCoordinates.Center(new(85, 60)))],
            Officers = [new("ruler", new(1, 5, 15, new(90, 60, 80, 90), false) { Status = OfficerStatus.Ruler, BirthYear = 150, LearnedTactics = [0], TacticProficiencies = [200, 0, 0, 0, 0, 0, 0, 0, 0] }),
                new("free", new(2, null, null, new(20, 60, 40, 50), false) { Status = OfficerStatus.Free, RegionId = 50 })],
            Information = new([new(0, "rank", 60000, 1000, 5, 0)], [], [new(0, "treasure", 1, 5, 10, 2) { AbilityBonuses = new(5, 0, 0, 0) }],
                [new(5, 5, 1, null)], [new(0, 1, TreasureDiscovery.Discovered, 15)])
        };
        if (adjusted)
        {
            var officer = scenario.Officers[0];
            scenario = scenario with { Officers = [officer with { State = officer.State with
                { Injury = 2, OfficeId = 900 } }, scenario.Officers[1]],
                Information = scenario.Information with { Offices = [new(900, "office", 10000, 0) { AbilityBonuses = new(3, 0, 0, 0) }] } };
        }
        var text = new TextCatalog(3, new[] { "force", "city", "region", "ruler", "free", "rank", "treasure", "office" }.ToDictionary(s => s, s => new TextEntry(s, s)));
        var session = GameSession.Start(scenario, 1);
        return (session, new(session, text));
    }

    [Fact]
    public void TreasureDetailProjectsCurrentHolderAndMultilineDescriptionWithoutMutatingWorld()
    {
        var (session, _) = Fixture();
        var texts = new TextCatalog(3, new()
        {
            ["treasure"] = new("测试宝物", "测试宝物"), ["ruler"] = new("持有人", "持有人"),
            [TreasureText.DescriptionKey(0)] = new("第一行\n第二行", "第一行\n第二行")
        });
        var hash = session.Snapshot.StateHash;
        var detail = TreasureDetails.Read(session.View, texts, 0);
        Assert.Equal("持有人", detail.Holder);
        Assert.Equal("第一行\n第二行", detail.Description);
        Assert.Equal("武力+5", detail.Ability);
        Assert.Equal("", detail.Effect);
        Assert.Equal(0, detail.TacticGroup);
        Assert.Equal("奋迅", detail.Tactic);
        Assert.Equal(hash, session.Snapshot.StateHash);
        texts.Entries.Remove(TreasureText.DescriptionKey(0));
        Assert.Throws<InvalidDataException>(() => TreasureDetails.Read(session.View, texts, 0));
    }

    [Fact]
    public void TreasureDetailReturnPreservesFilteredCatalogAndThenOriginalOfficer()
    {
        var browser = new InformationBrowser();
        browser.Open(InformationCategory.Officers); browser.ShowDetail(1);
        browser.Navigate(new("宝物", InformationCategory.Treasures, HolderId: 1));
        browser.Sort(3, 7); browser.Scroll(5, 40); browser.Select(7);
        browser.ShowDetail(7); browser.Select(9); browser.Back();
        Assert.False(browser.IsDetail);
        Assert.Equal(InformationCategory.Treasures, browser.Category);
        Assert.Equal(1, browser.HolderFilter);
        Assert.Equal(9, browser.SelectedId);
        Assert.Equal(5, browser.Offset);
        Assert.Equal(3, browser.SortColumn);
        Assert.True(browser.Descending);
        browser.Back();
        Assert.True(browser.IsDetail);
        Assert.Equal(InformationCategory.Officers, browser.Category);
        Assert.Equal(1, browser.SelectedId);
        Assert.Null(browser.HolderFilter);
    }

    [Fact]
    public void TreasureCatalogFiltersHolderAndReturnsToOriginalOfficerDetail()
    {
        var (session, query) = Fixture();
        var hash = session.Snapshot.StateHash;
        var browser = new InformationBrowser();
        browser.Open(InformationCategory.Officers);
        browser.SelectPage(5, 13);
        browser.Sort(2, 4);
        browser.Scroll(7, 40);
        browser.ShowDetail(1);
        var detail = new OfficerDetailState();
        detail.Open([1, 2], 1);
        detail.SelectPage(OfficerDetailPage.Tactics);
        detail.PointAt(2);
        browser.Navigate(new("宝物", InformationCategory.Treasures, HolderId: 1));
        var table = query.Read(browser);
        Assert.Equal(["宝物", "种类", "能力", "上升", "兵法", "效果", "所有"], table.Columns);
        Assert.Equal("武力", Assert.Single(table.Rows).Cells[2].Text);
        Assert.Equal("奋迅", table.Rows[0].Cells[4].Text);
        browser.Back();
        Assert.Equal(InformationCategory.Officers, browser.Category);
        Assert.True(browser.IsDetail);
        Assert.Equal(1, browser.SelectedId);
        Assert.Equal(5, browser.Page);
        Assert.Equal(7, browser.Offset);
        Assert.Equal(2, browser.SortColumn);
        Assert.Equal(OfficerDetailPage.Tactics, detail.Page);
        Assert.Equal(2, detail.FocusedId);
        browser.Navigate(new("宝物", InformationCategory.Treasures, HolderId: 2));
        Assert.Empty(query.Read(browser).Rows);
        Assert.Equal(hash, session.Snapshot.StateHash);
    }

    [Fact]
    public void OfficerPagesShareCandidatesAndKeepThreeDistinctTacticSources()
    {
        var (session, query) = Fixture();
        var before = session.Snapshot.StateHash;
        var ability = query.Read(InformationCategory.Officers);
        Assert.Equal(13, ability.Pages.Length);
        Assert.Equal(1, ability.Rows.Single(r => r.Key.Id == 1).Cells[5].Number);
        Assert.Equal("良", ability.Rows.Single(r => r.Key.Id == 1).Cells[6].Text);
        var infantry = query.Read(InformationCategory.Officers, 4);
        var cells = infantry.Rows.Single(r => r.Key.Id == 1).Cells;
        Assert.Equal(200, cells[1].Number);
        Assert.Equal(["○", "△", "△"], cells.Skip(2).Select(c => c.Text));
        Assert.All(infantry.Rows.Single(r => r.Key.Id == 2).Cells.Skip(2), c => Assert.Equal("", c.Text));
        for (int page = 0; page < 13; page++)
            Assert.Equal(ability.Rows.Select(r => r.Key), query.Read(InformationCategory.Officers, page).Rows.Select(r => r.Key));
        Assert.Equal(before, session.Snapshot.StateHash);
    }

    [Fact]
    public void OfficerDetailProjectsCurrentAbilitiesFieldsAndOwnedItemsWithoutMutatingWorld()
    {
        var (session, _) = Fixture(adjusted: true);
        var texts = new TextCatalog(3, new Dictionary<string, TextEntry>());
        // The fixture names are their own display strings.
        foreach (string key in session.View.Officers.Select(o => o.NameKey).Concat(session.View.RegionNameKeys.Values)
            .Concat(session.View.Information.Offices.Select(o => o.NameKey))) texts.Entries[key] = new(key, key);
        var before = session.Snapshot.StateHash;
        var detail = OfficerDetails.Read(session.View, texts, 1);
        Assert.Equal(new[] { 6, 4, 5, 6 }, detail.Fields.Select(c => c.Length));
        Assert.Equal(session.View.Officers.Single(o => o.Id == 1).Abilities, detail.RadarAbilities);
        Assert.Equal(89, detail.RadarAbilities.War); // Injury 2 recovers to 1 at the opening xun.
        Assert.Equal("90-1", detail.AbilityValues[1]);
        Assert.Equal(9, detail.Tactics.Length);
        Assert.All(detail.Tactics[0].Tactics, t => Assert.True(t.Available));
        Assert.Equal(session.View.World.Treasures.Where(t => t.Discovered && t.HolderId == 1).Select(t => t.Id), detail.Treasures);
        Assert.Equal(before, session.Snapshot.StateHash);
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
        Assert.Equal("region", query.Read(InformationCategory.Officers, 1).Rows.Single(r => r.Key.Id == 2).Cells[4].Text);
        Assert.False(query.Available(InformationCategory.Units));
        Assert.Equal(before, session.Snapshot.StateHash);
    }

    [Fact]
    public void SuccessfulCommandRefreshesResourcesCorpsAndJournalWithoutChangingInitialChronicle()
    {
        var (session, query) = Fixture();
        string initial = query.Read(InformationCategory.Chronicle).Rows[0].Cells[1].Text;
        Assert.True(session.Develop(new(5, 15, [1], DomesticActivity.Commerce)).Accepted);
        Assert.Equal(50, query.Read(InformationCategory.Forces, 2).Rows[0].Cells[1].Number);
        Assert.Equal(50, query.Read(InformationCategory.Corps, 1).Rows[0].Cells[2].Number);
        Assert.Single(query.Read(InformationCategory.Records).Rows);
        Assert.False(session.Develop(new(5, 15, [1], DomesticActivity.Commerce)).Accepted);
        Assert.Single(session.Records);
        Assert.False(session.Develop(new(5, 15, [2], DomesticActivity.Commerce)).Accepted);
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
        Assert.Equal([0, 2, 1], browser.Order(table).Select(r => r.Key.Id));
        browser.Sort(0, 1);
        Assert.Equal([1, 2, 0], browser.Order(table).Select(r => r.Key.Id));
        browser.Scroll(10, 50); browser.ShowDetail(2);
        browser.Navigate(new("force", InformationCategory.Forces, 5));
        browser.Back();
        Assert.True(browser.IsDetail); Assert.Equal(2, browser.SelectedId); Assert.Equal(10, browser.Offset); Assert.False(browser.Descending);
        browser.Back(); Assert.False(browser.IsDetail); Assert.True(browser.IsOpen);
        browser.Back(); Assert.False(browser.IsOpen);
    }
}
