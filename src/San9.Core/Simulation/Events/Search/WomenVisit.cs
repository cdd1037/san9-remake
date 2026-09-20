using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record WomenVisitResolvedEvent(GameClock Clock, string EventId, int ForceId,
    int SearchingOfficerId, int VisitorOfficerId, int PlaceId, int LearnedTacticId) : DomainEvent;

internal static class WomenVisit
{
    internal static bool TryResolve(WorldMutation world, PersistentTask task, WomenVisitSearchEventDefinition d,
        List<DomainEvent> events)
    {
        if (!world.Forces.TryGetValue(task.ForceId, out var force) || force.IsEliminated ||
            !world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player)) return false;
        var site = d.Sites.SingleOrDefault(s => s.PlaceId == task.Journey?.DestinationPlaceId);
        if (site is null) return false;
        var person = world.Officers[task.OfficerId];
        int chance = world.Abilities(person).Politics / d.PoliticsChanceDivisor;
        if (world.NextRandom("event-search-eligibility") % 100 >= (ulong)chance ||
            force.BaseTrust <= d.MinimumBaseTrustExclusive || force.Trust >= d.MaximumTrustExclusive ||
            !site.RequiredLeaderIds.IsEmpty && (force.LeaderId is not int leader || !site.RequiredLeaderIds.Contains(leader)) ||
            site.RequiredWorldKey is PersistentEventKey required && !world.Simulation.ConsumedEventKeys.Contains(required) ||
            site.EarliestScenarioStart is GameClock start && world.Simulation.Started.DayNumber < start.DayNumber &&
                !(site.AlternativeForceHistoryKey is PersistentEventKey history && force.EventHistory.Contains(history)) ||
            !world.Officers.TryGetValue(site.VisitorOfficerId, out var visitor) || visitor.Status == OfficerStatus.Dead)
            return false;
        int? learned = site.RewardTacticIds.Where(id => !person.LearnedTactics.Contains(id)).Select(id => (int?)id).FirstOrDefault();
        if (learned is not int tactic) return false;
        OfficerTacticGrowth.Grant(world, person.Id, tactic, events);
        events.Add(new WomenVisitResolvedEvent(world.Clock, d.Id, force.Id, person.Id, visitor.Id, site.PlaceId, tactic));
        return true;
    }
}
