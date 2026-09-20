using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record DivinerVisitResolvedEvent(GameClock Clock, string EventId, int ForceId,
    int SearchingOfficerId, int DivinerOfficerId, int PlaceId) : DomainEvent;

internal static class DivinerVisit
{
    internal static bool TryResolve(WorldMutation world, PersistentTask task, DivinerVisitSearchEventDefinition d,
        List<DomainEvent> events)
    {
        if (!world.Forces.TryGetValue(task.ForceId, out var force) || force.IsEliminated ||
            !world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player) ||
            force.EventHistory.Contains(d.HistoryKey) || task.Journey?.DestinationPlaceId != d.PlaceId) return false;
        int chance = world.Abilities(world.Officers[task.OfficerId]).Politics / d.PoliticsChanceDivisor;
        if (world.NextRandom("event-search-eligibility") % 100 >= (ulong)chance || force.Trust <= d.MinimumTrustExclusive ||
            !world.Officers.TryGetValue(d.DivinerOfficerId, out var diviner) || diviner.Status != OfficerStatus.Special) return false;
        world.Replace(force with { EventHistory = force.EventHistory.Append(d.HistoryKey).OrderBy(k => k.Value, StringComparer.Ordinal).ToImmutableArray() });
        events.Add(new DivinerVisitResolvedEvent(world.Clock, d.Id, force.Id, task.OfficerId, diviner.Id, d.PlaceId));
        return true;
    }
}
