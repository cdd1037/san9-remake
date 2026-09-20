using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SageVisitResolvedEvent(GameClock Clock, string EventId, int ForceId,
    int SearchingOfficerId, int SageOfficerId, int PlaceId, ImmutableArray<int> LoyaltyRecipientIds) : DomainEvent;

internal static class SageVisit
{
    internal static bool TryResolve(WorldMutation world, PersistentTask task, SageVisitSearchEventDefinition d,
        List<DomainEvent> events)
    {
        if (task.Journey?.DestinationPlaceId != d.PlaceId ||
            !world.Forces.TryGetValue(task.ForceId, out var force) || force.IsEliminated ||
            !world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player)) return false;
        int chance = world.Abilities(world.Officers[task.OfficerId]).Politics / d.PoliticsChanceDivisor;
        if (world.NextRandom("event-search-eligibility") % 100 >= (ulong)chance ||
            force.BaseTrust <= d.MinimumBaseTrustExclusive ||
            !world.Officers.TryGetValue(d.SageOfficerId, out var sage) || sage.Status != OfficerStatus.Special) return false;
        var recommended = d.RecommendedOfficerIds.ToHashSet();
        var recipients = world.Officers.Values.Where(p => recommended.Contains(p.Id) &&
            p.LoyaltyTo(force.Id) <= d.MaximumRecipientLoyaltyInclusive).OrderBy(p => p.Id).ToArray();
        foreach (var person in recipients)
            RecruitmentRules.SetLoyalty(world, person.Id, force.Id,
                (int)Math.Min(255L, (long)person.LoyaltyTo(force.Id) + d.LoyaltyReward), "sage-visit", events);
        events.Add(new SageVisitResolvedEvent(world.Clock, d.Id, force.Id, task.OfficerId, sage.Id, d.PlaceId,
            recipients.Select(p => p.Id).ToImmutableArray()));
        return true;
    }
}
