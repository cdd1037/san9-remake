using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class BattleRetreat
{
    internal static int CaptureParticipants(WorldMutation world, BattleSource defeated, int winner, MapOccupancy area, MilitaryRulebook military)
    {
        bool Targets(MilitaryOrder? order) => order is { IsAttack: true } &&
            (defeated.IsSettlement ? order.UnitId is null && order.SettlementId == defeated.Id : order.UnitId == defeated.Id);
        int count = world.Units.Values.Count(u => u.OwnerId == winner && !u.IsEmbedded && !u.IsPersonnel && u.Soldiers > 0 &&
            Targets(u.EffectiveOrder) && MilitaryCombat.Area(u, military).IntersectsExpanded(area, 5));
        if (!defeated.IsSettlement)
            count += world.Settlements.Values.Count(s => s.OwnerId == winner &&
                MilitaryEngagements.FacilityTarget(world, s, military)?.Id == defeated.Id &&
                new BattleSource(s.Id, true).Area(world, military).IntersectsExpanded(area, 5));
        return count;
    }

    internal static SettlementState? Destination(WorldMutation world, OfficerState person, WorldPosition origin,
        int? excluded, MilitaryRulebook military, bool citiesOnly = false, int? preferredCorps = null)
    {
        int? corps = preferredCorps ?? PersonnelLocations.Corps(world, person);
        return world.Settlements.Values.Where(s => person.OwnerId is not null && s.OwnerId == person.OwnerId &&
                s.Id != excluded && s.CanHostPersons && !s.IsDestroyed && s.Facility?.BuilderUnitId is null &&
                s.Durability > 0 && (!citiesOnly || s.Kind == SettlementKind.City) && military.TryPlace(s, out _))
            .OrderBy(s => !citiesOnly && corps is not null && s.CorpsId == corps ? 0 : 1)
            .ThenBy(s => MapCoordinates.Distance(origin, military.Place(s).Position)).ThenBy(s => s.Id).FirstOrDefault();
    }

    internal static int Days(WorldPosition from, WorldPosition to) => checked((int)((MapCoordinates.Distance(from, to) + 29) / 30));
}
