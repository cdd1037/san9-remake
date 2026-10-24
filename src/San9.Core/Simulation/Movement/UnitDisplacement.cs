using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Restores field positions invalidated by a settlement ownership transition.</summary>
internal static class UnitDisplacement
{
    internal static void Resolve(WorldMutation world, int settlementId, MilitaryRulebook rules, List<DomainEvent> events)
    {
        var place = world.Settlements[settlementId];
        if (place.IsDestroyed || place.IsDormant || !rules.TryPlace(place, out _)) return;
        var building = rules.SettlementArea(place) with { Object = new(settlementId, true) };
        foreach (int id in world.Units.Values.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.FormationId is not null &&
            MilitaryCombat.Area(u, rules).Overlaps(building)).Select(u => u.Id).Order().ToArray())
        {
            var unit = world.Units[id];
            var occupancy = world.MovementOccupancy(rules, unit.OwnerId, unit.Policies, id, null);
            if (!occupancy.Any(o => o.Object == building.Object && o.Hard)) continue;
            var destination = Destination(world, unit, settlementId, rules);
            if (destination is null || Exit(unit, building, rules.SettlementArea(destination), occupancy, rules) is not { } position) continue;
            world.Replace(unit with
            {
                Position = position, Facing = WorldDirections.Between(unit.Position, position) ?? unit.Facing,
                Lookahead = null, ContactTarget = null, EngagementTargetId = null,
                ActiveEngagement = false, PassiveEngagement = false, MovementRemainder = 0
            });
            events.Add(new UnitDisplacedEvent(world.Clock, id, settlementId, unit.Position, position));
        }
    }

    private static SettlementState? Destination(WorldMutation world, UnitState unit, int excluded, MilitaryRulebook rules) =>
        world.Settlements.Values.Where(s => s.Id != excluded && s.OwnerId == unit.OwnerId && !s.IsDestroyed && !s.IsDormant &&
            s.CanHostPersons && s.Durability > 0 && s.Facility?.BuilderUnitId is null && rules.TryPlace(s, out _))
            .OrderBy(s => s.Id == unit.ServicePlaceId ? 0 : s.CorpsId == unit.CorpsId ? 1 : 2)
            .ThenBy(s => MapCoordinates.Distance(unit.Position, rules.Place(s).Position)).ThenBy(s => s.Id).FirstOrDefault();

    private static WorldPosition? Exit(UnitState unit, MapOccupancy building, MapOccupancy destination,
        IReadOnlyList<MapOccupancy> occupancy, MilitaryRulebook rules)
    {
        var travel = rules.Travel(unit);
        var route = travel.Map.FindRoute(unit.Position, destination.Position, travel.Shape, travel.Mode, occupancy);
        if (!route.Reachable) return null;
        var cursor = unit.Position;
        foreach (var cell in route.Steps)
        {
            // Inspect the intervening fine positions too: relocation must not jump an unrelated obstacle.
            while (cursor != cell)
            {
                cursor = cursor.X != cell.X ? cursor with { X = cursor.X + Math.Sign(cell.X - cursor.X) }
                    : cursor with { Y = cursor.Y + Math.Sign(cell.Y - cursor.Y) };
                var area = travel.AreaAt(cursor);
                if (occupancy.Any(o => o.Hard && o.Object != building.Object && area.Overlaps(o))) return null;
                if (area.Overlaps(building)) continue;
                return travel.Map.PassageCost(cursor, travel.Shape, travel.Mode, occupancy) is not null ? cursor : null;
            }
        }
        return null;
    }
}
