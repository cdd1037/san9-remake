using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record MysticBookOfferPrompt(Guid TaskId, int Step, string EventId, int ForceId,
    int SearchingOfficerId, int MysticOfficerId, int PlaceId, int TreasureId, uint AcceptCost, uint DeclineCost, ResourceAccount Account);
public sealed record MysticBookReturnPrompt(Guid TaskId, int Step, string EventId, int ForceId,
    int SearchingOfficerId, int MysticOfficerId, int PlaceId, int TreasureId, int? DeclineRewardTacticId);
public sealed record MysticBookOfferResolvedEvent(GameClock Clock, string EventId, int ForceId,
    int SearchingOfficerId, int MysticOfficerId, int TreasureId, bool Accepted) : DomainEvent;
public sealed record MysticBookReturnResolvedEvent(GameClock Clock, string EventId, int ForceId,
    int SearchingOfficerId, int MysticOfficerId, int TreasureId, bool Accepted, int? LearnedTacticId) : DomainEvent;

internal static class MysticBook
{
    private static bool BeforeChance(WorldMutation world, PersistentTask task, int placeId) =>
        world.Forces.TryGetValue(task.ForceId, out var force) && !force.IsEliminated &&
        world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player) &&
        task.Journey?.DestinationPlaceId == placeId;

    private static bool Special(WorldMutation world, int id) =>
        world.Officers.TryGetValue(id, out var mystic) && mystic.Status == OfficerStatus.Special;
    private static bool Holds(WorldMutation world, int officerId, int treasureId) =>
        world.Treasures.TryGetValue(treasureId, out var item) && item.Discovered && item.HolderId == officerId;
    private static bool Chance(WorldMutation world, PersistentTask task, int divisor) =>
        world.NextRandom("event-search-eligibility") % 100 < (ulong)(world.Abilities(world.Officers[task.OfficerId]).Leadership / divisor);

    private static bool AfterChance(WorldMutation world, PersistentTask task, MysticBookOfferSearchEventDefinition d) =>
        SearchEventResources.HasFunds(world, task, d.MinimumMoneyExclusive, Math.Max(d.AcceptCost, d.DeclineCost)) &&
        Special(world, d.MysticOfficerId) && Holds(world, d.MysticOfficerId, d.TreasureId);
    private static bool AfterChance(WorldMutation world, PersistentTask task, MysticBookReturnSearchEventDefinition d) =>
        Special(world, d.MysticOfficerId) && FirstUnknown(world.Officers[task.OfficerId], d) is not null &&
        Holds(world, task.OfficerId, d.TreasureId);

    internal static int? FirstUnknown(OfficerState actor, MysticBookReturnSearchEventDefinition d) =>
        d.RewardTacticIds.Where(id => !actor.LearnedTactics.Contains(id)).Select(id => (int?)id).FirstOrDefault();

    internal static bool Prepare(WorldMutation world, PersistentTask task, MysticBookOfferSearchEventDefinition d) =>
        BeforeChance(world, task, d.PlaceId) && Chance(world, task, d.LeadershipChanceDivisor) && AfterChance(world, task, d);
    internal static bool Prepare(WorldMutation world, PersistentTask task, MysticBookReturnSearchEventDefinition d) =>
        BeforeChance(world, task, d.PlaceId) && Chance(world, task, d.LeadershipChanceDivisor) && AfterChance(world, task, d);

    internal static bool CanResolve(WorldMutation world, PersistentTask task, SimulationRules rules, MysticBookOfferEncounter encounter) =>
        rules.Personnel.SearchEvents.GetValueOrDefault(encounter.EventId) is MysticBookOfferSearchEventDefinition d &&
        BeforeChance(world, task, d.PlaceId) && AfterChance(world, task, d);
    internal static bool CanResolve(WorldMutation world, PersistentTask task, SimulationRules rules, MysticBookReturnEncounter encounter) =>
        rules.Personnel.SearchEvents.GetValueOrDefault(encounter.EventId) is MysticBookReturnSearchEventDefinition d &&
        BeforeChance(world, task, d.PlaceId) && AfterChance(world, task, d);

    internal static void Resolve(WorldMutation world, PersistentTask task, SimulationRules rules,
        MysticBookOfferEncounter encounter, bool accept, List<DomainEvent> events)
    {
        if (!CanResolve(world, task, rules, encounter)) throw new InvalidOperationException("Mystic book offer is no longer valid.");
        var d = (MysticBookOfferSearchEventDefinition)rules.Personnel.SearchEvents[encounter.EventId];
        if (accept) PersonnelTreasures.Transfer(world, world.Treasures[d.TreasureId], task.OfficerId, task.ForceId, "mystic-book-offer", events);
        ResourceAccounts.Change(world, SearchEventResources.Account(world, task), ResourceKind.Money,
            -(long)(accept ? d.AcceptCost : d.DeclineCost), "mystic-book-offer", task.OfficerId, events, requireFull: true);
        events.Add(new MysticBookOfferResolvedEvent(world.Clock, d.Id, task.ForceId, task.OfficerId, d.MysticOfficerId, d.TreasureId, accept));
    }

    internal static void Resolve(WorldMutation world, PersistentTask task, SimulationRules rules,
        MysticBookReturnEncounter encounter, bool accept, List<DomainEvent> events)
    {
        if (!CanResolve(world, task, rules, encounter)) throw new InvalidOperationException("Mystic book return is no longer valid.");
        var d = (MysticBookReturnSearchEventDefinition)rules.Personnel.SearchEvents[encounter.EventId];
        int? learned = null;
        if (accept)
        {
            PersonnelTreasures.Transfer(world, world.Treasures[d.TreasureId], d.MysticOfficerId, task.ForceId, "mystic-book-return", events);
        }
        else
        {
            learned = FirstUnknown(world.Officers[task.OfficerId], d)!.Value;
            OfficerTacticGrowth.Grant(world, task.OfficerId, learned.Value, events);
        }
        events.Add(new MysticBookReturnResolvedEvent(world.Clock, d.Id, task.ForceId, task.OfficerId, d.MysticOfficerId, d.TreasureId, accept, learned));
    }
}
