using San9.Content.Text;

namespace San9.Application.Presentation;

public static class UnitMapInteraction
{
    public static IReadOnlyList<MapContextEntry> Menu(StrategySnapshot view, int id)
    {
        var unit = view.World.Units.FirstOrDefault(u => u.Id == id && UnitCatalog.IsListed(u) && u.FormationId is not null && u.Soldiers > 0);
        if (unit is null) return [];
        List<MapContextEntry> entries = [
            new("部队情报", new("", InformationCategory.Units, UnitId: id), id),
            new("部队武将", new("", InformationCategory.Officers, UnitId: id),
                Enabled: view.Officers.Any(o => o.UnitId == id))];
        SettlementContextMenu.AddOwnerEntries(view, entries, unit.OwnerId, unit.CorpsId);
        return entries;
    }

    public static MapHoverInfo? Hover(StrategySnapshot view, TextCatalog texts, int id)
    {
        var unit = view.World.Units.FirstOrDefault(u => u.Id == id && UnitCatalog.IsListed(u) && u.FormationId is not null && u.Soldiers > 0);
        if (unit is null) return null;
        var force = view.Forces.Single(f => f.Id == unit.OwnerId);
        var corps = view.World.Corps.FirstOrDefault(c => c.Id == unit.CorpsId);
        string Person(int? person) => view.Officers.FirstOrDefault(o => o.Id == person) is {} officer ? texts.Resolve(officer.NameKey) : "----";
        var formation = view.Information.Formations.FirstOrDefault(f => f.Id == view.FormationId(unit));
        int relation = view.World.Relations.FirstOrDefault(r =>
            r.FirstId == view.PlayerForceId && r.SecondId == unit.OwnerId || r.SecondId == view.PlayerForceId && r.FirstId == unit.OwnerId)?.Value ?? 0;
        bool own = unit.OwnerId == view.PlayerForceId;
        return new(id, (unit.CommanderId is null ? texts.Resolve(force.NameKey) : Person(unit.CommanderId)) + "队",
            MapHoverInfo.ThemeFor(unit.OwnerId, view.PlayerForceId, relation), force, [],
            [new("状态", UnitCatalog.Condition(unit.Condition)),
             new(own ? "军团" : "势力", own ? Person(corps?.LeaderId) : texts.Resolve(force.NameKey)),
             new("所属", view.Settlements.FirstOrDefault(s => s.Id == unit.ServicePlaceId)?.ResolveName(texts) ?? "无"),
             new("士兵", unit.Soldiers.ToString(), true), new("士气", $"{unit.Morale}/100", true),
             new(view.MilitaryRules?.Formation(unit).TravelMode == San9.Core.Domain.TravelMode.Water ? "船" : "阵形", formation is null ? "----" : texts.Resolve(formation.NameKey)),
             new("在任", view.Officers.Count(o => o.UnitId == id && o.IsServing && o.OwnerId == unit.OwnerId).ToString(), true)], 240)
        { FlagGlyph = view.Officers.FirstOrDefault(o => o.Id == unit.CommanderId)?.FlagGlyph ?? -1,
          IsRulerCommander = unit.CommanderId is int leader && force.LeaderId == leader };
    }
}
