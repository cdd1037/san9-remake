using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record PhysicianVisitResolvedEvent(GameClock Clock, string EventId, int ForceId,
    int SearchingOfficerId, int PhysicianOfficerId, int PlaceId, int TreasureId) : DomainEvent;

internal static class PhysicianVisit
{
    internal static bool TryResolve(WorldMutation world, PersistentTask task, PhysicianVisitSearchEventDefinition d,
        EconomyRulebook economy, List<DomainEvent> events)
    {
        if (!world.Forces.TryGetValue(task.ForceId, out var force) || force.IsEliminated ||
            !world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player)) return false;
        var site = d.Sites.SingleOrDefault(s => s.PlaceId == task.Journey?.DestinationPlaceId);
        if (site is null) return false;
        int chance = world.Abilities(world.Officers[task.OfficerId]).Politics / d.PoliticsChanceDivisor;
        if (world.NextRandom("event-search-eligibility") % 100 >= (ulong)chance || force.BaseTrust <= d.MinimumBaseTrustExclusive ||
            !world.Officers.TryGetValue(site.PhysicianOfficerId, out var physician) || physician.Status != OfficerStatus.Special ||
            !world.Treasures.TryGetValue(site.TreasureId, out var treasure) || !treasure.Discovered || treasure.HolderId != physician.Id)
            return false;
        PersonnelTreasures.Transfer(world, treasure, task.OfficerId, force.Id, "physician-visit", events);
        if (economy.ImperialSealId == treasure.Id) EconomicCycle.UpdateTrust(world, economy, events);
        events.Add(new PhysicianVisitResolvedEvent(world.Clock, d.Id, force.Id, task.OfficerId, physician.Id, site.PlaceId, treasure.Id));
        return true;
    }
}
