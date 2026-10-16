using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

public enum OriginalAiWorkEnd { LaterStage, Permission, Calendar, NoCandidates, PreparationFailed, ReserveReached, AttemptsExhausted, ProbabilityFailed, AnalysisIncomplete }
public sealed record OriginalAiWorkReport(int CorpsId, OriginalAiWork Work, OriginalAiWorkEnd End, int Attempts, int Submitted)
{
    public long InputRevision { get; init; }
    public long ElapsedTimestampTicks { get; init; }
    public long AllocatedBytes { get; init; }
    public int EmptyPlans { get; init; }
    public int Rejected { get; init; }
    public int PathQueries { get; init; }
}
internal sealed record OriginalFoodTradePlan(FoodTradeDirection Direction, uint FoodAmount);

internal static class OriginalAiEconomy
{
    internal static OriginalFoodTradePlan? TradeAmount(OriginalDomesticInputs input, int rate, uint buyLimit, uint sellLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rate, 1);
        long money = input.Money, food = input.Food, cashReserve = input.SalaryDemand + 2000,
            foodReserve = 3 * input.RationDemand / 2 + 10000;
        long quantity;
        if (food < foodReserve && money > cashReserve)
        {
            quantity = Math.Min(rate * (money - cashReserve), buyLimit) / 500 * 500;
            if (quantity >= 3000 && (money - quantity / rate > cashReserve || food + quantity < foodReserve))
                return new(FoodTradeDirection.Buy, (uint)quantity);
        }
        else if (money < cashReserve && food > foodReserve)
        {
            quantity = Math.Min(food - foodReserve, sellLimit) / 500 * 500;
            if (quantity >= 3000 && (money + quantity / rate < cashReserve || food - quantity > foodReserve))
                return new(FoodTradeDirection.Sell, (uint)quantity);
        }
        if (food == 0 && money >= 100)
        {
            quantity = (long)((Int128)money * input.RationDemand / Math.Max(1, input.SalaryDemand + input.RationDemand / rate));
            quantity = Math.Min(quantity, buyLimit);
            if (quantity > 0) return new(FoodTradeDirection.Buy, (uint)quantity);
        }
        if (money == 0 && food >= 100)
        {
            quantity = (long)((Int128)input.SalaryDemand * food / Math.Max(1, input.SalaryDemand + input.RationDemand / rate));
            quantity = Math.Min(quantity, sellLimit);
            if (quantity > 0) return new(FoodTradeDirection.Sell, (uint)quantity);
        }
        return null;
    }

    internal static OriginalAiWorkReport Trade(int corpsId, int attempts, Func<OriginalDomesticInputs> capture,
        Func<WorldSnapshot> snapshot, Func<int, ImmutableArray<int>, FoodTradeDirection, FoodTradeQuote> quote,
        Func<int, ImmutableArray<int>, OriginalFoodTradePlan, bool> submit)
    {
        var initial = capture();
        var world = snapshot();
        var cities = world.Settlements.ToDictionary(s => s.Id);
        var sources = initial.Sites.Where(s => s.IsCity && cities[s.Settlement.Id].MerchantPresent && !cities[s.Settlement.Id].FoodTradeUsed &&
                !s.Settlement.OfficerIds.IsEmpty).OrderBy(s => s.HasPriorityRelation).Select(s => s.Settlement.Id).ToArray();
        int used = 0, submitted = 0, empty = 0, rejected = 0;
        OriginalAiWorkReport Report(OriginalAiWorkEnd end) => new(corpsId, OriginalAiWork.Trade, end, used, submitted) { EmptyPlans = empty, Rejected = rejected };
        foreach (int id in sources)
        {
            if (used == attempts) return Report(OriginalAiWorkEnd.AttemptsExhausted);
            used++; // No trade and public refusal both consume this city and this attempt.
            var current = capture();
            var site = current.Sites.FirstOrDefault(s => s.Settlement.Id == id);
            if (site is null) { empty++; continue; }
            var people = site.Settlement.OfficerIds.Where(p => current.Abilities[p].Politics >= 35)
                .OrderByDescending(p => OriginalDomesticWork.Adjusted(current.Abilities[p], DomesticActivity.Commerce)).Take(5).ToImmutableArray();
            if (people.IsEmpty) { empty++; continue; }
            var buy = quote(id, people, FoodTradeDirection.Buy);
            var sell = quote(id, people, FoodTradeDirection.Sell);
            var plan = TradeAmount(current, snapshot().Simulation.FoodExchangeRate,
                buy.Available ? buy.MaximumFoodAmount : 0, sell.Available ? sell.MaximumFoodAmount : 0);
            if (plan is null) empty++;
            else if (submit(id, people, plan)) submitted++;
            else rejected++;
        }
        return Report(OriginalAiWorkEnd.NoCandidates);
    }

    internal static OriginalAiWorkReport Residual(int corpsId, bool barbarian, int attempts,
        Func<OriginalDomesticInputs> capture, Func<WorldSnapshot> snapshot,
        Func<DevelopmentCommand, bool> train, Func<int, int, bool> search)
    {
        var sources = capture().Sites.Where(s => !s.Settlement.OfficerIds.IsEmpty).Select(s => s.Settlement.Id).ToArray();
        int used = 0, submitted = 0, empty = 0, rejected = 0;
        OriginalAiWorkReport Report(OriginalAiWorkEnd end) => new(corpsId, OriginalAiWork.Residual, end, used, submitted) { EmptyPlans = empty, Rejected = rejected };
        foreach (int source in sources)
        {
            if (used >= attempts) return Report(OriginalAiWorkEnd.AttemptsExhausted);
            var current = capture();
            var site = current.Sites.FirstOrDefault(s => s.Settlement.Id == source);
            if (site is null || site.Settlement.OfficerIds.IsEmpty)
                return Report(OriginalAiWorkEnd.PreparationFailed);
            var city = site.Settlement;
            bool canTrain = !city.UnavailableActivities.Contains(DomesticActivity.Train) && city.Soldiers > 0 && city.Morale < 100;
            var people = city.OfficerIds.OrderByDescending(id => current.Abilities[id].War).ToArray();
            int present = snapshot().Officers.Count(o => !o.IsDormant && o.OwnerId == current.ForceId && o.IsServing && o.UnitId is null && o.SettlementId == source);
            if (people.Length == present && !canTrain)
                people = people.OrderByDescending(id => OriginalDomesticWork.Adjusted(current.Abilities[id], DomesticActivity.Recruit)).Skip(1).ToArray();
            if (people.Length == 0)
                return Report(OriginalAiWorkEnd.PreparationFailed);
            int cursor = 0;
            while (cursor < people.Length)
            {
                if (used >= attempts) return Report(OriginalAiWorkEnd.AttemptsExhausted);
                used++;
                var refreshed = capture().Sites.FirstOrDefault(s => s.Settlement.Id == source);
                if (refreshed is not null && !refreshed.Settlement.UnavailableActivities.Contains(DomesticActivity.Train) &&
                    refreshed.Settlement.Soldiers > 0 && refreshed.Settlement.Morale < 100)
                {
                    var crew = people.Skip(cursor).Take(5).ToImmutableArray();
                    cursor += crew.Length;
                    if (train(new(current.ForceId, source, crew, DomesticActivity.Train))) submitted++;
                    else rejected++;
                }
                else if (barbarian) { cursor = people.Length; empty++; }
                else
                {
                    int id = people[cursor++];
                    if (search(id, source)) submitted++;
                    else rejected++;
                }
            }
        }
        return Report(OriginalAiWorkEnd.NoCandidates);
    }
}
