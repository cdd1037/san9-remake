using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum CommunitySearchChoice { Accept, DonateMoney, DonateFood, DemandMoney, Decline, Execute }
public sealed record CommunitySearchOption(CommunitySearchChoice Choice, ResourceKind Resource, uint Cost);
public sealed record CommunitySearchPrompt(Guid TaskId, int Step, string EventId, int ForceId, int OfficerId,
    int PlaceId, int Season, ResourceAccount Account, int? RequiredTreasureId, int? TacticCategory,
    ImmutableArray<CommunitySearchOption> Options);
public sealed record CommunitySearchResolvedEvent(GameClock Clock, string EventId, int ForceId, int OfficerId,
    int PlaceId, CommunitySearchChoice Choice, ResourceAccount Account, ResourceKind PaymentResource, uint Cost,
    long MoneyReward, uint Soldiers, int? LearnedTacticId, int BaseTrust) : DomainEvent;

internal static class CommunitySearch
{
    private static int Ability(OfficerAbilities a, CommunityAbility kind) => kind switch
    {
        CommunityAbility.Leadership => a.Leadership, CommunityAbility.War => a.War,
        CommunityAbility.Intelligence => a.Intelligence, CommunityAbility.Politics => a.Politics,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    private static bool Owned(WorldMutation world, PersistentTask task, PersonnelRulebook rules)
    {
        int place = task.Journey!.DestinationPlaceId;
        return (world.Settlements.TryGetValue(place, out var s) ? s.OwnerId :
            rules.RegionCities.TryGetValue(place, out int city) && world.Settlements.TryGetValue(city, out var related) ? related.OwnerId : null) == task.ForceId;
    }
    private static bool BeforeChance(WorldMutation world, PersistentTask task, PersonnelRulebook rules, CommunitySearchEventDefinition d) =>
        task.Journey is not null && world.Forces.TryGetValue(task.ForceId, out var force) && !force.IsEliminated &&
        world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player) &&
        (d is not CivicSearchEventDefinition { Activity: not CivicSearchKind.TravelPermit } || Owned(world, task, rules));
    internal static CommemorationSite? Site(PersistentTask task, CommemorationSearchEventDefinition d) =>
        d.Sites.SingleOrDefault(s => s.PlaceId == task.Journey!.DestinationPlaceId);
    internal static TrainingSearchSite? Site(PersistentTask task, TrainingSiteSearchEventDefinition d) =>
        d.Sites.SingleOrDefault(s => s.PlaceId == task.Journey!.DestinationPlaceId);
    private static bool AfterChance(WorldMutation world, PersistentTask task, PersonnelRulebook rules, CommunitySearchEventDefinition d)
    {
        int trust = world.Forces[task.ForceId].BaseTrust;
        var account = SearchEventResources.Account(world, task);
        if (trust <= d.MinimumTrustExclusive || trust >= d.MaximumTrustExclusive ||
            !d.Months.IsEmpty && !d.Months.Contains(world.Clock.Month) ||
            d.MinimumMoneyExclusive is uint minimum && ResourceAccounts.Balance(world, account, ResourceKind.Money) <= minimum) return false;
        bool eligible = d switch
        {
            CivicSearchEventDefinition { Activity: CivicSearchKind.DisasterRelief } civic => ResourceAccounts.Balance(world, account, ResourceKind.Food) > civic.MinimumFoodExclusive,
            CivicSearchEventDefinition { Activity: CivicSearchKind.Volunteers } civic => SoldierTotals.Force(world, task.ForceId) < civic.MaximumForceSoldiersExclusive,
            CivicSearchEventDefinition { Activity: CivicSearchKind.TravelPermit } => Owned(world, task, rules),
            CivicSearchEventDefinition => true,
            CommemorationSearchEventDefinition memorial => Site(task, memorial) is { } site && !world.Simulation.ConsumedEventKeys.Contains(site.ConsumptionKey) &&
                (site.RequiredTreasureId is not int itemId || world.Treasures.TryGetValue(itemId, out var item) && item.Discovered && item.HolderId == task.OfficerId),
            TrainingSiteSearchEventDefinition training => Site(task, training) is { } site &&
                world.Officers[task.OfficerId].TacticProficiencies[site.Category] < training.MaximumProficiencyExclusive &&
                SoldierTotals.Force(world, task.ForceId) > training.MinimumForceSoldiersExclusive,
            _ => false
        };
        return eligible && Options(world, task, d).All(o => ResourceAccounts.Balance(world, account, o.Resource) >= o.Cost);
    }
    internal static bool Prepare(WorldMutation world, PersistentTask task, PersonnelRulebook rules, CommunitySearchEventDefinition d)
    {
        if (!BeforeChance(world, task, rules, d)) return false;
        if (d.ChanceDivisor is int divisor && world.NextRandom("event-search-eligibility") % 100 >=
            (ulong)(Ability(world.Abilities(world.Officers[task.OfficerId]), d.ChanceAbility) / divisor)) return false;
        return AfterChance(world, task, rules, d);
    }
    internal static bool CanResolve(WorldMutation world, PersistentTask task, SimulationRules rules, CommunitySearchEncounter encounter) =>
        rules.Personnel.SearchEvents.GetValueOrDefault(encounter.EventId) is CommunitySearchEventDefinition d &&
        BeforeChance(world, task, rules.Personnel, d) && AfterChance(world, task, rules.Personnel, d);
    internal static ImmutableArray<CommunitySearchOption> Options(WorldMutation world, PersistentTask task, CommunitySearchEventDefinition d)
    {
        var accept = new CommunitySearchOption(CommunitySearchChoice.Accept, ResourceKind.Money, 0);
        var decline = new CommunitySearchOption(CommunitySearchChoice.Decline, ResourceKind.Money, 0);
        var demand = new CommunitySearchOption(CommunitySearchChoice.DemandMoney, ResourceKind.Money, 0);
        var a = world.Officers[task.OfficerId].BaseAbilities;
        return d switch
        {
            CivicSearchEventDefinition { Activity: CivicSearchKind.DisasterRelief } civic =>
                [new(CommunitySearchChoice.DonateMoney, ResourceKind.Money, Cost(civic.MoneyCostMultiplier, civic.CostAbilityCeiling, a.Politics)),
                 new(CommunitySearchChoice.DonateFood, ResourceKind.Food, Cost(civic.FoodCostMultiplier, civic.CostAbilityCeiling, a.Intelligence)), decline],
            CivicSearchEventDefinition { Activity: CivicSearchKind.Bribe or CivicSearchKind.TravelPermit } => [accept, demand, decline],
            CivicSearchEventDefinition { Activity: CivicSearchKind.Festival } => [accept, decline, new(CommunitySearchChoice.Execute, ResourceKind.Money, 0)],
            CivicSearchEventDefinition => [accept, decline],
            CommemorationSearchEventDefinition memorial => [accept with { Cost = memorial.Cost }, decline],
            TrainingSiteSearchEventDefinition training => [accept with { Cost = training.Cost }, decline],
            _ => throw new ArgumentException("Unknown community search.")
        };
    }
    private static uint Cost(uint multiplier, int ceiling, int ability) => (uint)Math.Min(uint.MaxValue, (long)multiplier * Math.Max(0L, (long)ceiling - ability));
    internal static bool IsAcceptance(CommunitySearchChoice choice) => choice is not (CommunitySearchChoice.Decline or CommunitySearchChoice.Execute);
    internal static bool ValidResponse(WorldMutation world, PersistentTask task, CommunitySearchEventDefinition d, CommunitySearchChoice choice) =>
        Enum.IsDefined(choice) && Options(world, task, d).Any(o => o.Choice == choice);

