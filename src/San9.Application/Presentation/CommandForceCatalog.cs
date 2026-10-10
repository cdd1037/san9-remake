using San9.Content.Text;

namespace San9.Application.Presentation;

/// <summary>Delegation target list adds its command page to the public force catalog.</summary>
public sealed class CommandForceCatalog
{
    public IReadOnlyList<CatalogPage> Pages { get; }
    private readonly CatalogRow[][] rows;
    public CatalogRow[] Read(int page) => rows[page];

    public CommandForceCatalog(StrategySnapshot view, TextCatalog texts, IEnumerable<int> candidates)
    {
        var shared = AdministrativeCatalog.Pages(InformationCategory.Forces);
        CatalogColumn Column(string field) => shared.SelectMany(p => p.Columns).First(c => c.Id == field);
        CatalogColumn Count(string field) => Column(field) with { PreferredHalfCells = 6 };
        Pages = [new("委任", [Column("name") with { PreferredHalfCells = 8 }, Column("relation"), Column("alliance"),
            Count("trust"), Count("cities"), Column("soldiers"), Count("officers") with { Title = "在任" }]), .. shared];
        var catalog = new AdministrativeCatalog(view,texts);
        var fields = Pages.SelectMany(p => p.Columns).Select(c => c.Id).Distinct().ToArray();
        var facts = candidates.Distinct().ToDictionary(id => id,
            id => fields.ToDictionary(field => field,field => catalog.Cell(InformationCategory.Forces,id,field)));
        var ids = facts.Keys.OrderBy(id => facts[id]["relation"].Number).ThenBy(id => id).ToArray();
        rows = Pages.Select(page => ids.Select(id => new CatalogRow(id,
            page.Columns.Select(column => facts[id][column.Id]).ToArray())).ToArray()).ToArray();
    }
}
