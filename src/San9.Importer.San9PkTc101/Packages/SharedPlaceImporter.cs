using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Packages;

internal static class SharedPlaceImporter
{
    internal static SharedMapReference Reference { get; } = new("shared/maps/china/map.json",
        "shared/maps/china/geography.json", "shared/maps/china/places.json");

    internal static void Collect(SharedMapSites result, ScenarioInitialState scenario, TextCatalog text)
    {
        foreach (var (key, value) in text.Entries.Where(p => p.Key.StartsWith("terrain.", StringComparison.Ordinal) ||
            p.Key.StartsWith("region.", StringComparison.Ordinal)))
            result.Labels.TryAdd(key, value);
        var military = scenario.Military!.Places.ToDictionary(p => p.Id);
        foreach (var settlement in scenario.Settlements)
        {
            var facility = military[settlement.Id];
            var (name, suffix) = SettlementInitialState.DisplayNameKeys(settlement, settlement.Id, facility.FacilityTypeId,
                scenario.RegionNameKeys, scenario.FacilityNameSuffixKeys);
            Add(settlement.Id, new(text.Resolve(name) + (suffix is null ? "" : text.Resolve(suffix)), settlement.Cell, facility.Footprint,
                facility.FacilityTypeId, settlement.Kind == SettlementKind.City));
        }
        foreach (var place in scenario.UnbuiltPlaces)
            Add(place.Id, new(text.Resolve(scenario.RegionNameKeys[place.Id]), MapCoordinates.Cell(place.Position), new(1, 1), null, false));

        void Add(int id, MapSite site)
        {
            if (result.Sites.TryGetValue(id, out var prior))
            {
                if (prior.Cell != site.Cell || prior.FacilityType.HasValue && site.FacilityType.HasValue && prior != site)
                    throw new InvalidDataException($"剧本 {scenario.ScenarioId} 的地点 {id} 与共享地图不一致。");
                if (site.FacilityType is null) return;
            }
            result.Sites[id] = site;
        }
    }
}
