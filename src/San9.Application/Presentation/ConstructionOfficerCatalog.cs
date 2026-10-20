using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

/// <summary>Construction-specific candidate facts followed by the common officer pages.</summary>
public static class ConstructionOfficerCatalog
{
    public static IReadOnlyList<CatalogPage> Pages { get; } = Array.AsReadOnly<CatalogPage>([
        new("建设", [
            new("name", "武将", 8, CatalogAlignment.Left),
            new("construction", "营造", 2, CatalogAlignment.Center),
            new("stone", "石兵", 2, CatalogAlignment.Center),
            new("capacity", "指挥", 5, CatalogAlignment.Right),
            new("leadership", "统率", 3, CatalogAlignment.Right),
            new("war", "武力", 3, CatalogAlignment.Right),
            new("intelligence", "智力", 3, CatalogAlignment.Right),
            new("politics", "政治", 3, CatalogAlignment.Right),
            new("learned", "学得", 2, CatalogAlignment.Right)]),
        .. OfficerCatalog.Pages]);

    public static CatalogRow[] Read(SpecialDeploymentDraft draft, TextCatalog texts, int page)
    {
        if ((uint)page >= Pages.Count) throw new ArgumentOutOfRangeException(nameof(page));
        var common = OfficerCatalog.Read(draft.View, texts, page == 0 ? 0 : page-1, null, draft.Home.Id)
            .Rows.ToDictionary(row => row.Key.Id);
        var people = draft.View.World.Officers.ToDictionary(o => o.Id);
        var holdings = draft.View.World.Treasures.ToLookup(t => t.HolderId);
        var treasures = draft.View.Rules.Treasures;
        var availability = draft.Candidates.ToDictionary(id => id,
            id => TacticAvailability.ForOfficer(people[id], draft.Force, holdings[id], treasures));
        TacticSource Sources(int id, TacticEffect effect) => draft.Rules.Tactics.Values.Where(t => t.Effect == effect)
            .Aggregate(TacticSource.None, (result, tactic) => result | availability[id].GetValueOrDefault(tactic.Id));
        // Original candidate priority: construction contributes two, stone formation one.
        // Equal priorities retain the incoming order; selection does not change commander rules.
        var qualifications = draft.Candidates.ToDictionary(id => id,
            id => (Construction: Sources(id, TacticEffect.Construction), Stone: Sources(id, TacticEffect.StoneFormation)));
        var candidates = draft.Candidates.OrderByDescending(id =>
            (qualifications[id].Construction != TacticSource.None ? 2 : 0) +
            (qualifications[id].Stone != TacticSource.None ? 1 : 0));
        if (page != 0)
            return candidates.Select(id => new CatalogRow(id, common[id].Cells.ToArray())).ToArray();
        return candidates.Select(id =>
        {
            var cells = common[id].Cells;
            static CatalogCell Mark(TacticSource sources)
            {
                bool learned = (sources & TacticSource.Learned) != 0;
                return new(learned ? "○" : sources != TacticSource.None ? "△" : "", learned ? 2 : sources != TacticSource.None ? 1 : 0);
            }
            return new CatalogRow(id, [cells[0], Mark(qualifications[id].Construction), Mark(qualifications[id].Stone),
                (long)draft.CommandCapacity(id), cells[1], cells[2], cells[3], cells[4], cells[5]]);
        }).ToArray();
    }
}
