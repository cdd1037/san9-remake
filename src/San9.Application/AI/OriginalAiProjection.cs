using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>One control run shares geography across corps and work; only topology and ownership invalidate it.</summary>
internal sealed class OriginalAiGeographyContext(ScenarioInitialState content, MilitaryRulebook? military)
{
    private OriginalAiGeography? current;
    internal OriginalAiGeography Get(WorldSnapshot world)
    {
        if (current is null || !current.Matches(world)) current = new(content, military, world);
        return current;
    }
}

/// <summary>Public ownership/topology only; never inspects foreign tasks or unit orders.</summary>
internal sealed class OriginalAiGeography
{
    internal sealed record Place(int Id, int? OwnerId, int CityId, int? ProvinceId);
    internal ImmutableArray<Place> Places { get; }
    private readonly Dictionary<int, Place> _places;
    private readonly Dictionary<int, int[]> _neighbors;
    private readonly Dictionary<int, int[]> _cityNeighbors;
    private readonly Dictionary<int, HashSet<int>> _priority = [];
    private IReadOnlyList<SettlementState> _source;

    internal bool Matches(WorldSnapshot world)
    {
        if (ReferenceEquals(_source, world.Settlements)) return true;
        if (_source.Count != world.Settlements.Count) return false;
        for (int i = 0; i < _source.Count; i++)
        {
            var before = _source[i];
            var after = world.Settlements[i];
            if (before.Id != after.Id || before.OwnerId != after.OwnerId || before.Kind != after.Kind ||
                before.IsDormant != after.IsDormant || before.IsDestroyed != after.IsDestroyed || before.Facility != after.Facility)
                return false;
        }
        // Resource, durability and action-flag changes do not change ownership or topology.
        _source = world.Settlements;
        return true;
    }

    internal OriginalAiGeography(ScenarioInitialState content, MilitaryRulebook? military, WorldSnapshot world)
    {
        _source = world.Settlements;
        var places = new List<Place>();
        var settlements = world.Settlements.ToDictionary(s => s.Id);
        foreach (var s in world.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed))
        {
            int? region = military is not null && military.TryPlace(s, out var p) ? military.RegionAt(p.Position) : null;
            int? city = s.Kind == SettlementKind.City ? s.Id : region is int r && content.RegionCityIds.TryGetValue(r, out int c) ? c :
                content.RegionCityIds.TryGetValue(s.Id, out int home) ? home : null;
            if (city is int id) places.Add(new(s.Id, s.OwnerId, id, content.CityProvinceIds.TryGetValue(id, out int province) ? province : null));
        }
        foreach (var p in content.UnbuiltPlaces.Where(p => !settlements.TryGetValue(p.Id, out var existing) || existing.IsDestroyed))
            if (content.RegionCityIds.TryGetValue(p.Id, out int city))
                places.Add(new(p.Id, null, city, content.CityProvinceIds.TryGetValue(city, out int province) ? province : null));
        Places = places.ToImmutableArray();
        _places = places.ToDictionary(p => p.Id);
        _cityNeighbors = content.CityConnections.GroupBy(c => c.FromCityId).ToDictionary(g => g.Key, g => g.Select(c => c.ToCityId).Distinct().ToArray());
        if (military is null)
            _neighbors = _cityNeighbors;
        else
        {
            var regionPlaces = places.GroupBy(p => settlements.GetValueOrDefault(p.Id) is { } s && military.TryPlace(s, out var actual)
                    ? military.RegionAt(actual.Position) ?? p.Id : p.Id)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToArray());
            _neighbors = military.TerritoryConnections(world.Settlements)
                .SelectMany(edge => regionPlaces.GetValueOrDefault(edge.FromRegionId, [])
                    .SelectMany(from => regionPlaces.GetValueOrDefault(edge.ToRegionId, []).Select(to => (From: from, To: to))))
                .GroupBy(p => p.From).ToDictionary(g => g.Key, g => g.Select(p => p.To).Distinct().ToArray());
        }
    }

    internal IEnumerable<Place> Neighbors(int id) => _neighbors.GetValueOrDefault(id, [])
        .Select(id => _places.GetValueOrDefault(id)).OfType<Place>();
    internal IEnumerable<int> NearbyCities(int city) => new[] { city }.Concat(_cityNeighbors.GetValueOrDefault(city, []));
    internal bool Frontier(int place, int owner) => Neighbors(place).Any(p => p.OwnerId != owner);
    internal bool Priority(int place, int owner)
    {
        var own = _places.GetValueOrDefault(place);
        if (own is null) return false;
        if (!_priority.TryGetValue(owner, out var cities))
            _priority[owner] = cities = Places.Where(p => p.OwnerId == owner && Frontier(p.Id, owner))
                .SelectMany(p => NearbyCities(p.CityId)).ToHashSet();
        return cities.Contains(own.CityId);
    }
}

