using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class DiplomacyJourney
{
    internal static int Days(WorldPosition source, WorldPosition destination) =>
        Math.Max(10, checked((PersonnelRulebook.JourneyDays(source, destination) + 9) / 10 * 10));

    internal static int Duration(PersistentTask task, SimulationRules rules) =>
        task.Started.IsXunBoundary && task.Journey is { } journey &&
        rules.Personnel.Places.TryGetValue(task.OriginSettlementId, out var origin) &&
        rules.Personnel.Places.TryGetValue(journey.DestinationPlaceId, out var destination) &&
        (journey.DestinationPosition is not { } position || rules.Military?.RegionAt(position) == journey.DestinationPlaceId)
            ? Days(origin.Position, journey.DestinationPosition ?? destination.Position) : -1;
}
