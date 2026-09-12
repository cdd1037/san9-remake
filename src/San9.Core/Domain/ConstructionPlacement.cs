namespace San9.Core.Domain;

/// <summary>Location eligibility only; workforce, technology, treasury and command authorization are separate checks.</summary>
public static class ConstructionPlacement
{
    public static bool IsAvailable(int owner, ConstructionSite site, FacilityRule facility, MilitaryRulebook rules,
        IReadOnlyDictionary<int, int> regionCities, IEnumerable<SettlementState> settlements, IEnumerable<UnitState> units)
    {
        if (!facility.Buildable || !site.FacilityIds.Contains(facility.Id) ||
            !rules.ConstructionSites.TryGetValue((site.RegionId, site.Position), out var known) || !known.FacilityIds.Contains(facility.Id) ||
            !regionCities.TryGetValue(site.RegionId, out int cityId)) return false;
        var places = settlements.ToArray();
        if (!places.Any(s => s.Id == cityId && s.Kind == SettlementKind.City && s.OwnerId == owner && !s.IsDestroyed && s.Durability > 0) ||
            places.Any(s => s.Id == site.RegionId && !s.IsDestroyed)) return false;
        var area = new MapOccupancy(site.Position, facility.Footprint);
        if (places.Where(s => !s.IsDestroyed).Any(s => rules.TryPlace(s, out var p) &&
            area.Overlaps(new(p.Position, p.Footprint)))) return false;
        return !units.Any(u => u.FormationId is int id && rules.Formations.TryGetValue(id, out var f) &&
            area.Overlaps(new(u.Position, f.Footprint)));
    }
}
