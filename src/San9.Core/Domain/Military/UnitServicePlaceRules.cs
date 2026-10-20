namespace San9.Core.Domain;

/// <summary>The service attachment must be an operational hosting place in the unit's effective corps.</summary>
public static class UnitServicePlaceRules
{
    public static bool IsEligible(UnitState unit, SettlementState place, int? rootCorpsId) =>
        place.OwnerId == unit.OwnerId && (place.CorpsId ?? rootCorpsId) == (unit.CorpsId ?? rootCorpsId) &&
        !place.IsDormant && !place.IsDestroyed && place.CanHostPersons && place.Durability > 0 &&
        place.Facility?.BuilderUnitId is null;
}
