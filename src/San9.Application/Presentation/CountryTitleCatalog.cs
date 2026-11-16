using San9.Content.Text;

namespace San9.Application.Presentation;

public static class CountryTitleCatalog
{
    public static IReadOnlyList<CatalogPage> Pages { get; } = Array.AsReadOnly(new[]
    {
        new CatalogPage("说明", [new("name", "国号", 4, CatalogAlignment.Left),
            new("description", "说明", 28, CatalogAlignment.Left)])
    });

    public static CatalogRow[] Read(StrategySnapshot view, TextCatalog texts, IEnumerable<int> candidates) =>
        candidates.Select(id =>
        {
            var title = view.Information.CountryTitles.Single(t => t.Id == id);
            return new CatalogRow(id, [new(texts.Resolve(title.NameKey)), new(title.Description)]);
        }).ToArray();
}