internal sealed class OriginalAiProjection(SimulationEngine engine, ScenarioInitialState content, int corpsId,
    OriginalAiGeography geography, bool recruitmentAllowed)
{
    private readonly Dictionary<int, int> salaries = content.Information.Offices.ToDictionary(o => o.Id, o => o.Salary);
    private WorldSnapshot? projected;
    private OriginalDomesticInputs? inputs;
    private ImmutableDictionary<int, OfficerAbilities>? abilitySource;
    private ImmutableDictionary<int, OfficerAbilities> ownAbilities = ImmutableDictionary<int, OfficerAbilities>.Empty;
    private HashSet<int> ownPeople = [];
    private int? abilityOwner;

    internal OriginalDomesticInputs Domestic()
    {
        var world = engine.Snapshot;
        if (ReferenceEquals(projected, world)) return inputs!;
        var corps = world.Corps.Single(c => c.Id == corpsId);
        var accountPlaces = world.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && s.OwnerId == corps.OwnerId &&
            (s.CorpsId == corps.Id || s.CorpsId is null && corps.IsRoot)).ToArray();
        var places = accountPlaces.Where(s => s.CanHostPersons && engine.CanStartSettlementWork(s.Id)).ToArray();
        var busy = world.Simulation.Tasks.Where(t => t.ForceId == corps.OwnerId).Select(t => t.OfficerId).ToHashSet();
        var available = world.Officers.Where(o => !o.IsDormant && o.OwnerId == corps.OwnerId && o.IsServing && !o.Acted &&
            o.UnitId is null && o.SettlementId is not null && !busy.Contains(o.Id)).ToLookup(o => o.SettlementId!.Value);
        int root = world.Corps.Single(c => c.OwnerId == corps.OwnerId && c.IsRoot).Id;
        var units = world.Units.Where(u => u.OwnerId == corps.OwnerId).ToDictionary(u => u.Id);
        var settlementCorps = world.Settlements.Where(s => !s.IsDestroyed && s.OwnerId == corps.OwnerId).ToDictionary(s => s.Id, s => s.CorpsId ?? root);
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
        var source = engine.GetAbilities(world);
        int ownCount = 0;
        bool membershipChanged = abilityOwner != corps.OwnerId;
        foreach (var person in world.Officers)
            if (person.OwnerId == corps.OwnerId)
            {
                ownCount++;
                membershipChanged |= !ownPeople.Contains(person.Id);
            }
        membershipChanged |= ownCount != ownPeople.Count;
        if (membershipChanged)
            ownPeople = world.Officers.Where(o => o.OwnerId == corps.OwnerId).Select(o => o.Id).ToHashSet();
        if (membershipChanged || !ReferenceEquals(abilitySource, source))
            ownAbilities = source.Where(p => ownPeople.Contains(p.Key)).ToImmutableDictionary();
        abilityOwner = corps.OwnerId;
        abilitySource = source;
        projected = world;
        return inputs = new(corps.OwnerId, corps.Treasury.Money, corps.Treasury.Food, SalaryDemand(world, corps, salaries, root, units, settlementCorps), foodNeed, recruitmentAllowed,
            places.Select(s => new OriginalDomesticSite(DomesticObservation.Settlement(s, corps.Id, corps.Treasury.Money, corps.Treasury.Food,
                available[s.Id].Select(o => o.Id).ToImmutableArray()), s.Kind == SettlementKind.City,
                geography.Priority(s.Id, corps.OwnerId))).ToImmutableArray(), ownAbilities);
    }

    internal DomesticSettlementObservation? Observe(int id)
    {
        var world = engine.Snapshot;
        var corps = world.Corps.Single(c => c.Id == corpsId);
        var place = world.Settlements.FirstOrDefault(s => s.Id == id && !s.IsDormant && !s.IsDestroyed &&
            s.OwnerId == corps.OwnerId && (s.CorpsId == corps.Id || s.CorpsId is null && corps.IsRoot) &&
            s.CanHostPersons && engine.CanStartSettlementWork(s.Id));
        return place is null ? null : DomesticObservation.Settlement(place, corpsId, corps.Treasury.Money, corps.Treasury.Food, []);
    }

    internal static long SalaryDemand(WorldSnapshot world, CorpsState corps, IReadOnlyDictionary<int, int> salaries)
    {
        int root = world.Corps.Single(c => c.OwnerId == corps.OwnerId && c.IsRoot).Id;
        var units = world.Units.Where(u => u.OwnerId == corps.OwnerId).ToDictionary(u => u.Id);
        var accounts = world.Settlements.Where(s => !s.IsDestroyed && s.OwnerId == corps.OwnerId)
            .ToDictionary(s => s.Id, s => s.CorpsId ?? root);
        return SalaryDemand(world, corps, salaries, root, units, accounts);
    }

    private static long SalaryDemand(WorldSnapshot world, CorpsState corps, IReadOnlyDictionary<int, int> salaries,
        int root, IReadOnlyDictionary<int, UnitState> units, IReadOnlyDictionary<int, int> accounts)
    {
        long payroll = 0;
        foreach (var officer in world.Officers)
        {
            if (officer.IsDormant || officer.OwnerId != corps.OwnerId || !officer.IsServing || officer.Status == OfficerStatus.Ruler) continue;
            int account = officer.UnitId is int unit && units.TryGetValue(unit, out var u) ? u.CorpsId ?? root :
                officer.SettlementId is int home ? accounts.GetValueOrDefault(home, root) : root;
            if (account == corps.Id) payroll += officer.OfficeId is int office ? salaries[office] : 1;
        }
        int days = 90 - ((world.Clock.Month - 1) % 3 * 30 + (world.Clock.Day - 1) / 10 * 10);
        return payroll * (days / 10);
    }
}
