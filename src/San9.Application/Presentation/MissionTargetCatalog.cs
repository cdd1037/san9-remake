using San9.Content.Text;

namespace San9.Application.Presentation;

/// <summary>Adds the current strategy recommendation to existing target facts and ordering.</summary>
public sealed class MissionTargetCatalog
{
    private readonly MissionDraft draft;
    private readonly bool secondary;
    private readonly Func<int, CatalogRow[]> readRows;
    public IReadOnlyList<CatalogPage> Pages { get; }

    public MissionTargetCatalog(MissionDraft draft, TextCatalog texts, IReadOnlyList<CatalogPage> pages,
        Func<int, CatalogRow[]> readRows, bool secondary = false)
    {
        this.draft = draft;
        this.secondary = secondary;
        this.readRows = readRows;
        Pages = pages.Select((page, index) => index == 0 && draft.HasRecommendations
            ? page with { Columns = [new("advice", CommandText.Resolve(texts, "label.advice"), 4, CatalogAlignment.Center), .. page.Columns] }
            : page).ToArray();
    }

    public CatalogRow[] Read(int page) => readRows(page)
        .OrderByDescending(row => MissionDraft.AdvicePriority(draft.TargetOpinion(row.Id, secondary)))
        .Select(row => page == 0 && draft.HasRecommendations
            ? new CatalogRow(row.Id, [new(MissionDraft.AdviceSymbol(draft.TargetOpinion(row.Id, secondary)),
                MissionDraft.AdvicePriority(draft.TargetOpinion(row.Id, secondary))), .. row.Cells]) : row).ToArray();
}
