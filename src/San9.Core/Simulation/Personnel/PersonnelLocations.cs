using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Administrative affiliation follows current location; service place is not physical presence.</summary>
internal static class PersonnelLocations
{
    internal static WorldPosition Position(WorldMutation world, PersonnelRulebook places, int id) =>
        world.Settlements.TryGetValue(id, out var settlement) && settlement.Facility is { } facility
            ? facility.Position : places.Places[id].Position;

    internal static PersonnelRoute Route(WorldMutation world, PersonnelRulebook places, int origin, int destination,
        WorldPosition? fieldDestination = null) => new(Position(world, places, origin),
            fieldDestination ?? Position(world, places, destination), fieldDestination is not null);

    internal static int? Corps(WorldMutation world, OfficerState person) => person.UnitId is int unit
        ? world.Units[unit].CorpsId : person.SettlementId is int home ? world.Settlements[home].CorpsId : null;

    internal static int? ServicePlace(WorldMutation world, OfficerState person) => person.UnitId is int unit
        ? world.Units[unit].ServicePlaceId : person.SettlementId;
}
