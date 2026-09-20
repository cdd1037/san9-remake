using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record LongevityPrompt(Guid TaskId, int Step, string EventId, int ForceId,
    int SearchingOfficerId, int PlaceId, uint Cost, int MinimumLifespanTier);
public sealed record LongevityResolvedEvent(GameClock Clock, string EventId, int ForceId,
    int SearchingOfficerId, int PlaceId, bool Accepted, int PreviousLifespanTier, int LifespanTier) : DomainEvent;

internal static class Longevity
{
    private static bool BeforeChance(WorldMutation world, PersistentTask task, LongevitySearchEventDefinition d) =>
        world.Forces.TryGetValue(task.ForceId, out var force) && !force.IsEliminated &&
        world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player) &&
        force.EventHistory.Contains(d.RequiredHistoryKey) && task.Journey?.DestinationPlaceId == d.PlaceId;

    private static bool AfterChance(WorldMutation world, PersistentTask task, LongevitySearchEventDefinition d) =>
        ResourceAccounts.Balance(world, ResourceAccounts.Resolve(world, task.ForceId,
            world.Settlements[task.OriginSettlementId].CorpsId), ResourceKind.Money) > d.MinimumMoneyExclusive &&
        world.Officers.TryGetValue(d.DivinerOfficerId, out var diviner) && diviner.Status == OfficerStatus.Special &&
        d.LifespanChangesAllowed;

    internal static bool Prepare(WorldMutation world, PersistentTask task, LongevitySearchEventDefinition d) =>
        BeforeChance(world, task, d) &&
        world.NextRandom("event-search-eligibility") % 100 < (ulong)(world.Abilities(world.Officers[task.OfficerId]).Intelligence / d.IntelligenceChanceDivisor) &&
        AfterChance(world, task, d);

    internal static bool CanResolve(WorldMutation world, PersistentTask task, SimulationRules rules, LongevityEncounter encounter) =>
        rules.Personnel.SearchEvents.GetValueOrDefault(encounter.EventId) is LongevitySearchEventDefinition d &&
        BeforeChance(world, task, d) && AfterChance(world, task, d);

    internal static void Resolve(WorldMutation world, PersistentTask task, SimulationRules rules,
        LongevityEncounter encounter, bool accept, List<DomainEvent> events)
    {
        if (!CanResolve(world, task, rules, encounter)) throw new InvalidOperationException("Longevity encounter is no longer valid.");
        var d = (LongevitySearchEventDefinition)rules.Personnel.SearchEvents[encounter.EventId];
        var actor = world.Officers[task.OfficerId];
        int lifespan = accept ? Math.Max(actor.LifespanTier, d.MinimumLifespanTier) : actor.LifespanTier;
        if (accept)
        {
            world.Replace(actor with { LifespanTier = lifespan });
            ResourceAccounts.Change(world, new(ResourceOwnerKind.Force, task.ForceId), ResourceKind.Money,
                -(long)d.Cost, "longevity", actor.Id, events);
        }
        events.Add(new LongevityResolvedEvent(world.Clock, d.Id, task.ForceId, actor.Id, d.PlaceId,
            accept, actor.LifespanTier, lifespan));
    }
}
