using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Public ownership/topology only; never inspects foreign tasks or unit orders.</summary>
internal sealed class OriginalAiGeography
{
    internal sealed record Place(int Id, int? OwnerId, int CityId, int? ProvinceId);
    internal ImmutableArray<Place> Places { get; }
    private readonly Dictionary<int, int[]> _neighbors;
    private readonly Dictionary<int, int[]> _cityNeighbors;
    private readonly Dictionary<int, HashSet<int>> _priority = [];

    internal OriginalAiGeography(ScenarioInitialState content, MilitaryRulebook? military, WorldSnapshot world)
    {
        var places = new List<Place>();
        foreach (var s in world.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed))
        {
            int? region = military is not null && military.TryPlace(s, out var p) ? military.RegionAt(p.Position) : null;
            int? city = s.Kind == SettlementKind.City ? s.Id : region is int r && content.RegionCityIds.TryGetValue(r, out int c) ? c :
                content.RegionCityIds.TryGetValue(s.Id, out int home) ? home : null;
            if (city is int id) places.Add(new(s.Id, s.OwnerId, id, content.CityProvinceIds.TryGetValue(id, out int province) ? province : null));
        }
        foreach (var p in content.UnbuiltPlaces.Where(p => world.Settlements.All(s => s.Id != p.Id || s.IsDestroyed)))
            if (content.RegionCityIds.TryGetValue(p.Id, out int city))
                places.Add(new(p.Id, null, city, content.CityProvinceIds.TryGetValue(city, out int province) ? province : null));
        Places = places.ToImmutableArray();
        _cityNeighbors = content.CityConnections.GroupBy(c => c.FromCityId).ToDictionary(g => g.Key, g => g.Select(c => c.ToCityId).Distinct().ToArray());
        if (military is null)
            _neighbors = content.CityConnections.GroupBy(c => c.FromCityId).ToDictionary(g => g.Key, g => g.Select(c => c.ToCityId).Distinct().ToArray());
        else
        {
            var regionPlaces = places.GroupBy(p => world.Settlements.FirstOrDefault(s => s.Id == p.Id) is { } s && military.TryPlace(s, out var actual)
                    ? military.RegionAt(actual.Position) ?? p.Id : p.Id)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToArray());
            _neighbors = military.TerritoryConnections(world.Settlements)
                .SelectMany(edge => regionPlaces.GetValueOrDefault(edge.FromRegionId, [])
                    .SelectMany(from => regionPlaces.GetValueOrDefault(edge.ToRegionId, []).Select(to => (From: from, To: to))))
                .GroupBy(p => p.From).ToDictionary(g => g.Key, g => g.Select(p => p.To).Distinct().ToArray());
        }
    }

    internal IEnumerable<Place> Neighbors(int id) => _neighbors.GetValueOrDefault(id, [])
        .Select(id => Places.FirstOrDefault(p => p.Id == id)).OfType<Place>();
    internal IEnumerable<int> NearbyCities(int city) => new[] { city }.Concat(_cityNeighbors.GetValueOrDefault(city, []));
    internal bool Frontier(int place, int owner) => Neighbors(place).Any(p => p.OwnerId != owner);
    internal bool Priority(int place, int owner)
    {
        var own = Places.FirstOrDefault(p => p.Id == place);
        if (own is null) return false;
        if (!_priority.TryGetValue(owner, out var cities))
            _priority[owner] = cities = Places.Where(p => p.OwnerId == owner && Frontier(p.Id, owner))
                .SelectMany(p => NearbyCities(p.CityId)).ToHashSet();
        return cities.Contains(own.CityId);
    }
}

internal static class OriginalAiProjection
{
    internal static OriginalDomesticInputs Domestic(SimulationEngine engine, ScenarioInitialState content, CorpsState corps,
        OriginalAiGeography geography, bool recruitmentAllowed)
    {
        var world = engine.Snapshot;
        var accountPlaces = world.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && s.OwnerId == corps.OwnerId &&
            (s.CorpsId == corps.Id || s.CorpsId is null && corps.IsRoot)).ToArray();
        var places = accountPlaces.Where(s => s.CanHostPersons && s.Facility?.BuilderUnitId is null).ToArray();
        var busy = world.Simulation.Tasks.Where(t => t.ForceId == corps.OwnerId).Select(t => t.OfficerId).ToHashSet();
        var available = world.Officers.Where(o => !o.IsDormant && o.OwnerId == corps.OwnerId && o.IsServing && !o.Acted &&
            o.UnitId is null && o.SettlementId is not null && !busy.Contains(o.Id)).ToLookup(o => o.SettlementId!.Value);
        int root = world.Corps.Single(c => c.OwnerId == corps.OwnerId && c.IsRoot).Id;
        var units = world.Units.Where(u => u.OwnerId == corps.OwnerId).ToDictionary(u => u.Id);
        var settlementCorps = world.Settlements.Where(s => !s.IsDestroyed && s.OwnerId == corps.OwnerId).ToDictionary(s => s.Id, s => s.CorpsId ?? root);
        int Account(OfficerState officer) => officer.UnitId is int unit && units.TryGetValue(unit, out var u) ? u.CorpsId ?? root :
            officer.SettlementId is int home ? settlementCorps.GetValueOrDefault(home, root) : root;
        var salaries = content.Information.Offices.ToDictionary(o => o.Id, o => o.Salary);
        long payroll = world.Officers.Where(o => !o.IsDormant && o.OwnerId == corps.OwnerId && o.IsServing && o.Status != OfficerStatus.Ruler && Account(o) == corps.Id)
            .Sum(o => o.OfficeId is int office ? (long)salaries[office] : 1);
        int moneyDays = 90 - ((world.Clock.Month - 1) % 3 * 30 + (world.Clock.Day - 1) / 10 * 10);
        int foodDays = (180 - ((world.Clock.Month - 1) * 30 + (world.Clock.Day - 1) / 10 * 10) + 360) % 360;
        if (foodDays == 0) foodDays = 360;
        // Round each consuming object before summing, as in public daily settlement.
        long dailyFood = accountPlaces.Where(s => s.ConsumesFood)
            .Sum(s => FoodSupplyForecast.GarrisonConsumption(s.Soldiers, s.Wounded)) +
            units.Values.Where(u => (u.CorpsId ?? root) == corps.Id)
                .Sum(u => FoodSupplyForecast.FieldConsumption(u.Soldiers, u.Wounded));
        foreach (var task in world.Simulation.Tasks.Where(t => t.ForceId == corps.OwnerId && t.Reinforcements is not null))
            if (settlementCorps.GetValueOrDefault(task.OriginSettlementId, root) == corps.Id)
                dailyFood += FoodSupplyForecast.FieldConsumption(task.Reinforcements!.Soldiers, task.Reinforcements.Wounded);
        long foodNeed = foodDays * dailyFood;
        var ownPeople = world.Officers.Where(o => o.OwnerId == corps.OwnerId).Select(o => o.Id).ToHashSet();
        var abilities = engine.GetAbilities(world).Where(p => ownPeople.Contains(p.Key))
            .ToImmutableDictionary();
        return new(corps.OwnerId, corps.Treasury.Money, corps.Treasury.Food, payroll * (moneyDays / 10), foodNeed, recruitmentAllowed,
            places.Select(s => new OriginalDomesticSite(DomesticObservation.Settlement(s, corps.Id, corps.Treasury.Money, corps.Treasury.Food,
                available[s.Id].Select(o => o.Id).ToImmutableArray()), s.Kind == SettlementKind.City,
                geography.Priority(s.Id, corps.OwnerId))).ToImmutableArray(), abilities);
    }
}
