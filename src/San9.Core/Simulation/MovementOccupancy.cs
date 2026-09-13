using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>World-owned occupancy rectangles in stable order; passage is filtered per query.</summary>
internal sealed class MovementOccupancy
{
    private readonly MilitaryRulebook rules;
    private readonly SortedDictionary<(bool Unit, int Id), MapOccupancy> areas = [];

    internal MovementOccupancy(WorldMutation world, MilitaryRulebook rules)
    {
        this.rules = rules;
        foreach (var place in world.Settlements.Values) Set(place);
        foreach (var unit in world.Units.Values) Set(unit);
    }

    internal void Set(UnitState unit) => Set((true, unit.Id), unit.FormationId is int id
        ? new(unit.Position, rules.Formations[id].Footprint) : null);
    internal void Set(SettlementState place) => Set((false, place.Id), !place.IsDestroyed && rules.TryPlace(place, out var p)
        ? new(p.Position, p.Footprint) : null);
    internal void RemoveUnit(int id) => Set((true, id), null);
    private void Set((bool Unit, int Id) key, MapOccupancy? area)
    {
        if (area is not { } next) areas.Remove(key);
        else if (!areas.TryGetValue(key, out var previous) || previous != next) areas[key] = next;
    }

    internal IReadOnlyList<MapOccupancy> Query(WorldMutation world, int movingUnit, int? entry, MapOccupancy envelope)
    {
        var result = new List<MapOccupancy>();
        var actor = world.Units[movingUnit];
        foreach (var (key, area) in areas)
        {
            if (!area.Overlaps(envelope)) continue;
            if (key.Unit) { if (key.Id == movingUnit) continue; }
            else
            {
                if (key.Id == entry) continue;
                var place = world.Settlements[key.Id];
                if (!MilitaryDiplomacy.Work(world, place.Id, rules) &&
                    (place.OwnerId is null || !MilitaryDiplomacy.AutomaticEnemy(world, actor.OwnerId, place.OwnerId))) continue;
            }
            result.Add(area);
        }
        return result;
    }

}
