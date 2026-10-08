using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

/// <summary>Personnel selection pages project shared catalog facts over the draft's fixed candidate set.</summary>
public static class PersonnelCatalog
{
    private static CatalogColumn OfficerColumn(string field) => OfficerCatalog.Pages.SelectMany(p => p.Columns).First(c => c.Id == field);
    private static CatalogColumn RegionColumn(string field) => AdministrativeCatalog.Pages(InformationCategory.Regions).SelectMany(p => p.Columns).First(c => c.Id == field);
    private static readonly CatalogColumn Arrival = new("estimate", "到达预定", 3, CatalogAlignment.Right);
    private static IEnumerable<int> OrderedOfficers(PersonnelDraft draft, bool targets)
    {
        var people = draft.View.World.Officers.ToDictionary(p => p.Id);
        var officers = draft.View.Officers.ToDictionary(p => p.Id);
        var places = draft.View.World.Settlements.ToDictionary(p => p.Id);
        var offices = draft.View.Information.Offices.ToDictionary(o => o.Id);
        var ids = (targets ? draft.Targets : draft.Candidates).OrderBy(id => id);
        int LocationKind(int id) => people[id].SettlementId is int place && places.TryGetValue(place, out var settlement)
            ? settlement.Kind == SettlementKind.City ? 0 : 1 : 2;
        int OfficePriority(int id) => people[id].OfficeId is int office && offices.TryGetValue(office, out var definition) ? definition.GovernorPriority : 0;
        int AbilityTotal(int id)
        {
            var abilities = officers[id].Abilities;
            return abilities.Leadership + abilities.War + abilities.Intelligence + abilities.Politics;
        }
        if (targets) return ids.OrderBy(id => people[id].Status switch { OfficerStatus.Captive => 0, OfficerStatus.Free => 1, _ => 2 })
            .ThenBy(id => people[id].OwnerId is null ? -1 : Math.Min(100, people[id].Loyalty));
        return draft.Kind switch
        {
            "summon" => ids.OrderBy(id => officers[id].CorpsId ?? int.MaxValue).ThenBy(LocationKind)
                .ThenBy(id => people[id].SettlementId ?? int.MaxValue),
            "move" => ids.OrderBy(id => people[id].Status).ThenByDescending(OfficePriority).ThenByDescending(AbilityTotal),
            _ => ids.OrderByDescending(id => officers[id].Abilities.Politics)
        };
    }
    public static IReadOnlyList<CatalogPage> OfficerPages(PersonnelDraft draft, bool targets = false)
    {
        string[] fields = targets ? ["status", "loyalty", "force", "location", "leadership", "war", "intelligence", "politics", "estimate"]
            : draft.Kind switch
            {
                "summon" => ["corps", "location", "status", "leadership", "war", "intelligence", "politics", "estimate"],
                "move" => ["status", "office", "capacity", "leadership", "war", "intelligence", "politics", "learned"],
                _ => ["politics", "status", "leadership", "war", "intelligence", "learned"]
            };
        return [new(draft.Title, [OfficerColumn("name"), .. fields.Select(f => f == "estimate" ? Arrival : OfficerColumn(f))]), .. OfficerCatalog.Pages];
    }
    public static CatalogRow[] Officers(PersonnelDraft draft, TextCatalog texts, int page, bool targets = false)
    {
        var ids = OrderedOfficers(draft, targets);
        if (page > 0)
        {
            var rows = OfficerCatalog.Read(draft.View, texts, page - 1, null, null).Rows.ToDictionary(r => r.Key.Id);
            return ids.Select(id => new CatalogRow(id, rows[id].Cells.ToArray())).ToArray();
        }
        var columns = OfficerPages(draft, targets)[0].Columns;
        var fields = columns.Where(c => c.Id != "estimate").ToDictionary(c => c.Id,
            c => OfficerCatalog.Pages.Select((catalog, index) => (Page: index, Column: Array.FindIndex(catalog.Columns, field => field.Id == c.Id)))
                .First(location => location.Column >= 0));
        var facts = fields.Values.Select(location => location.Page).Distinct().ToDictionary(pageIndex => pageIndex,
            pageIndex => OfficerCatalog.Read(draft.View, texts, pageIndex, null, null).Rows.ToDictionary(r => r.Key.Id));
        CatalogCell Cell(int id, string field)
        {
            if (field == "estimate")
            {
                int? days = targets ? draft.ArrivalDaysTo(id) : draft.ArrivalDays(id);
                return days is int value ? value : CatalogCell.Unavailable;
            }
            var location = fields[field];
            return facts[location.Page][id].Cells[location.Column];
        }
        return ids.Select(id => new CatalogRow(id, columns.Select(c => Cell(id,c.Id)).ToArray())).ToArray();
    }
    public static IReadOnlyList<CatalogPage> TargetPages(PersonnelDraft draft)
    {
        bool search = draft.Kind == "search";
        string[] primary = search ? ["estimate", "province", "city", "force", "facility", "free"]
            : ["corps-name", "commander", "soldiers", "serving", "estimate"];
        string[] affiliation = search ? ["province", "city", "force", "corps-name", "commander", "serving", "captives", "free"]
            : ["city", "force", "corps-name", "commander", "serving", "captives", "free"];
        string[] strength = search ? ["soldiers", "wounded", "morale", "state", "facility", "durability", "max-durability", "recovery", "tactics", "trait"]
            : ["soldiers", "wounded", "morale", "state", "facility", "durability", "max-durability", "trait"];
        CatalogPage Page(string title, IEnumerable<string> fields) => new(title,
            [new("name",search ? "地域" : "设施",6,CatalogAlignment.Left), .. fields.Select(f => f == "estimate" ? Arrival : RegionColumn(f))]);
        return search ? [Page(draft.Title,primary),Page("所属",affiliation),Page("战力",strength),Page("邻接",["frontline","adjacent"])]
            : [Page(draft.Title,primary),Page("所属",affiliation),Page("战力",strength)];
    }
    public static CatalogRow[] Targets(PersonnelDraft draft, TextCatalog texts, int page)
    {
        var catalog = new AdministrativeCatalog(draft.View,texts);
        var columns = TargetPages(draft)[page].Columns;
        CatalogCell Cell(int id, string field)
        {
            if (field == "name") return draft.PlaceName(texts,id);
            if (field == "estimate") return draft.ArrivalDaysTo(id) is int days ? days : CatalogCell.Unavailable;
            var cell = catalog.Cell(InformationCategory.Regions,id,field);
            return field == "facility" && cell.IsUnavailable ? "" : cell;
        }
        IEnumerable<int> ids = draft.Kind == "search"
            ? draft.Targets.OrderBy(id => draft.DistanceDaysTo(id) ?? int.MaxValue).ThenBy(id => id)
            : draft.Targets.OrderBy(id => draft.View.Settlements.Single(s => s.Id == id).CorpsId ?? int.MaxValue)
                .ThenBy(id => draft.View.Settlements.Single(s => s.Id == id).Kind == SettlementKind.City ? 0 : 1).ThenBy(id => id);
        return ids.Select(id => new CatalogRow(id,columns.Select(c => Cell(id,c.Id)).ToArray())).ToArray();
    }
}
