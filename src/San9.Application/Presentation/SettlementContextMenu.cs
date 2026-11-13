using San9.Core.Domain;

namespace San9.Application.Presentation;

public sealed record MapContextEntry(string Title, InformationLink Target, int? DetailId = null, bool Enabled = true);

/// <summary>Read-only actions for the object under the pointer, independent of command selection.</summary>
public static class SettlementContextMenu
{
    public static IReadOnlyList<MapContextEntry> Read(StrategySnapshot view, int settlementId)
    {
        var place = view.World.Settlements.FirstOrDefault(s => s.Id == settlementId && !s.IsDormant && !s.IsDestroyed);
        if (place is null || !view.Settlements.First(s => s.Id == settlementId).HasMapContextMenu) return [];
        bool city = place.Kind == SettlementKind.City;
        string prefix = city ? "都市" : "地域";
        List<MapContextEntry> entries = [new(prefix + "情报",
            city ? new("", InformationCategory.Cities, SettlementId: place.Id) : new("", InformationCategory.Regions, RegionId: place.Id), place.Id)];
        if (!city && !place.CanHostPersons) return entries;
        bool hasPeople = view.Officers.Any(o => o.ServicePlaceId == place.Id);
        entries.Add(new(prefix + "武将", new("", InformationCategory.Officers, RegionId: place.Id), Enabled: hasPeople));
        if (place.OwnerId is not int owner || view.Forces.FirstOrDefault(f => f.Id == owner) is not { } force) return entries;
        AddOwnerEntries(view, entries, owner, place.CorpsId);
        return entries;
    }

    internal static void AddOwnerEntries(StrategySnapshot view, List<MapContextEntry> entries, int owner, int? corpsId)
    {
        var force = view.Forces.Single(f => f.Id == owner);
        if (corpsId is int corps && (force.IsBarbarian || view.World.Corps.Count(c => c.OwnerId == owner) > 1))
        {
            entries.Add(new("军团情报", new("", InformationCategory.Corps, CorpsId: corps), corps));
            entries.Add(new("军团武将", new("", InformationCategory.Officers, CorpsId: corps)));
        }
        entries.Add(new("势力情报", new("", InformationCategory.Forces, ForceId: owner), owner));
        entries.Add(new("势力武将", new("", InformationCategory.Officers, ForceId: owner)));
        if (!force.IsBarbarian) entries.Add(new("势力官职", new("", InformationCategory.Offices, ForceId: owner)));
        entries.Add(new("势力兵法", new("", InformationCategory.Tactics, ForceId: owner)));
        entries.Add(new("势力阵形", new("", InformationCategory.Formations, ForceId: owner)));
        entries.Add(new("势力船", new("", InformationCategory.Ships, ForceId: owner)));
    }
}
