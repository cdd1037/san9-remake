using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class UnitServicePlaces
{
    internal static void Repair(WorldMutation world, SimulationRules rules)
    {
        foreach (var unit in world.Units.Values.Where(u => !u.IsEmbedded && u.ServicePlaceId is not null).OrderBy(u => u.Id).ToArray())
            Repair(world, unit, place => rules.Military?.TryPlace(place, out var militaryPlace) == true
                ? militaryPlace.Position : rules.Personnel.Places.TryGetValue(place.Id, out var personnelPlace) ? personnelPlace.Position : null);
    }

    internal static UnitState Repair(WorldMutation world, UnitState unit, MilitaryRulebook rules) =>
        unit.IsEmbedded ? unit : Repair(world, unit, place => rules.TryPlace(place, out var definition) ? definition.Position : null);

    private static UnitState Repair(WorldMutation world, UnitState unit, Func<SettlementState, WorldPosition?> position)
    {
        int? root = world.Corps.Values.SingleOrDefault(c => c.OwnerId == unit.OwnerId && c.IsRoot)?.Id;
        bool Eligible(SettlementState place) => UnitServicePlaceRules.IsEligible(unit, place, root);
        if (unit.ServicePlaceId is int home && world.Settlements.TryGetValue(home, out var current) && Eligible(current))
            return unit;
        SettlementState? destination = unit.EffectiveOrder is { Kind: MilitaryOrderKind.Transport, SettlementId: int target } &&
            world.Settlements.TryGetValue(target, out var arrival) && Eligible(arrival) ? arrival : null;
        destination ??= world.Settlements.Values.Where(Eligible)
            .Select(s => (Place: s, Position: position(s)))
            .Where(s => s.Position is not null)
            .OrderBy(s => MapCoordinates.Distance(unit.Position, s.Position!.Value)).ThenBy(s => s.Place.Id)
            .Select(s => s.Place).FirstOrDefault();
        if (destination is null) return unit;
        unit = unit with { ServicePlaceId = destination.Id };
        world.Replace(unit);
        return world.Units[unit.Id];
    }
}
