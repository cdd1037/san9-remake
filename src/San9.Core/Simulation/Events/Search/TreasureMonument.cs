using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record TreasureMonumentPrompt(Guid TaskId, int Step, string EventId, int ForceId,
    int SearchingOfficerId, int PlaceId, int TreasureId, uint Cost, ResourceAccount Account);
public sealed record TreasureMonumentResolvedEvent(GameClock Clock, string EventId, int ForceId,
    int SearchingOfficerId, int PlaceId, bool Accepted) : DomainEvent;

internal static class TreasureMonument
{
    internal static bool Eligible(WorldMutation world, PersistentTask task, SimulationRules rules, TreasureMonumentSearchEventDefinition d) =>
        !world.Simulation.ConsumedEventKeys.Contains(d.ConsumptionKey) && task.Journey?.DestinationPlaceId == d.PlaceId &&
        world.Forces.TryGetValue(task.ForceId, out var force) && !force.IsEliminated &&
        world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player) &&
        world.Officers.TryGetValue(task.OfficerId, out var actor) && actor.IsServing && actor.Status != OfficerStatus.Ruler &&
        world.Settlements.TryGetValue(d.PlaceId, out var place) && !place.IsDestroyed && place.OwnerId == force.Id &&
        force.RankId is int rank && rules.Politics.Ranks.TryGetValue(rank, out var currentRank) &&
        rules.Politics.Ranks.TryGetValue(d.MinimumRankId, out var minimumRank) && currentRank.Precedence >= minimumRank.Precedence &&
        SearchEventResources.HasFunds(world, task, d.MinimumMoneyExclusive, d.Cost) &&
        world.Treasures.TryGetValue(d.TreasureId, out var item) && item.HolderId is int holderId &&
        world.Officers.TryGetValue(holderId, out var holder) && holder.OwnerId == force.Id &&
        (holder.IsServing || holder.Status == OfficerStatus.Captive);

    internal static bool CanResolve(WorldMutation world, PersistentTask task, SimulationRules rules, TreasureMonumentEncounter encounter) =>
        rules.Personnel.SearchEvents.GetValueOrDefault(encounter.EventId) is TreasureMonumentSearchEventDefinition d && Eligible(world, task, rules, d);

    internal static void Resolve(WorldMutation world, PersistentTask task, SimulationRules rules,
        TreasureMonumentEncounter encounter, bool accept, List<DomainEvent> events)
    {
        if (!CanResolve(world, task, rules, encounter)) throw new InvalidOperationException("Treasure monument is no longer valid.");
        var d = (TreasureMonumentSearchEventDefinition)rules.Personnel.SearchEvents[encounter.EventId];
        if (accept)
        {
            ResourceAccounts.Change(world, SearchEventResources.Account(world, task), ResourceKind.Money,
                -(long)d.Cost, "treasure-monument", task.OfficerId, events, requireFull: true);
            var force = world.Forces[task.ForceId];
            world.Replace(force with { BaseTrust = (int)Math.Min(1000L, (long)force.BaseTrust + d.BaseTrustReward) });
        }
        world.ConsumeEvent(d.Candidate(d.ConsumptionKey), new(d.Id, EventOutcomeKind.Completed, false,
            accept ? EventResult.Succeeded : EventResult.Declined));
        events.Add(new TreasureMonumentResolvedEvent(world.Clock, d.Id, task.ForceId, task.OfficerId, d.PlaceId, accept));
    }
}
