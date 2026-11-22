using San9.Content.Text;

namespace San9.Application.Presentation;

/// <summary>Command-specific first page and candidate priority, followed by shared officer facts.</summary>
public static class MissionOfficerCatalog
{
    private static CatalogColumn Column(string id, TextCatalog texts) => id switch
    {
        "dispatched" => DispatchCounts.Column(texts),
        "estimate" => new(id,CommandText.Resolve(texts, "label.arrival"),2,CatalogAlignment.Right),
        "inspiration" => new(id,CommandText.Resolve(texts, "label.inspiration"),2,CatalogAlignment.Center),
        "advice" => new(id,CommandText.Resolve(texts, "label.advice"),4,CatalogAlignment.Center),
        _ => OfficerCatalog.CorePages.SelectMany(p => p.Columns).First(c => c.Id == id)
    };

    public static IReadOnlyList<CatalogPage> Pages(MissionDraft draft, TextCatalog texts, bool targets = false)
    {
        string[] fields = targets ? draft.Kind == "alienate"
            ? ["intelligence","loyalty","force","location","status","leadership","war","politics","estimate","dispatched"]
            : ["leadership","war","intelligence","politics","learned","loyalty","home","location"]
            : draft.ActorPriorityField switch
            {
                "war" => ["war","status","leadership","intelligence","politics","learned"],
                "leadership" => ["leadership","status","war","intelligence","politics","learned"],
                "politics" => ["politics","status","leadership","war","intelligence","learned"],
                _ => draft.Kind == "inspire" ? ["intelligence","inspiration","status","leadership","war","politics","learned"]
                    : ["intelligence","status","leadership","war","politics","learned"]
            };
        if (!targets && draft.SupportsMultipleActors) fields = [.. fields, "estimate"];
        CatalogColumn[] prefix = draft.HasRecommendations ? [Column("advice",texts), Column("name",texts)] : [Column("name",texts)];
        return [new(CommandText.Resolve(texts, "title." + draft.Kind),[.. prefix, .. fields.Select(f => Column(f,texts))]), .. OfficerCatalog.Pages(draft.View, texts)];
    }

    public static CatalogRow[] Read(MissionDraft draft, TextCatalog texts, int page, bool targets = false)
    {
        var pages = Pages(draft,texts,targets);
        if ((uint)page >= pages.Count) throw new ArgumentOutOfRangeException(nameof(page));
        IEnumerable<int> ids = targets ? draft.Kind == "return" ? draft.Prisoners : draft.Targets : draft.Actors;
        if (targets && draft.Kind == "alienate") ids = ids.OrderBy(id => draft.View.Officers.Single(o => o.Id == id).Abilities.Intelligence).ThenBy(id => id);
        ids = targets ? ids.OrderByDescending(id => MissionDraft.AdvicePriority(draft.TargetOpinion(id))) : draft.RecommendedActors;
        if (page > 0)
        {
            var rows = OfficerCatalog.Read(draft.View,texts,page-1,null,null).Rows.ToDictionary(r => r.Key.Id);
            return ids.Select(id => new CatalogRow(id,rows[id].Cells.ToArray())).ToArray();
        }
        var columns = pages[0].Columns;
        var dispatched = targets && draft.Kind == "alienate" ? DispatchCounts.ByTarget(draft.View, "strategy-alienate") : null;
        var fields = columns.Where(c => c.Id is not ("estimate" or "inspiration" or "advice" or "dispatched")).ToDictionary(c => c.Id,
            c => OfficerCatalog.CorePages.Select((p,index) => (Page:index,Column:Array.FindIndex(p.Columns,f => f.Id == c.Id))).First(f => f.Column >= 0));
        var facts = fields.Values.Select(f => f.Page).Distinct().ToDictionary(p => p,
            p => OfficerCatalog.Read(draft.View,texts,p,null,null).Rows.ToDictionary(r => r.Key.Id));
        CatalogCell Cell(int id, string field)
        {
            if (field == "dispatched") return dispatched!.GetValueOrDefault(id);
            if (field == "advice")
            {
                var opinion = targets ? draft.TargetOpinion(id) : draft.ActorAdvice(id)?.Opinion ?? San9.Core.Simulation.AdviserOpinion.Unavailable;
                return new(MissionDraft.AdviceSymbol(opinion), MissionDraft.AdvicePriority(opinion));
            }
            if (field == "estimate") return (targets ? draft.ArrivalDaysToOfficer(id) : draft.ArrivalDaysForActor(id)) is long days
                ? days : CatalogCell.Unavailable;
            if (field == "inspiration") return new(draft.KnowsInspiration(id) ? "○" : "×", draft.KnowsInspiration(id) ? 1 : 0);
            var f = fields[field]; return facts[f.Page][id].Cells[f.Column];
        }
        return ids.Select(id => new CatalogRow(id,columns.Select(c => Cell(id,c.Id)).ToArray())).ToArray();
    }
}