    internal static void Resolve(WorldMutation world, PersistentTask task, SimulationRules rules, CommunitySearchEncounter encounter,
        CommunitySearchChoice choice, List<DomainEvent> events)
    {
        if (!CanResolve(world, task, rules, encounter)) throw new InvalidOperationException("Community search is no longer valid.");
        var d = (CommunitySearchEventDefinition)rules.Personnel.SearchEvents[encounter.EventId];
        var option = Options(world, task, d).Single(o => o.Choice == choice);
        var account = SearchEventResources.Account(world, task);
        var a = world.Abilities(world.Officers[task.OfficerId]);
        if (option.Cost > 0) ResourceAccounts.Change(world, account, option.Resource, -(long)option.Cost, "community-search", task.OfficerId, events, requireFull: true);
        int trust = 0; long money = 0; uint soldiers = 0; int? learned = null;
        switch (d)
        {
            case CivicSearchEventDefinition civic:
                switch (civic.Activity)
                {
                    case CivicSearchKind.DisasterRelief:
                        trust = choice == CommunitySearchChoice.Decline ? -civic.TrustPenalty : civic.TrustReward;
                        break;
                    case CivicSearchKind.Volunteers:
                        if (choice == CommunitySearchChoice.Accept)
                        {
                            var home = world.Settlements[task.OriginSettlementId];
                            soldiers = (uint)Math.Min((long)home.SoldierCapacity - home.Soldiers, (long)civic.SoldierMultiplier * a.Leadership);
                            world.Replace(home with { Soldiers = home.Soldiers + soldiers });
                        }
                        break;
                    case CivicSearchKind.Bribe:
                        trust = choice == CommunitySearchChoice.Decline ? civic.TrustReward : -civic.TrustPenalty;
                        if (choice != CommunitySearchChoice.Decline) money = civic.BaseMoneyReward +
                            (choice == CommunitySearchChoice.DemandMoney ? (long)civic.MoneyAbilityMultiplier * a.Politics : 0);
                        break;
                    case CivicSearchKind.TravelPermit:
                        trust = choice == CommunitySearchChoice.Accept ? civic.TrustReward : -civic.TrustPenalty;
                        if (choice == CommunitySearchChoice.DemandMoney) money = civic.BaseMoneyReward + (long)civic.MoneyAbilityMultiplier * a.Politics;
                        break;
                    case CivicSearchKind.Festival:
                        trust = choice == CommunitySearchChoice.Accept ? civic.TrustReward : choice == CommunitySearchChoice.Execute ? -civic.ExecutionTrustPenalty : 0;
                        break;
                }
                break;
            case CommemorationSearchEventDefinition memorial when choice == CommunitySearchChoice.Accept:
                var site = Site(task, memorial)!;
                trust = memorial.TrustReward;
                learned = site.RewardTacticIds.Where(id => !world.Officers[task.OfficerId].LearnedTactics.Contains(id)).Select(id => (int?)id).FirstOrDefault();
                if (learned is int tactic) OfficerTacticGrowth.Grant(world, task.OfficerId, tactic, events);
                var ability = site.RewardAbility switch
                {
                    CommunityAbility.Leadership => OfficerAbility.Leadership, CommunityAbility.War => OfficerAbility.War,
                    CommunityAbility.Intelligence => OfficerAbility.Intelligence, CommunityAbility.Politics => OfficerAbility.Politics,
                    _ => throw new InvalidOperationException()
                };
                OfficerAbilityGrowth.Increase(world, task.OfficerId, ability, memorial.AbilityReward, events);
                world.ConsumeEvent(d.Candidate(site.ConsumptionKey), new(d.Id, EventOutcomeKind.Completed, false, EventResult.Succeeded));
                break;
            case TrainingSiteSearchEventDefinition training when choice == CommunitySearchChoice.Accept:
                OfficerTacticGrowth.IncreaseRaw(world, task.OfficerId, Site(task, training)!.Category, a.Leadership / training.LeadershipRewardDivisor, events);
                break;
        }
        if (money > 0)
        {
            uint before = ResourceAccounts.Balance(world, account, ResourceKind.Money);
            money = (long)ResourceAccounts.Change(world, account, ResourceKind.Money, money, "community-search", task.OfficerId, events) - before;
        }
        if (trust != 0)
        {
            var force = world.Forces[task.ForceId];
            world.Replace(force with { BaseTrust = (int)Math.Clamp((long)force.BaseTrust + trust, 0, 1000) });
        }
        events.Add(new CommunitySearchResolvedEvent(world.Clock, d.Id, task.ForceId, task.OfficerId, task.Journey!.DestinationPlaceId,
            choice, account, option.Resource, option.Cost, money, soldiers, learned, world.Forces[task.ForceId].BaseTrust));
    }
}
