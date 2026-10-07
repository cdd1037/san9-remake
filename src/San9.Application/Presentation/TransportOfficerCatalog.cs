using San9.Content.Text;

namespace San9.Application.Presentation;

/// <summary>Transport candidates retain command priority across the shared officer pages.</summary>
public static class TransportOfficerCatalog
{
    public static IReadOnlyList<CatalogPage> Pages { get; } = Array.AsReadOnly<CatalogPage>([
        new("输送", [
            new("name", "武将", 8, CatalogAlignment.Left),
            new("capacity", "指挥", 5, CatalogAlignment.Right),
            new("leadership", "统率", 3, CatalogAlignment.Right),
            new("war", "武力", 3, CatalogAlignment.Right),
            new("intelligence", "智力", 3, CatalogAlignment.Right),
            new("politics", "政治", 3, CatalogAlignment.Right),
            new("learned", "学得", 2, CatalogAlignment.Right),
            new("personality", "性格", 4, CatalogAlignment.Center)]),
        .. OfficerCatalog.Pages]);

    public static CatalogRow[] Read(SpecialDeploymentDraft draft, TextCatalog texts, int page)
    {
        if ((uint)page >= Pages.Count) throw new ArgumentOutOfRangeException(nameof(page));
        var common = OfficerCatalog.Read(draft.View, texts, page == 0 ? 0 : page-1, null, draft.Home.Id)
            .Rows.ToDictionary(row => row.Key.Id);
        if (page != 0)
            return draft.Candidates.Select(id => new CatalogRow(id, common[id].Cells.ToArray())).ToArray();
        var personal = OfficerCatalog.Read(draft.View, texts, 3, null, draft.Home.Id)
            .Rows.ToDictionary(row => row.Key.Id);
        int personality = Array.FindIndex(OfficerCatalog.Pages[3].Columns, c => c.Id == "personality");
        return draft.Candidates.Select(id =>
        {
            var cells = common[id].Cells;
            return new CatalogRow(id, [cells[0], (long)draft.CommandCapacity(id), cells[1], cells[2],
                cells[3], cells[4], cells[5], personal[id].Cells[personality]]);
        }).ToArray();
    }
}
