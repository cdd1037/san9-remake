using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum SearchConflictChoice { Pay, Execute, Allow, Fight, Persuade, Appease, MediatePeace, Decline }
public sealed record SearchConflictOption(SearchConflictChoice Choice, uint Cost, int? SuccessChance);
public sealed record SearchConflictPrompt(Guid TaskId, int Step, string EventId, int ForceId, int OfficerId,
    int PlaceId, int? OtherOfficerId, ResourceAccount Account, ImmutableArray<SearchConflictOption> Options);
public sealed record SearchConflictResolvedEvent(GameClock Clock, string EventId, int ForceId, int OfficerId,
    int PlaceId, int? OtherOfficerId, SearchConflictChoice Choice, EventResult Result, int? Chance, int? Roll,
    ResourceAccount Account, uint Cost, long MoneyReward, uint Soldiers, int? LearnedTacticId,
    bool FavoredLeader, int Injury, int LifespanTier, int BaseTrust) : DomainEvent;

internal static class SearchConflicts
{
    private static int Ability(OfficerAbilities a, ConflictChanceAbility ability) => ability switch
    {
        ConflictChanceAbility.War => a.War, ConflictChanceAbility.Intelligence => a.Intelligence,
        ConflictChanceAbility.Politics => a.Politics, ConflictChanceAbility.Leadership => a.Leadership,
        _ => throw new ArgumentOutOfRangeException(nameof(ability))
    };
    private static bool BeforeChance(WorldMutation world, PersistentTask task, PersonnelRulebook rules, ConflictSearchEventDefinition d)
    {
        if (task.Journey is null || !world.Forces.TryGetValue(task.ForceId, out var force) || force.IsEliminated ||
            !world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player)) return false;
        if (!d.RequiresOwnedPlace) return true;
        int place = task.Journey.DestinationPlaceId;
        int? owner = world.Settlements.TryGetValue(place, out var settlement) ? settlement.OwnerId :
            rules.RegionCities.TryGetValue(place, out int city) && world.Settlements.TryGetValue(city, out var related) ? related.OwnerId : null;
        return owner == task.ForceId;
    }
    private static bool Living(WorldMutation world, int id) => world.Officers.TryGetValue(id, out var p) && p.Status != OfficerStatus.Dead;
    private static ScholarPunishmentSite? ScholarSite(PersistentTask task, ScholarPunishmentSearchEventDefinition d) =>
        d.Sites.SingleOrDefault(s => s.PlaceId == task.Journey!.DestinationPlaceId);
    private static BanditMediationSite? MediationSite(PersistentTask task, BanditMediationSearchEventDefinition d) =>
        d.Sites.SingleOrDefault(s => s.PlaceId == task.Journey!.DestinationPlaceId);
    private static bool AfterChance(WorldMutation world, PersistentTask task, ConflictSearchEventDefinition d)
    {
        int trust = world.Forces[task.ForceId].BaseTrust;
        if (trust <= d.MinimumBaseTrustExclusive || trust >= d.MaximumBaseTrustExclusive ||
            world.Officers[task.OfficerId].Injury >= d.MaximumInjuryExclusive || !d.Months.IsEmpty && !d.Months.Contains(world.Clock.Month)) return false;
        var account = SearchEventResources.Account(world, task);
        uint money = ResourceAccounts.Balance(world, account, ResourceKind.Money);
        if (d.MinimumMoneyExclusive is uint minimum && money <= minimum ||
            d.RequiresMoneyCapacity && ResourceAccounts.Receivable(world, account, ResourceKind.Money) == 0 || money < Cost(world, task, d)) return false;
        return d switch
        {
            MoneyDemandSearchEventDefinition demand => demand.DemandingOfficerIds.Any(id => Living(world, id)),
            ScholarPunishmentSearchEventDefinition scholar => ScholarSite(task, scholar) is { } site && Living(world, site.ScholarOfficerId) &&
                site.ScholarOfficerId != task.OfficerId && !world.Forces[task.ForceId].EventHistory.Contains(site.HistoryKey),
            BanditMediationSearchEventDefinition mediation => MediationSite(task, mediation) is not null &&
                SoldierTotals.Force(world, task.ForceId) < mediation.MaximumForceSoldiersExclusive,
            _ => true
        };
    }
    internal static bool Prepare(WorldMutation world, PersistentTask task, PersonnelRulebook rules, ConflictSearchEventDefinition d,
        out ConflictSearchEncounter? encounter)
    {
        encounter = null;
        if (!BeforeChance(world, task, rules, d) || world.NextRandom("event-search-eligibility") % 100 >=
            (ulong)(Ability(world.Abilities(world.Officers[task.OfficerId]), d.ChanceAbility) / d.ChanceDivisor) || !AfterChance(world, task, d)) return false;
        int? other = null;
        if (d is MoneyDemandSearchEventDefinition demand)
        {
            var candidates = demand.DemandingOfficerIds.Where(id => Living(world, id)).Order().ToArray();
            other = candidates.Length == 1 ? candidates[0] : candidates[(int)(world.NextRandom("event-search-demanding-officer") % (ulong)candidates.Length)];
        }
        else if (d is ScholarPunishmentSearchEventDefinition scholar) other = ScholarSite(task, scholar)!.ScholarOfficerId;
        encounter = new(d.Id, other);
        return true;
    }
    internal static bool CanResolve(WorldMutation world, PersistentTask task, SimulationRules rules, ConflictSearchEncounter encounter) =>
        rules.Personnel.SearchEvents.GetValueOrDefault(encounter.EventId) is ConflictSearchEventDefinition d &&
        BeforeChance(world, task, rules.Personnel, d) && AfterChance(world, task, d) &&
        (d is MoneyDemandSearchEventDefinition demand ? encounter.OtherOfficerId is int other && demand.DemandingOfficerIds.Contains(other) && Living(world, other) :
         d is ScholarPunishmentSearchEventDefinition scholar ? encounter.OtherOfficerId == ScholarSite(task, scholar)?.ScholarOfficerId : encounter.OtherOfficerId is null);

    internal static uint Cost(WorldMutation world, PersistentTask task, ConflictSearchEventDefinition d)
    {
        var p = world.Officers[task.OfficerId];
        return d switch
        {
            MoneyDemandSearchEventDefinition demand => (uint)Math.Min(uint.MaxValue, (long)demand.CostUnit * Math.Max(0,
                demand.CostSteps - world.Abilities(p).Politics / demand.PoliticsCostDivisor)),
            BanditSuppressionSearchEventDefinition bandit => (uint)Math.Min(uint.MaxValue,
                (long)bandit.AppeasementCostMultiplier * Math.Max(0, bandit.AppeasementPoliticsCeiling - p.BaseAbilities.Politics)),
            _ => 0
        };
    }
    internal static ImmutableArray<SearchConflictOption> Options(WorldMutation world, PersistentTask task, ConflictSearchEventDefinition d)
    {
        var a = world.Abilities(world.Officers[task.OfficerId]);
        var decline = new SearchConflictOption(SearchConflictChoice.Decline, 0, null);
        return d switch
        {
            MoneyDemandSearchEventDefinition => [new(SearchConflictChoice.Pay, Cost(world, task, d), null), new(SearchConflictChoice.Execute, 0, null)],
            ScholarPunishmentSearchEventDefinition => [new(SearchConflictChoice.Allow, 0, null), new(SearchConflictChoice.Execute, 0, null)],
            EvildoerSearchEventDefinition evil => [new(SearchConflictChoice.Fight, 0, Math.Max(evil.MinimumSuccessChance, a.War)),
                new(SearchConflictChoice.Persuade, 0, Math.Max(evil.MinimumSuccessChance, a.Politics)), decline],
            LandSpiritSearchEventDefinition spirit => [new(SearchConflictChoice.Fight, 0, Math.Max(spirit.MinimumSuccessChance, a.War)),
                new(SearchConflictChoice.Persuade, 0, Math.Max(spirit.MinimumSuccessChance, a.Politics)), decline],
            BanditSuppressionSearchEventDefinition bandit => [new(SearchConflictChoice.Fight, 0, Math.Max(bandit.MinimumSuccessChance, a.Leadership)),
                new(SearchConflictChoice.Appease, Cost(world, task, d), null), decline],
            TravelerRescueSearchEventDefinition => [new(SearchConflictChoice.Fight, 0, a.Leadership), decline],
            BanditMediationSearchEventDefinition mediation => [new(SearchConflictChoice.Fight, 0,
                mediation.GuaranteedOfficerIds.Contains(task.OfficerId) ? 100 : Math.Max(mediation.MinimumSuccessChance, a.War)),
                new(SearchConflictChoice.MediatePeace, 0, mediation.GuaranteedOfficerIds.Contains(task.OfficerId) ? 100 : Math.Max(mediation.MinimumSuccessChance, a.War)), decline],
            _ => throw new ArgumentException("Unknown conflict definition.")
        };
    }
    internal static bool IsAcceptance(SearchConflictChoice choice) => choice is not (SearchConflictChoice.Decline or SearchConflictChoice.Execute);
    internal static bool ValidResponse(WorldMutation world, PersistentTask task, ConflictSearchEventDefinition d, SearchConflictChoice choice) =>
        Options(world, task, d).Any(o => o.Choice == choice);

    internal static void Resolve(WorldMutation world, PersistentTask task, SimulationRules rules, ConflictSearchEncounter encounter,
        SearchConflictChoice choice, List<DomainEvent> events)
    {
        if (!CanResolve(world, task, rules, encounter)) throw new InvalidOperationException("Search conflict is no longer valid.");
        var d = (ConflictSearchEventDefinition)rules.Personnel.SearchEvents[encounter.EventId];
        var option = Options(world, task, d).SingleOrDefault(o => o.Choice == choice) ?? throw new ArgumentException("Invalid conflict choice.");
        int? roll = option.SuccessChance is null ? null : (int)(world.NextRandom("event-search-conflict") % 100);
        bool won = option.SuccessChance is int chance ? roll < chance : IsAcceptance(choice);
        bool refused = choice == SearchConflictChoice.Decline;
        var account = SearchEventResources.Account(world, task);
        var actor = world.Officers[task.OfficerId];
        var abilities = world.Abilities(actor);
        int trust = won ? d.TrustReward : -d.TrustPenalty;
        long money = 0; uint soldiers = 0; int? learned = null; bool favored = false;
        if (option.Cost > 0) ResourceAccounts.Change(world, account, ResourceKind.Money, -(long)option.Cost, "search-conflict", actor.Id, events, requireFull: true);
        switch (d)
        {
            case MoneyDemandSearchEventDefinition:
                if (!won) OfficerInjuries.Apply(world, actor with { Injury = d.DefeatInjury }, events);
                break;
            case ScholarPunishmentSearchEventDefinition scholar:
                var site = ScholarSite(task, scholar)!;
                if (won)
                {
                    learned = site.RewardTacticIds.Where(id => !actor.LearnedTactics.Contains(id)).Select(id => (int?)id).FirstOrDefault();
                    if (learned is int tactic) OfficerTacticGrowth.Grant(world, actor.Id, tactic, events);
                }
                else
                {
                    OfficerInjuries.Apply(world, actor with { Injury = d.DefeatInjury, LifespanTier = Math.Min(actor.LifespanTier, scholar.PunishmentLifespanCeiling) }, events);
                    PersonnelWithdrawal.Resolve(world, site.ScholarOfficerId, rules, events);
                    world.ConsumeEvent(new(d.Id, EventTrigger.Search, d.Priority, EventConsumption.OnSuccess, site.WithdrawalKey),
                        new(d.Id, EventOutcomeKind.Completed, false, EventResult.Succeeded));
                }
                var historyForce = world.Forces[task.ForceId];
                world.Replace(historyForce with { EventHistory = historyForce.EventHistory.Add(site.HistoryKey) });
                break;
            case LandSpiritSearchEventDefinition spirit:
                if (won) money = spirit.MoneyReward;
                else if (!refused) OfficerInjuries.Apply(world, actor with { Injury = d.DefeatInjury }, events);
                break;
            case BanditSuppressionSearchEventDefinition bandit:
                if (choice == SearchConflictChoice.Appease) trust = bandit.AppeasementTrustReward;
                else if (!won && !refused) OfficerInjuries.Apply(world, actor with { Injury = d.DefeatInjury }, events);
                break;
            case TravelerRescueSearchEventDefinition rescue:
                if (won) { trust = 0; money = (long)rescue.IntelligenceRewardMultiplier * abilities.Intelligence + rescue.BaseMoneyReward; }
                else if (!refused) OfficerInjuries.Apply(world, actor with { Injury = d.DefeatInjury }, events);
                break;
            case BanditMediationSearchEventDefinition mediation:
                favored = world.Forces[task.ForceId].LeaderId == MediationSite(task, mediation)!.FavoredLeaderId;
                if (won)
                {
                    trust = d.TrustReward;
                    if (favored) money = mediation.FavoredMoneyReward;
                    var home = PersonnelLocations.TaskHome(world, task);
                    soldiers = (uint)Math.Min(home.SoldierCapacity - home.Soldiers,
                        (long)(favored ? mediation.FavoredSoldierMultiplier : mediation.SoldierMultiplier) * abilities.War);
                    world.Replace(home with { Soldiers = home.Soldiers + soldiers });
                }
                else if (!refused) OfficerInjuries.Apply(world, actor with { Injury = d.DefeatInjury }, events);
                break;
        }
        if (money > 0) ResourceAccounts.Change(world, account, ResourceKind.Money, money, "search-conflict", actor.Id, events);
        var force = world.Forces[task.ForceId];
        world.Replace(force with { BaseTrust = (int)Math.Clamp((long)force.BaseTrust + trust, 0, 1000) });
        var after = world.Officers[task.OfficerId];
        events.Add(new SearchConflictResolvedEvent(world.Clock, d.Id, task.ForceId, task.OfficerId, task.Journey!.DestinationPlaceId,
            encounter.OtherOfficerId, choice, refused ? EventResult.Declined : won ? EventResult.Succeeded : EventResult.Defeated,
            option.SuccessChance, roll, account, option.Cost, money, soldiers, learned, favored, after.Injury, after.LifespanTier,
            world.Forces[task.ForceId].BaseTrust));
    }
}
