using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class UnitServicePlaces
{
    internal static void Repair(WorldMutation world, SimulationRules rules)
    {
        foreach (var unit in world.Units.Values.Where(u => !u.IsEmbedded && u.ServicePlaceId is not null).OrderBy(u => u.Id).ToArray())
        {
            int? root = world.Corps.Values.SingleOrDefault(c => c.OwnerId == unit.OwnerId && c.IsRoot)?.Id;
            int? corps = unit.CorpsId ?? root;
            bool Eligible(SettlementState place) => place.OwnerId == unit.OwnerId && (place.CorpsId ?? root) == corps &&
                !place.IsDormant && !place.IsDestroyed && place.CanHostPersons && place.Durability > 0 &&
                place.Facility?.BuilderUnitId is null;
            if (unit.ServicePlaceId is int home && world.Settlements.TryGetValue(home, out var current) && Eligible(current))
                continue;
            SettlementState? destination = unit.EffectiveOrder is { Kind: MilitaryOrderKind.Transport, SettlementId: int target } &&
                world.Settlements.TryGetValue(target, out var arrival) && Eligible(arrival) ? arrival : null;
            destination ??= world.Settlements.Values.Where(Eligible)
                .Select(s => (Place: s, Position: Position(s)))
                .Where(s => s.Position is not null)
                .OrderBy(s => MapCoordinates.Distance(unit.Position, s.Position!.Value)).ThenBy(s => s.Place.Id)
                .Select(s => s.Place).FirstOrDefault();
            if (destination is not null) world.Replace(unit with { ServicePlaceId = destination.Id });
        }

        WorldPosition? Position(SettlementState place) => rules.Military?.TryPlace(place, out var militaryPlace) == true
            ? militaryPlace.Position : rules.Personnel.Places.TryGetValue(place.Id, out var personnelPlace) ? personnelPlace.Position : null;
    }
}
