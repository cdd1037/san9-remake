using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Application.Tests;

public sealed class RankCatalogSortingTests
{
    [Fact]
    public void OfficerOfficesSortByPriorityWithUnappointedBelowEveryOffice()
    {
        var (session, texts) = Fixture();
        var table = OfficerCatalog.Read(session.View, texts, 3, 1, null);
        var state = State(table);
        int column = Array.IndexOf(table.Columns, "爵位");

        state.Sort(column);
        Assert.Equal(new[] { 2, 3, 1 }, state.Rows.Select(r => r.Id));
        Assert.Equal(new[] { "A", "Z", "无" }, state.Rows.Select(r => r.Cells[column].Text));
        state.Sort(column);
        Assert.Equal(new[] { 1, 3, 2 }, state.Rows.Select(r => r.Id));
    }

    [Theory]
    [InlineData(InformationCategory.Offices, 0, "爵位", 3, 900)]
    [InlineData(InformationCategory.Offices, 1, "爵位", 3, 900)]
    [InlineData(InformationCategory.Ranks, 0, "官爵", 3, 900)]
    [InlineData(InformationCategory.Forces, 0, "官爵", 1, 2)]
    public void RankColumnsSortByContentPrecedenceInsteadOfNameOrIdentity(
        InformationCategory category, int page, string title, int higher, int lower)
    {
        var (session, texts) = Fixture();
        var table = new InformationQuery(session, texts).Read(category, page, force: category == InformationCategory.Offices ? 1 : null);
        var state = State(table);
        int column = Array.IndexOf(table.Columns, title);

        state.Sort(column);
        Assert.Equal(new[] { higher, lower }, state.Rows.Select(r => r.Id));
        Assert.Equal(new[] { "A", "Z" }, state.Rows.Select(r => r.Cells[column].Text));
        state.Sort(column);
        Assert.Equal(new[] { lower, higher }, state.Rows.Select(r => r.Id));
    }

    private static CatalogTableState State(InformationTable table)
    {
        var state = new CatalogTableState();
        state.SetRows(table.Rows.Select(r => new CatalogRow(r.Key.Id, r.Cells)).ToArray());
        return state;
    }

    private static (GameSession Session, TextCatalog Texts) Fixture()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            Information = baseline.Information with
            {
                Offices = [new(900, "low", 10000, 0) { GovernorPriority = 0 },
                    new(3, "high", 10000, 0) { GovernorPriority = 20 }],
                Ranks = [new(900, "low", 10000, 0, 0, 0), new(3, "high", 10000, 0, 0, 20)]
            },
            Officers = baseline.Officers.Select(o => o with
            {
                State = o.State with { OfficeId = o.State.Id == 2 ? 3 : o.State.Id == 3 ? 900 : null }
            }).ToArray(),
            Forces = baseline.Forces.Select(f => f with { RankId = f.Id == 1 ? 3 : 900 }).ToArray()
        };
        var entries = definition.Officers.Select(o => o.NameKey)
            .Concat(definition.Settlements.Select(s => s.NameKey))
            .Concat(definition.Forces.Select(f => f.NameKey)).Distinct().ToDictionary(k => k, k => k);
        entries.Add("high", "A");
        entries.Add("low", "Z");
        return (GameSession.Start(definition, 190), new TextCatalog(TextCatalog.CurrentSchemaVersion, entries));
    }
}
