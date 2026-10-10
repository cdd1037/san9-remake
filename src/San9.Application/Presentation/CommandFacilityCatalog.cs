using San9.Content.Text;

namespace San9.Application.Presentation;

/// <summary>Command facility pages share facts, candidate identity and one default ordering.</summary>
public sealed class CommandFacilityCatalog
{
    public IReadOnlyList<CatalogPage> Pages { get; }
    private readonly CatalogRow[][] rows;
    public CatalogRow[] Read(int page) => rows[page];

    public CommandFacilityCatalog(StrategySnapshot view, TextCatalog texts, string kind, IEnumerable<int> candidates,
        Func<int, long?>? arrival = null)
    {
        var catalog = new AdministrativeCatalog(view,texts);
        CatalogColumn Column(string field) => field switch
        {
            "commander-leadership" => new(field,CommandText.Resolve(texts, "label.commander-leadership"),4,CatalogAlignment.Right),
            "estimate" => new(field,CommandText.Resolve(texts, "label.arrival"),3,CatalogAlignment.Right),
            "relation" => AdministrativeCatalog.Pages(InformationCategory.Forces).SelectMany(p => p.Columns).First(c => c.Id == field),
            "durability" or "serving" when kind == "delegation" => AdministrativeCatalog.Pages(InformationCategory.Regions).SelectMany(p => p.Columns).First(c => c.Id == field) with { PreferredHalfCells = 6 },
            _ => AdministrativeCatalog.Pages(InformationCategory.Regions).SelectMany(p => p.Columns).First(c => c.Id == field)
        };
        CatalogPage Page(string title, string[] fields) => new(title,
            [new("name",kind == "delegation" ? CommandText.Resolve(texts, "label.facility") : CommandText.Resolve(texts, "label.work"),10,CatalogAlignment.Left), .. fields.Select(Column)]);
        string[] primary = kind switch
        {
            "burn" => ["force","commander","commander-leadership","durability","state","estimate"],
            "seize" => ["force","commander","commander-leadership","soldiers","captives","state","estimate"],
            "request" or "delegation" => ["force","relation","commander","soldiers","durability","serving"],
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Pages = [Page(kind switch { "burn" => CommandText.Resolve(texts, "title.burn"), "seize" => CommandText.Resolve(texts, "title.seize"), "request" => CommandText.Resolve(texts, "title.request"), _ => CommandText.Resolve(texts, "title.delegate") },primary),
            Page(CommandText.Resolve(texts, "page.affiliation"),["city","force","corps-name","commander","serving","captives","free"]),
            Page(CommandText.Resolve(texts, "page.strength"),["soldiers","wounded","morale","state","facility","durability","max-durability","trait"])];
        var places = view.Settlements.ToDictionary(p => p.Id);
        CatalogCell Cell(int id, string field)
        {
            if (field == "name") return places[id].ResolveName(texts);
            if (field == "commander-leadership") return catalog.Commander(id) is {} commander ? commander.Abilities.Leadership : CatalogCell.Unavailable;
            if (field == "estimate") return arrival?.Invoke(id) is long days ? days : CatalogCell.Unavailable;
            if (field == "relation") return places[id].OwnerId is int owner && owner != view.PlayerForceId ?
                catalog.Cell(InformationCategory.Forces,owner,"relation") : CatalogCell.Unavailable;
            if (field == "state") return view.World.Settlements.Single(s => s.Id == id).ConfusionDaysRemaining > 0 ? CommandText.Resolve(texts, "state.confused") : CommandText.Resolve(texts, "state.normal");
            return catalog.Cell(InformationCategory.Regions,id,field);
        }
        int Leadership(int id) => catalog.Commander(id)?.Abilities.Leadership ?? 0;
        long Relation(int id) => Cell(id,"relation").Number ?? 0;
        var ids = kind switch
        {
            "burn" => candidates.OrderBy(Leadership).ThenBy(id => id).ToArray(),
            "seize" => candidates.OrderBy(Leadership).ThenBy(id => places[id].Soldiers).ThenBy(id => id).ToArray(),
            _ => candidates.OrderBy(Relation).ThenBy(id => places[id].OwnerId).ThenBy(id => id).ToArray()
        };
        var facts = ids.ToDictionary(id => id,id => Pages.SelectMany(p => p.Columns).Select(c => c.Id).Distinct()
            .ToDictionary(field => field,field => Cell(id,field)));
        rows = Pages.Select(page => ids.Select(id => new CatalogRow(id,
            page.Columns.Select(column => facts[id][column.Id]).ToArray())).ToArray()).ToArray();
    }
}
