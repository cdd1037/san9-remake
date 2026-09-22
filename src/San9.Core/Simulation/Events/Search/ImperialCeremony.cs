using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ImperialCeremonyResolvedEvent(GameClock Clock, string EventId, int ForceId, int RulerId,
    int PlaceId, ImmutableArray<int> LoyaltyRecipientIds) : DomainEvent;

internal static class ImperialCeremony
{
    internal static bool TryResolve(WorldMutation world, PersistentTask task, ImperialCeremonySearchEventDefinition d,
        EconomyRulebook economy, List<DomainEvent> events)
    {
        if (world.Simulation.ConsumedEventKeys.Contains(d.ConsumptionKey) || task.Journey?.DestinationPlaceId != d.PlaceId ||
            !world.Forces.TryGetValue(task.ForceId, out var force) || force.IsEliminated || force.LeaderId != task.OfficerId ||
            force.RankId != d.RequiredRankId || world.Officers[task.OfficerId].Status != OfficerStatus.Ruler ||
            !world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player)) return false;
        foreach (var ability in Enum.GetValues<OfficerAbility>())
            OfficerAbilityGrowth.Increase(world, task.OfficerId, ability, d.AbilityReward, events);
        var recipients = world.Officers.Values.Where(p => p.OwnerId == force.Id && p.IsServing && p.Status != OfficerStatus.Ruler)
            .OrderBy(p => p.Id).ToArray();
        foreach (var person in recipients)
            RecruitmentRules.SetLoyalty(world, person.Id, force.Id,
                (int)Math.Min(255L, (long)person.LoyaltyTo(force.Id) + d.LoyaltyReward), "imperial-ceremony", events);
        world.Replace(force with { BaseTrust = (int)Math.Min(1000L, (long)force.BaseTrust + d.BaseTrustReward) });
        world.ConsumeEvent(d.Candidate(d.ConsumptionKey), new(d.Id, EventOutcomeKind.Completed, false, EventResult.Succeeded));
        events.Add(new ImperialCeremonyResolvedEvent(world.Clock, d.Id, force.Id, task.OfficerId, d.PlaceId,
            recipients.Select(p => p.Id).ToImmutableArray()));
        return true;
    }
}
