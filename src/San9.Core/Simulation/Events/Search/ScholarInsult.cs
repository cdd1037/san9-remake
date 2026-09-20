using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ScholarInsultPrompt(Guid TaskId, int Step, string EventId, int ForceId,
    int SearchingOfficerId, int ScholarOfficerId, int PlaceId);
public sealed record ScholarInsultResolvedEvent(GameClock Clock, string EventId, int ForceId,
    int SearchingOfficerId, int ScholarOfficerId, int PlaceId, bool AttemptedExecution,
    int Intelligence, int Injury, int BaseTrust) : DomainEvent;

internal static class ScholarInsult
{
    private static bool BeforeChance(WorldMutation world, PersistentTask task, PersonnelRulebook rules)
    {
        if (!world.Forces.TryGetValue(task.ForceId, out var force) || force.IsEliminated ||
            !world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player) || task.Journey is null) return false;
        int place = task.Journey.DestinationPlaceId;
        int? owner = world.Settlements.TryGetValue(place, out var settlement) ? settlement.OwnerId :
            rules.RegionCities.TryGetValue(place, out int city) && world.Settlements.TryGetValue(city, out var related) ? related.OwnerId : null;
        return owner == force.Id;
    }

    private static bool AfterChance(WorldMutation world, PersistentTask task, ScholarInsultSearchEventDefinition d) =>
        world.Forces[task.ForceId].BaseTrust < d.MaximumBaseTrustExclusive &&
        world.Officers[task.OfficerId].Injury < d.MaximumInjuryExclusive &&
        world.Officers.TryGetValue(d.ScholarOfficerId, out var scholar) && scholar.Status != OfficerStatus.Dead;

    internal static bool Prepare(WorldMutation world, PersistentTask task, PersonnelRulebook rules, ScholarInsultSearchEventDefinition d) =>
        BeforeChance(world, task, rules) &&
        world.NextRandom("event-search-eligibility") % 100 < (ulong)(world.Abilities(world.Officers[task.OfficerId]).Intelligence / d.IntelligenceChanceDivisor) &&
        AfterChance(world, task, d);

    internal static bool CanResolve(WorldMutation world, PersistentTask task, SimulationRules rules, ScholarInsultEncounter encounter) =>
        rules.Personnel.SearchEvents.GetValueOrDefault(encounter.EventId) is ScholarInsultSearchEventDefinition d &&
        BeforeChance(world, task, rules.Personnel) && AfterChance(world, task, d);

    internal static void Resolve(WorldMutation world, PersistentTask task, SimulationRules rules,
        ScholarInsultEncounter encounter, bool attemptExecution, List<DomainEvent> events)
    {
        if (!CanResolve(world, task, rules, encounter)) throw new InvalidOperationException("Scholar insult is no longer valid.");
        var d = (ScholarInsultSearchEventDefinition)rules.Personnel.SearchEvents[encounter.EventId];
        var actor = world.Officers[task.OfficerId]; var force = world.Forces[task.ForceId];
        if (attemptExecution)
        {
            world.Replace(actor with { Injury = d.RetaliationInjury });
            world.Replace(force with { BaseTrust = Math.Max(0, force.BaseTrust - d.BaseTrustPenalty) });
        }
        else if (actor.BaseAbilities.Intelligence >= d.TrustRewardIntelligenceThreshold)
            world.Replace(force with { BaseTrust = (int)Math.Min(1000L, (long)force.BaseTrust + d.BaseTrustReward) });
        else OfficerAbilityGrowth.Increase(world, actor.Id, OfficerAbility.Intelligence, d.IntelligenceReward, events);
        EconomicCycle.UpdateTrust(world, rules.Economy, events);
        var after = world.Officers[actor.Id];
        events.Add(new ScholarInsultResolvedEvent(world.Clock, d.Id, force.Id, actor.Id, d.ScholarOfficerId,
            task.Journey!.DestinationPlaceId, attemptExecution, after.BaseAbilities.Intelligence, after.Injury, world.Forces[force.Id].BaseTrust));
    }
}
