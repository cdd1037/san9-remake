using San9.Content.Text;

namespace San9.Application.Presentation;

/// <summary>Command-specific first page and candidate priority, followed by shared officer facts.</summary>
public static class MissionOfficerCatalog
{
    private static CatalogColumn Column(string id) => id switch
    {
        "estimate" => new(id,"到达预定",2,CatalogAlignment.Right),
        "inspiration" => new(id,"鼓舞",2,CatalogAlignment.Center),
        _ => OfficerCatalog.Pages.SelectMany(p => p.Columns).First(c => c.Id == id)
    };

    public static IReadOnlyList<CatalogPage> Pages(MissionDraft draft, bool targets = false)
    {
        string[] fields = targets ? draft.Kind == "alienate"
            ? ["intelligence","loyalty","force","location","status","leadership","war","politics","estimate"]
            : ["leadership","war","intelligence","politics","learned","loyalty","home","location"]
            : draft.ActorPriorityField switch
            {
                "war" => ["war","status","leadership","intelligence","politics","learned"],
                "leadership" => ["leadership","status","war","intelligence","politics","learned"],
                "politics" => ["politics","status","leadership","war","intelligence","learned"],
                _ => draft.Kind == "inspire" ? ["intelligence","inspiration","status","leadership","war","politics","learned"]
                    : ["intelligence","status","leadership","war","politics","learned"]
            };
        return [new(draft.Title,[Column("name"), .. fields.Select(Column)]), .. OfficerCatalog.Pages];
    }

    public static CatalogRow[] Read(MissionDraft draft, TextCatalog texts, int page, bool targets = false)
    {
        var pages = Pages(draft,targets);
        if ((uint)page >= pages.Count) throw new ArgumentOutOfRangeException(nameof(page));
        IEnumerable<int> ids = targets ? draft.Kind == "return" ? draft.Prisoners : draft.Targets : draft.Actors;
        if (targets && draft.Kind == "alienate") ids = ids.OrderBy(id => draft.View.Officers.Single(o => o.Id == id).Abilities.Intelligence).ThenBy(id => id);
        if (page > 0)
        {
            var rows = OfficerCatalog.Read(draft.View,texts,page-1,null,null).Rows.ToDictionary(r => r.Key.Id);
            return ids.Select(id => new CatalogRow(id,rows[id].Cells.ToArray())).ToArray();
        }
        var columns = pages[0].Columns;
        var fields = columns.Where(c => c.Id is not ("estimate" or "inspiration")).ToDictionary(c => c.Id,
            c => OfficerCatalog.Pages.Select((p,index) => (Page:index,Column:Array.FindIndex(p.Columns,f => f.Id == c.Id))).First(f => f.Column >= 0));
        var facts = fields.Values.Select(f => f.Page).Distinct().ToDictionary(p => p,
            p => OfficerCatalog.Read(draft.View,texts,p,null,null).Rows.ToDictionary(r => r.Key.Id));
        CatalogCell Cell(int id, string field)
        {
            if (field == "estimate") return draft.ArrivalDaysToOfficer(id) is long days ? days : CatalogCell.Unavailable;
            if (field == "inspiration") return new(draft.KnowsInspiration(id) ? "○" : "×", draft.KnowsInspiration(id) ? 1 : 0);
            var f = fields[field]; return facts[f.Page][id].Cells[f.Column];
        }
        return ids.Select(id => new CatalogRow(id,columns.Select(c => Cell(id,c.Id)).ToArray())).ToArray();
    }
}
