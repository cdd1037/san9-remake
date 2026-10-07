using San9.Content.Text;

namespace San9.Application.Presentation;

/// <summary>Projects the revision-bound candidates while preserving facility/unit identity.</summary>
public sealed class MilitaryTargetCatalog
{
    public static IReadOnlyList<CatalogPage> Pages { get; } = [new("所属", [
        new("name", "对象", 10, CatalogAlignment.Center),
        new("force", "势力", 8, CatalogAlignment.Center),
        new("soldiers", "士兵", 6, CatalogAlignment.Right),
        new("wounded", "伤兵", 6, CatalogAlignment.Right),
        new("morale", "士气", 3, CatalogAlignment.Right),
        new("condition", "状态", 4, CatalogAlignment.Center)])];

    private readonly MilitaryTarget[] _targets;
    public IReadOnlyList<CatalogPage> DisplayPages { get; }
    public CatalogRow[] Rows { get; }
    public MilitaryTarget Target(int rowId) => _targets[rowId];

    public MilitaryTargetCatalog(MilitaryTargets selection, TextCatalog texts)
    {
        var view = selection.View;
        DisplayPages = Pages;
        if (selection is SpecialDeploymentTargets { IsConstruction: true } construction)
        {
            DisplayPages = [new("建设地点", [new("region","地域",12,CatalogAlignment.Center),
                new("city","所属都市",12,CatalogAlignment.Center),new("location","位置",12,CatalogAlignment.Center)])];
            // One representative per region keeps the catalog usable; all exact sites remain selectable on the map.
            _targets = construction.Candidates.Keys.GroupBy(t=>t.Id).OrderBy(g=>g.Key).Select(g=>g.First()).ToArray();
            Rows = _targets.Select((t,index)=>new CatalogRow(index,[texts.Resolve(view.RegionNameKeys[t.Id]),
                view.Settlements.Single(s=>s.Id==view.RegionCityIds[t.Id]).ResolveName(texts),
                $"{San9.Core.Domain.MapCoordinates.Cell(t.Position!.Value).X}, {San9.Core.Domain.MapCoordinates.Cell(t.Position.Value).Y}"])).ToArray();
            return;
        }
        var forces = view.Forces.ToDictionary(f => f.Id, f => texts.Resolve(f.NameKey));
        var people = view.Officers.ToDictionary(o => o.Id, o => texts.Resolve(o.NameKey));
        var places = view.Settlements.ToDictionary(s => s.Id);
        var states = view.World.Settlements.ToDictionary(s => s.Id);
        var units = view.World.Units.ToDictionary(u => u.Id);
        string Force(int? owner) => owner is int id ? forces.GetValueOrDefault(id, "") : "";
        _targets = selection.Candidates.Keys.OrderBy(t => t.IsUnit).ThenBy(t => t.Id).ToArray();
        Rows = _targets.Select((target, index) =>
        {
            CatalogCell[] cells;
            if (target.IsUnit)
            {
                var unit = units[target.Id];
                string name = (unit.CommanderId is int commander ? people[commander] : Force(unit.OwnerId)) + "队";
                cells = [name, Force(unit.OwnerId), (long)unit.Soldiers, (long)unit.Wounded,
                    unit.Morale, UnitCatalog.Condition(unit.Condition)];
            }
            else
            {
                var place = states[target.Id];
                cells = [places[target.Id].ResolveName(texts), Force(place.OwnerId), (long)place.Soldiers,
                    (long)place.Wounded, place.Morale, place.ConfusionDaysRemaining > 0 ? "混乱" : "一般"];
            }
            return new CatalogRow(index, cells);
        }).ToArray();
    }
}
