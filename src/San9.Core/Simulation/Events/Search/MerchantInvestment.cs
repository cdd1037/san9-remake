using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum MerchantInvestmentChoice { Invest, Decline, Execute }
public sealed record MerchantInvestmentResponse([property: JsonRequired] MerchantInvestmentChoice Choice, [property: JsonRequired] uint Amount);
public sealed record MerchantInvestmentPrompt(Guid TaskId, int Step, string EventId, int ForceId, int OfficerId, int PlaceId,
    string CategoryId, ResourceAccount Account, ResourceKind RewardResource, uint MinimumInvestment, uint MaximumInvestment,
    int SuccessChance, int FoodExchangeRate);
public sealed record MerchantInvestmentResolvedEvent(GameClock Clock, string EventId, int ForceId, int OfficerId, string CategoryId,
    MerchantInvestmentChoice Choice, uint Investment, ResourceAccount Account, ResourceKind RewardResource,
    int SuccessChance, int? Roll, bool Succeeded, long GrossReturn, int BaseTrust) : DomainEvent;

internal static class MerchantInvestment
{
    internal static ResourceAccount Account(WorldMutation world, PersistentTask task) => SearchEventResources.Account(world, task);
    internal static MerchantTradeCategory? Category(PersistentTask task, MerchantInvestmentDefinition d) =>
        d.Categories.SingleOrDefault(c => c.PlaceIds.Contains(task.Journey!.DestinationPlaceId));
    private static bool BeforeChance(WorldMutation world, PersistentTask task) => task.Journey is not null &&
        world.Forces.TryGetValue(task.ForceId, out var force) && !force.IsEliminated &&
        world.Simulation.Controls.Any(c => c.ForceId == force.Id && c.Kind == ControllerKind.Player);
    private static bool AfterChance(WorldMutation world, PersistentTask task, MerchantInvestmentDefinition d)
    {
        var account = Account(world, task);
        return world.Forces[task.ForceId].BaseTrust > d.MinimumBaseTrustExclusive &&
            ResourceAccounts.Balance(world, account, ResourceKind.Money) > d.MinimumMoneyExclusive &&
            ResourceAccounts.Receivable(world, account, ResourceKind.Money) > 0 && ResourceAccounts.Receivable(world, account, ResourceKind.Food) > 0 &&
            MaximumInvestment(world, task, d) >= d.MinimumInvestment && Category(task, d) is { } category &&
            !world.Forces[task.ForceId].EventHistory.Contains(category.BanHistoryKey);
    }
    internal static bool Prepare(WorldMutation world, PersistentTask task, MerchantInvestmentDefinition d) =>
        BeforeChance(world, task) && world.NextRandom("event-search-eligibility") % 100 <
            (ulong)(world.Abilities(world.Officers[task.OfficerId]).Politics / d.PoliticsChanceDivisor) && AfterChance(world, task, d);
    internal static bool CanResolve(WorldMutation world, PersistentTask task, SimulationRules rules, MerchantInvestmentEncounter encounter) =>
        rules.Personnel.SearchEvents.GetValueOrDefault(encounter.EventId) is MerchantInvestmentDefinition d && BeforeChance(world, task) && AfterChance(world, task, d);
    internal static uint MaximumInvestment(WorldMutation world, PersistentTask task, MerchantInvestmentDefinition d) =>
        ResourceAccounts.Balance(world, Account(world, task), ResourceKind.Money) / d.InvestmentBalanceDivisor;
    internal static int SuccessChance(WorldMutation world, PersistentTask task, MerchantInvestmentDefinition d) =>
        Math.Max(d.MinimumSuccessChance, world.Abilities(world.Officers[task.OfficerId]).Politics);
    internal static bool ValidResponse(WorldMutation world, PersistentTask task, MerchantInvestmentDefinition d, MerchantInvestmentResponse response) =>
        Enum.IsDefined(response.Choice) && (response.Choice == MerchantInvestmentChoice.Invest
            ? response.Amount >= d.MinimumInvestment && response.Amount <= MaximumInvestment(world, task, d) : response.Amount == 0);

    internal static void Resolve(WorldMutation world, PersistentTask task, SimulationRules rules,
        MerchantInvestmentEncounter encounter, MerchantInvestmentResponse response, List<DomainEvent> events)
    {
        if (!CanResolve(world, task, rules, encounter)) throw new InvalidOperationException("Merchant investment is no longer valid.");
        var d = (MerchantInvestmentDefinition)rules.Personnel.SearchEvents[encounter.EventId];
        if (!ValidResponse(world, task, d, response)) throw new ArgumentException("Invalid merchant investment response.");
        var category = Category(task, d)!; var account = Account(world, task);
        int chance = SuccessChance(world, task, d);
        int? roll = response.Choice == MerchantInvestmentChoice.Invest ? (int)(world.NextRandom("event-merchant-investment") % 100) : null;
        bool won = roll < chance; long gross = 0;
        if (response.Choice == MerchantInvestmentChoice.Invest)
        {
            if (won)
            {
                long basis = (long)response.Amount * d.ReturnNumerator / d.ReturnDenominator +
                    (long)d.PoliticsReturnMultiplier * world.Abilities(world.Officers[task.OfficerId]).Politics;
                gross = (long)Math.Min(long.MaxValue, (decimal)basis * (category.RewardResource == ResourceKind.Food ? world.Simulation.FoodExchangeRate : 1));
            }
            ResourceAccounts.Change(world, account, ResourceKind.Money, -(long)response.Amount, "merchant-investment", task.OfficerId, events, requireFull: true);
            if (won) ResourceAccounts.Change(world, account, category.RewardResource, gross, "merchant-return", task.OfficerId, events);
        }
        else if (response.Choice == MerchantInvestmentChoice.Execute)
        {
            var force = world.Forces[task.ForceId];
            world.Replace(force with { BaseTrust = Math.Max(0, force.BaseTrust - d.ExecutionTrustPenalty),
                EventHistory = force.EventHistory.Append(category.BanHistoryKey).OrderBy(k => k.Value, StringComparer.Ordinal).ToImmutableArray() });
            EconomicCycle.UpdateTrust(world, rules.Economy, events);
        }
        events.Add(new MerchantInvestmentResolvedEvent(world.Clock, d.Id, task.ForceId, task.OfficerId, category.Id,
            response.Choice, response.Amount, account, category.RewardResource, chance, roll, won, gross, world.Forces[task.ForceId].BaseTrust));
    }
}
