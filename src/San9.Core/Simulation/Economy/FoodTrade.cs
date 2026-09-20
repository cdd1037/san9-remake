using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum FoodTradeDirection { Buy, Sell }
public enum FoodTradeRejection { None, InvalidDirection, InvalidParticipants, UnknownObject, WrongOwner, InvalidCity, OfficerUnavailable, MerchantAbsent, AlreadyUsed, NoCapacity }
public sealed record FoodTradeCommand(Guid CommandId, int IssuerId, long Sequence, int SettlementId,
    ImmutableArray<int> OfficerIds, FoodTradeDirection Direction, uint FoodAmount) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record FoodTradeQuote(FoodTradeRejection Rejection, int FoodExchangeRate, ResourceAccount? Account, uint MaximumFoodAmount)
{
    public bool Available => Rejection == FoodTradeRejection.None;
}
public sealed record FoodTradeCompletedEvent(GameClock Clock, int ForceId, int SettlementId, ImmutableArray<int> OfficerIds,
    FoodTradeDirection Direction, ResourceAccount Account, uint FoodAmount, uint MoneyAmount, int FoodExchangeRate) : DomainEvent;

internal static class FoodTrade
{
    internal static FoodTradeQuote Quote(WorldMutation world, int forceId, int settlementId, ImmutableArray<int> officerIds, FoodTradeDirection direction)
    {
        int price = world.Simulation.FoodExchangeRate;
        FoodTradeQuote Reject(FoodTradeRejection reason) => new(reason, price, null, 0);
        if (!Enum.IsDefined(direction)) return Reject(FoodTradeRejection.InvalidDirection);
        if (officerIds.IsDefaultOrEmpty || officerIds.Length > 5 || officerIds.Distinct().Count() != officerIds.Length)
            return Reject(FoodTradeRejection.InvalidParticipants);
        if (!world.Forces.TryGetValue(forceId, out var force) || force.IsEliminated || !world.Settlements.TryGetValue(settlementId, out var city) ||
            officerIds.Any(id => !world.Officers.ContainsKey(id))) return Reject(FoodTradeRejection.UnknownObject);
        var people = officerIds.Order().Select(id => world.Officers[id]).ToArray();
        if (city.OwnerId != forceId || people.Any(p => p.OwnerId != forceId)) return Reject(FoodTradeRejection.WrongOwner);
        if (city.Kind != SettlementKind.City || city.IsDestroyed) return Reject(FoodTradeRejection.InvalidCity);
        if (people.Any(p => !p.IsServing || p.Acted || p.UnitId is not null || p.SettlementId != city.Id || world.Simulation.Tasks.Any(t => t.OfficerId == p.Id)))
            return Reject(FoodTradeRejection.OfficerUnavailable);
        if (!city.MerchantPresent) return Reject(FoodTradeRejection.MerchantAbsent);
        if (city.FoodTradeUsed) return Reject(FoodTradeRejection.AlreadyUsed);
        var account = ResourceAccounts.Resolve(world, forceId, city.CorpsId);
        long money = ResourceAccounts.Balance(world, account, ResourceKind.Money);
        long food = ResourceAccounts.Balance(world, account, ResourceKind.Food);
        long limit = 100 * people.Sum(p => EconomyMath.ExecutedAbility(world.Abilities(p).Politics));
        limit = direction == FoodTradeDirection.Buy
            ? Math.Min(limit, Math.Min(price * money, ResourceAccounts.Receivable(world, account, ResourceKind.Food)))
            : food / price == 0 ? 0 : Math.Min(limit, Math.Min(food, (long)price * ResourceAccounts.Receivable(world, account, ResourceKind.Money)));
        return new(limit > 0 ? FoodTradeRejection.None : FoodTradeRejection.NoCapacity, price, account, (uint)limit);
    }

    internal static bool Apply(WorldMutation world, FoodTradeCommand command, List<DomainEvent> events)
    {
        var quote = Quote(world, command.IssuerId, command.SettlementId, command.OfficerIds, command.Direction);
        if (!quote.Available || command.FoodAmount == 0 || command.FoodAmount > quote.MaximumFoodAmount) return false;
        bool buy = command.Direction == FoodTradeDirection.Buy;
        uint money = (uint)(buy ? ((long)command.FoodAmount + quote.FoodExchangeRate - 1) / quote.FoodExchangeRate : command.FoodAmount / quote.FoodExchangeRate);
        string reason = buy ? "buy-food" : "sell-food";
        ResourceAccounts.Change(world, quote.Account!, ResourceKind.Money, buy ? -(long)money : money, reason, command.SettlementId, events, true);
        ResourceAccounts.Change(world, quote.Account!, ResourceKind.Food, buy ? command.FoodAmount : -(long)command.FoodAmount, reason, command.SettlementId, events, true);
        world.Replace(world.Settlements[command.SettlementId] with { FoodTradeUsed = true });
        foreach (int id in command.OfficerIds) world.Replace(world.Officers[id] with { Acted = true });
        events.Add(new FoodTradeCompletedEvent(world.Clock, command.IssuerId, command.SettlementId, command.OfficerIds.Order().ToImmutableArray(),
            command.Direction, quote.Account!, command.FoodAmount, money, quote.FoodExchangeRate));
        return true;
    }
}
