using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

/// <summary>Editor catalogs are distinct from the three-page geographic information catalog.</summary>
public static class WorldEditorCatalog
{
    private static CatalogColumn C(string id, string title, int cells = 6, CatalogAlignment alignment = CatalogAlignment.Center) =>
        new(id, title, cells, alignment);
    private static CatalogColumn Name(string title) => C("name", title, 10, CatalogAlignment.Left);
    public static IReadOnlyList<CatalogPage> Facilities { get; } =
    [
        new("所属", [Name("工作物"), C("city", "都市"), C("force", "势力", 8), C("corps-name", "军团", 8),
            C("commander", "大将", 8), C("serving", "在任"), C("captives", "俘虏"), C("free", "在野")]),
        new("战力", [Name("工作物"), C("soldiers", "士兵"), C("wounded", "伤兵"), C("morale", "士气"), C("state", "状态"),
            C("facility", "工作物", 8), C("durability", "耐久"), C("max-durability", "最大耐久"), C("trait", "特性")])
    ];
    public static IReadOnlyList<CatalogPage> Obstacles { get; } =
    [new("耐久", [Name("障碍物"), C("city", "都市"), C("durability", "耐久"), C("max-durability", "最大耐久")])];

    public static CatalogRow[] Read(StrategySnapshot view, TextCatalog texts, WorldEditorKind kind, int page, IReadOnlySet<int> candidates)
    {
        var pages = kind == WorldEditorKind.Facilities ? Facilities : Obstacles;
        if ((uint)page >= pages.Count) throw new ArgumentOutOfRangeException(nameof(page));
        var catalog = new AdministrativeCatalog(view, texts);
        return view.Settlements.Where(s => candidates.Contains(s.Id)).OrderBy(s => s.Id).Select(s => new CatalogRow(s.Id,
            pages[page].Columns.Select(c => c.Id == "name" ? new CatalogCell(s.ResolveName(texts)) :
                catalog.Cell(InformationCategory.Regions, s.Id, c.Id)).ToArray())).ToArray();
    }
}
