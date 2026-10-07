using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>World-owned occupancy rectangles in stable order; passage is filtered per query.</summary>
internal sealed class MovementOccupancy
{
    private readonly MilitaryRulebook rules;
    private readonly SortedDictionary<(bool Unit, int Id), MapOccupancy> areas = [];

    internal MovementOccupancy(WorldMutation world, MilitaryRulebook rules) : this(world.Settlements.Values, world.Units.Values, rules) { }
    private MovementOccupancy(IEnumerable<SettlementState> places, IEnumerable<UnitState> units, MilitaryRulebook rules)
    {
        this.rules = rules;
        foreach (var place in places) Set(place);
        foreach (var unit in units) Set(unit);
    }
    internal static IReadOnlyList<MapOccupancy> Snapshot(WorldSnapshot world, MilitaryRulebook rules, int owner, UnitPolicies policies, bool includeUnits,
        int? movingUnit = null)
    {
        var units = world.Units.ToDictionary(u => u.Id);
        var places = world.Settlements.ToDictionary(s => s.Id);
        return new MovementOccupancy(world.Settlements, includeUnits ? world.Units : [], rules)
            .Query(units, places, policies, other => MilitaryDiplomacy.AutomaticEnemy(world, owner, other), movingUnit);
    }

    internal void Set(UnitState unit) => Set((true, unit.Id), unit.FormationId is int id
        ? new(unit.Position, rules.Formation(unit).Footprint) : null);
    internal void Set(SettlementState place) => Set((false, place.Id), !place.IsDestroyed && rules.TryPlace(place, out var p)
        ? rules.BuildingArea(p) : null);
    internal void RemoveUnit(int id) => Set((true, id), null);
    private void Set((bool Unit, int Id) key, MapOccupancy? area)
    {
        if (area is not { } next) areas.Remove(key);
        else if (!areas.TryGetValue(key, out var previous) || previous != next) areas[key] = next;
    }

    internal IReadOnlyList<MapOccupancy> Query(WorldMutation world, int owner, UnitPolicies policies, int? movingUnit = null, int? entry = null)
        => Query(world.Units, world.Settlements, policies, other => MilitaryDiplomacy.AutomaticEnemy(world, owner, other), movingUnit, entry);

    private IReadOnlyList<MapOccupancy> Query(IReadOnlyDictionary<int, UnitState> units, IReadOnlyDictionary<int, SettlementState> places,
        UnitPolicies policies, Func<int?, bool> enemyOf, int? movingUnit = null, int? entry = null)
    {
        var result = new List<MapOccupancy>();
        foreach (var (key, area) in areas)
        {
            if (key.Unit)
            {
                if (key.Id == movingUnit) continue;
                var unit = units[key.Id];
                if (unit.IsEmbedded || unit.IsPersonnel) continue;
                bool enemy = enemyOf(unit.OwnerId);
                result.Add(area with { Hard = enemy && policies.AttackApproachingEnemies,
                    Avoidance = enemy ? NavigationAvoidance.Hostile : NavigationAvoidance.Friendly });
            }
            else
            {
                if (key.Id == entry) continue;
                var place = places[key.Id];
                if (rules.Place(place).FacilityTypeId is int type && rules.Facilities[type].IsWork) result.Add(area);
                else if (place.OwnerId is not null && enemyOf(place.OwnerId))
                    result.Add(area with { Avoidance = NavigationAvoidance.Facility });
            }
        }
        return result;
    }
}

/// <summary>Read-only deployment queries use the same diplomacy and obstacle classification as daily movement.</summary>
public static class MilitaryRouteQuery
{
    public static IReadOnlyList<MapOccupancy> Occupancy(WorldSnapshot snapshot, MilitaryRulebook rules, int owner, UnitPolicies policies,
        bool includeUnits = true, int? movingUnit = null) => MovementOccupancy.Snapshot(snapshot, rules, owner, policies, includeUnits, movingUnit);

    public static WorldPosition? DeploymentPosition(UnitTravel travel, MapOccupancy place, IReadOnlyList<MapOccupancy> occupancy)
    {
        if (travel.Map.PassageCost(place.Position, travel.Shape, travel.Mode, occupancy) is not null) return place.Position;
        // Water-only formations can require an actual shoreline entrance instead of a land facility's center.
        return travel.Entrances(place).Cast<WorldPosition?>().FirstOrDefault(p =>
            travel.Map.PassageCost(p!.Value, travel.Shape, travel.Mode, occupancy) is not null);
    }
}
