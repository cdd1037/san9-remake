using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ScholarReturnPrompt(Guid TaskId, int Step, string EventId, int ForceId,
    int SearchingOfficerId, int ScholarOfficerId, int PlaceId);
public sealed record ScholarReturnResolvedEvent(GameClock Clock, string EventId, int ForceId,
    int SearchingOfficerId, int ScholarOfficerId, int PlaceId, bool Accepted, int? LearnedTacticId) : DomainEvent;

internal static class ScholarReturn
{
    private static bool BeforeChance(WorldMutation world, PersistentTask task, ScholarReturnSearchEventDefinition d) =>
        !world.Simulation.ConsumedEventKeys.Contains(d.ConsumptionKey) &&
        world.Forces.TryGetValue(task.ForceId, out var force) && !force.IsEliminated &&
        world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player) &&
        world.Officers[task.OfficerId].BaseAbilities.Intelligence < d.MaximumBaseAbilityExclusive &&
        world.Officers[task.OfficerId].BaseAbilities.Politics < d.MaximumBaseAbilityExclusive &&
        task.Journey?.DestinationPlaceId == d.PlaceId;

    private static bool AfterChance(WorldMutation world, PersistentTask task, ScholarReturnSearchEventDefinition d) =>
        world.Clock.DayNumber >= d.EarliestDate.DayNumber && world.Clock.DayNumber <= d.LatestDate.DayNumber &&
        world.Forces[task.ForceId].Trust > d.MinimumTrustExclusive &&
        world.Forces[task.ForceId].BaseTrust < d.MaximumBaseTrustExclusive &&
        ResourceAccounts.Balance(world, SearchEventResources.Account(world, task), ResourceKind.Money) > d.MinimumMoneyExclusive &&
        world.Officers.TryGetValue(d.ScholarOfficerId, out var scholar) && scholar.Status != OfficerStatus.Dead;

    internal static bool Prepare(WorldMutation world, PersistentTask task, ScholarReturnSearchEventDefinition d)
    {
        if (!BeforeChance(world, task, d)) return false;
        int chance = world.Abilities(world.Officers[task.OfficerId]).Politics / d.PoliticsChanceDivisor;
        return world.NextRandom("event-search-eligibility") % 100 < (ulong)chance && AfterChance(world, task, d);
    }

    internal static bool CanResolve(WorldMutation world, PersistentTask task, PersonnelRulebook rules, ScholarReturnEncounter encounter) =>
        rules.SearchEvents.GetValueOrDefault(encounter.EventId) is ScholarReturnSearchEventDefinition d &&
        BeforeChance(world, task, d) && AfterChance(world, task, d);

    internal static void Resolve(WorldMutation world, PersistentTask task, PersonnelRulebook rules, EconomyRulebook economy,
        ScholarReturnEncounter encounter, bool accept, List<DomainEvent> events)
    {
        if (!CanResolve(world, task, rules, encounter)) throw new InvalidOperationException("Scholar return is no longer valid.");
        var d = (ScholarReturnSearchEventDefinition)rules.SearchEvents[encounter.EventId];
        int? learned = null;
        if (accept)
        {
            OfficerAbilityGrowth.Increase(world, task.OfficerId, OfficerAbility.Intelligence, d.IntelligenceReward, events);
            learned = d.RewardTacticIds.Where(id => !world.Officers[task.OfficerId].LearnedTactics.Contains(id)).Select(id => (int?)id).FirstOrDefault();
            if (learned is int tactic) OfficerTacticGrowth.Grant(world, task.OfficerId, tactic, events);
            else OfficerAbilityGrowth.Increase(world, task.OfficerId, OfficerAbility.Politics, d.FallbackPoliticsReward, events);
            var force = world.Forces[task.ForceId];
            world.Replace(force with
            {
                BaseTrust = (int)Math.Min(1000L, (long)force.BaseTrust + d.BaseTrustReward),
                EventHistory = force.EventHistory.Append(d.SuccessHistoryKey).Distinct().OrderBy(k => k.Value, StringComparer.Ordinal).ToImmutableArray()
            });
        }
        world.ConsumeEvent(d.Candidate(d.ConsumptionKey), new(d.Id, EventOutcomeKind.Completed, false,
            accept ? EventResult.Succeeded : EventResult.Declined));
        events.Add(new ScholarReturnResolvedEvent(world.Clock, d.Id, task.ForceId, task.OfficerId, d.ScholarOfficerId, d.PlaceId, accept, learned));
    }
}
