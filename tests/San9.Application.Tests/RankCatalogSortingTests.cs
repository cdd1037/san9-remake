using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class RankCatalogSortingTests
{
    [Fact]
    public void OfficerOfficesSortByRulerPriorityAppointmentAndStatus()
    {
        var (session, texts, definition) = Fixture();
        var original = session.Snapshot;
        var world = original with
        {
            Officers = original.Officers.Select(o => o.Id switch
            {
                4 => o with { Status = OfficerStatus.Serving },
                5 => o with { Status = OfficerStatus.Captive, SettlementId = 1, OfficeId = 3 },
                6 => o with { Status = OfficerStatus.Free, OwnerId = null },
                _ => o
            }).ToArray()
        };
        var view = new StrategySnapshot(definition, world);
        var table = OfficerCatalog.Read(view, texts, 3, null, null);
        var state = State(table);
        int column = Array.IndexOf(table.Columns, "官职");

        state.Sort(column);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, state.Rows.Select(r => r.Id));
        Assert.Equal(new[] { "—", "A", "Z", "", "—", "—" }, state.Rows.Select(r => r.Cells[column].Text));
        state.Sort(column);
        Assert.Equal(new[] { 6, 5, 4, 3, 2, 1 }, state.Rows.Select(r => r.Id));

        var capacity = table.Rows.Single(r => r.Key.Id == 4).Cells[Array.IndexOf(table.Columns, "指挥")];
        var salary = table.Rows.Single(r => r.Key.Id == 4).Cells[Array.IndexOf(table.Columns, "俸禄")];
        Assert.Equal((CatalogCell)10000, capacity);
        Assert.Equal((CatalogCell)1, salary);
    }

    [Theory]
    [InlineData(true, 45000)]
    [InlineData(false, 20000)]
    public void RulerPersonalFieldsUseCurrentForceRankAndApplicableSortKeys(bool ranked, int capacity)
    {
        var (session, texts, definition) = Fixture();
        var world = session.Snapshot with
        {
            Forces = session.Snapshot.Forces.Select(f => f.Id == 1 ? f with { RankId = ranked ? 3 : null } : f).ToArray()
        };
        var view = new StrategySnapshot(definition, world);
        var table = OfficerCatalog.Read(view, texts, 3, 1, null);
        var ruler = table.Rows.Single(r => r.Key.Id == 1);
        var detail = OfficerDetails.Read(view, texts, 1);
        foreach (string title in new[] { "官职", "俸禄", "仕官" })
        {
            int column = Array.IndexOf(table.Columns, title);
            Assert.Equal(new CatalogCell("—", long.MaxValue), ruler.Cells[column]);
            Assert.Equal("—", detail.Fields.SelectMany(f => f).Single(f => f.Label == title).Value);
            var state = State(table);
            state.Sort(column);
            Assert.Equal(1, state.Rows[0].Id);
            state.Sort(column);
            Assert.True(Array.FindIndex(state.Rows, r => r.Id == 1) > Array.FindIndex(state.Rows, r => r.Id == 2));
        }
        Assert.Equal((CatalogCell)capacity, ruler.Cells[Array.IndexOf(table.Columns, "指挥")]);
        Assert.Equal(capacity.ToString(), detail.Fields.SelectMany(f => f).Single(f => f.Label == "指挥").Value);
    }

    [Theory]
    [InlineData(InformationCategory.Offices, 0, "官职", 3, 900)]
    [InlineData(InformationCategory.Offices, 1, "官职", 3, 900)]
    [InlineData(InformationCategory.Ranks, 0, "官爵", 3, 900)]
    [InlineData(InformationCategory.Forces, 0, "官爵", 1, 2)]
    public void RankColumnsSortByContentPrecedenceInsteadOfNameOrIdentity(
        InformationCategory category, int page, string title, int higher, int lower)
    {
        var (session, texts, _) = Fixture();
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

    private static (GameSession Session, TextCatalog Texts, ScenarioInitialState Definition) Fixture()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            Information = baseline.Information with
            {
                Offices = [new(900, "Z", 10000, 0) { GovernorPriority = 0 },
                    new(3, "A", 10000, 0) { GovernorPriority = int.MaxValue }],
                Ranks = [new(900, "Z", 10000, 0, 0, 0), new(3, "A", 45000, 0, 0, 20)]
            },
            Officers = baseline.Officers.Select(o => o with
            {
                State = o.State with
                {
                    Status = o.State.Id is 1 or 4 ? OfficerStatus.Ruler : OfficerStatus.Serving,
                    OfficeId = o.State.Id == 2 ? 3 : o.State.Id == 3 ? 900 : null,
                    StatusStartYear = 180
                }
            }).ToArray(),
            Forces = baseline.Forces.Select(f => f with { RankId = f.Id == 1 ? 3 : 900 }).ToArray()
        };
        var entries = definition.Officers.Select(o => o.NameKey)
            .Concat(definition.Settlements.Select(s => s.NameKey))
            .Concat(definition.Forces.Select(f => f.NameKey)).Distinct().ToDictionary(k => k, k => k);
        return (GameSession.Start(definition, 190), new TextCatalog(TextCatalog.CurrentSchemaVersion, entries), definition);
    }
}
