using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

public sealed record AiPolicyConfiguration(string Version, long WorkBudget, int MaximumPurchases)
{
    public static AiPolicyConfiguration Standard { get; } = new("economic-v1", 4096, 8);
}

public sealed record FoodSecurityGoal(int ForceId, string ControllerId, int AccountId, GameClock Started);

/// <summary>Only durable intentions and reproducible policy inputs belong to the session.</summary>
public sealed record AiSessionState
{
    [JsonRequired] public AiPolicyConfiguration Configuration { get; init; } = AiPolicyConfiguration.Standard;
    [JsonRequired] public ImmutableArray<FoodSecurityGoal> FoodSecurity { get; init; } = [];

    internal void Validate(WorldSnapshot world)
    {
        if (Configuration is null || Configuration.Version != "economic-v1" || Configuration.WorkBudget is < 1 or > 1000000 ||
            Configuration.MaximumPurchases is < 1 or > 256 || FoodSecurity.IsDefault || FoodSecurity.Any(g => g is null) ||
            FoodSecurity.DistinctBy(g => (g.ControllerId, g.AccountId)).Count() != FoodSecurity.Length ||
            FoodSecurity.Any(g => string.IsNullOrWhiteSpace(g.ControllerId) || g.Started.DayNumber < world.Simulation.Started.DayNumber ||
                g.Started.DayNumber > world.Clock.DayNumber || !Authorized(world, g)))
            throw new ArgumentException("Invalid AI policy state.");
    }

    internal AiSessionState Prune(WorldSnapshot world) => this with
    {
        FoodSecurity = FoodSecurity.Where(g => Authorized(world, g)).ToImmutableArray()
    };

    private static bool Authorized(WorldSnapshot world, FoodSecurityGoal goal)
    {
        var seat = world.Simulation.Controls.FirstOrDefault(s => s.ForceId == goal.ForceId);
        return seat is not null && world.Corps.Any(c => c.Id == goal.AccountId && c.OwnerId == goal.ForceId) &&
            (seat.Kind == ControllerKind.Automatic || goal.ControllerId.StartsWith("corps:", StringComparison.Ordinal)) &&
            world.Settlements.Any(s => !s.IsDormant && !s.IsDestroyed &&
                (s.CorpsId ?? world.Corps.Single(c => c.OwnerId == goal.ForceId && c.IsRoot).Id) == goal.AccountId &&
                CorpsControl.CanControlSettlement(world, seat, new(goal.ControllerId), s.Id));
    }
}

public sealed record EconomicAccountObservation(int Id, long Money, long Food, long RequiredFood);
public sealed record FoodPurchaseObservation(int SettlementId, int AccountId, ImmutableArray<int> OfficerIds, int ExchangeRate, uint Maximum);
public sealed record EconomicObservation(DomesticObservation Domestic, ImmutableArray<EconomicAccountObservation> Accounts,
    ImmutableArray<FoodPurchaseObservation> Purchases, int Turn)
{
    public long ProjectionWork { get; init; }
    public bool Complete { get; init; } = true;

    internal static EconomicObservation Capture(SimulationEngine engine, ControlSeat seat, CommandContext context, long budget)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        var world = engine.Snapshot;
        var domestic = DomesticObservation.Capture(world, seat, context);
        var accounts = ImmutableArray.CreateBuilder<EconomicAccountObservation>();
        var purchases = ImmutableArray.CreateBuilder<FoodPurchaseObservation>();
        long work = 0;
        bool Spend() { if (work >= budget) return false; work++; return true; }
        EconomicObservation Result(bool complete) => new(domestic, accounts.ToImmutable(), purchases.ToImmutable(),
            checked((int)((world.Clock.DayNumber - world.Simulation.Started.DayNumber) / 10)))
            { ProjectionWork = work, Complete = complete };
        foreach (int id in domestic.Settlements.Select(s => s.AccountId).Distinct().Order())
        {
            if (!Spend()) return Result(false);
            // Food projection uses shared garrison, field and in-transit consumption rules.
            var food = FoodSupplyForecast.Evaluate(FoodSupplyForecast.Capture(world, seat.ForceId, id));
            var treasury = world.Corps.Single(c => c.Id == id).Treasury;
            accounts.Add(new(id, treasury.Money, food.Balance, food.Expenditure));
        }
        foreach (var settlement in domestic.Settlements)
        {
            if (!Spend()) return Result(false);
            var account = accounts.Single(a => a.Id == settlement.AccountId);
            if (account.Food >= account.RequiredFood) continue;
            List<FoodPurchaseObservation> singles = [];
            foreach (int id in settlement.OfficerIds)
            {
                if (!Spend()) return Result(false);
                var quote = engine.GetFoodTradeQuote(seat.ForceId, settlement.Id, [id], FoodTradeDirection.Buy);
                if (!quote.Available) continue;
                var candidate = new FoodPurchaseObservation(settlement.Id, settlement.AccountId, [id], quote.FoodExchangeRate, quote.MaximumFoodAmount);
                singles.Add(candidate);
                purchases.Add(candidate);
            }
            var officers = singles.OrderByDescending(p => p.Maximum).ThenBy(p => p.OfficerIds[0])
                .Take(5).Select(p => p.OfficerIds[0]).ToImmutableArray();
            for (int count = 2; count <= officers.Length; count++)
            {
                if (!Spend()) return Result(false);
                var participants = officers.Take(count).ToImmutableArray();
                var quote = engine.GetFoodTradeQuote(seat.ForceId, settlement.Id, participants, FoodTradeDirection.Buy);
                if (quote.Available) purchases.Add(new(settlement.Id, settlement.AccountId, participants, quote.FoodExchangeRate, quote.MaximumFoodAmount));
            }
        }
        return Result(true);
    }
}

public abstract record EconomicIntent(int AccountId, int SettlementId, ImmutableArray<int> OfficerIds);
public sealed record PurchaseFoodIntent(int AccountId, int SettlementId, ImmutableArray<int> Participants, uint Amount)
    : EconomicIntent(AccountId, SettlementId, Participants);
public sealed record DevelopIntent(int AccountId, DevelopmentCommand Development)
    : EconomicIntent(AccountId, Development.SettlementId, Development.OfficerIds);
public sealed record EconomicDecision(EconomicIntent? Intent, ImmutableArray<FoodSecurityGoal> Goals, long Work, bool Exhausted);

/// <summary>One account ledger and one person allocation over authorized values. No engine callbacks.</summary>
public static class EconomicPlanner
{
    public static EconomicDecision Decide(EconomicObservation observation, ImmutableArray<FoodSecurityGoal> memory,
        string controllerId, long remainingWork, bool allowDevelopment,
        IReadOnlySet<string>? rejected = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(remainingWork);
        var domestic = observation.Domestic;
        var goals = observation.Accounts.Where(a => a.Food < a.RequiredFood).Select(a =>
            memory.FirstOrDefault(g => g.AccountId == a.Id && g.ControllerId == controllerId) ??
            new FoodSecurityGoal(domestic.Perspective.ObserverId, controllerId, a.Id, domestic.Perspective.Clock)).ToImmutableArray();
        long work = 0;
        bool Spend() { if (work >= remainingWork) return false; work++; return true; }
        EconomicDecision Result(EconomicIntent? intent, bool exhausted = false) => new(intent, goals, work, exhausted);
        // Ledger is keyed by actual account, never by city balances duplicated across the same corps.
        var ledger = observation.Accounts.ToDictionary(a => a.Id, a => a.Money);
        foreach (var account in observation.Accounts.Where(a => a.Food < a.RequiredFood)
            .OrderByDescending(a => a.RequiredFood - a.Food)
            .ThenBy(a => goals.Single(g => g.AccountId == a.Id).Started.DayNumber).ThenBy(a => a.Id))
        {
            if (!Spend()) return Result(null, true);
            foreach (var purchase in observation.Purchases.Where(p => p.AccountId == account.Id)
                .OrderByDescending(p => Math.Min(account.RequiredFood - account.Food, p.Maximum))
                .ThenBy(p => p.OfficerIds.Length).ThenBy(p => p.SettlementId).ThenBy(p => p.OfficerIds[0]))
            {
                if (!Spend()) return Result(null, true);
                long amount = Math.Min(account.RequiredFood - account.Food, purchase.Maximum);
                if (amount <= 0 || purchase.ExchangeRate <= 0) continue;
                long cost = (amount + purchase.ExchangeRate - 1) / purchase.ExchangeRate;
                if (cost > ledger[account.Id]) continue;
                ledger[account.Id] -= cost;
                return Result(new PurchaseFoodIntent(account.Id, purchase.SettlementId, purchase.OfficerIds, (uint)amount));
            }
            // Preserve scarce money for supply. No recruitment while the account cannot sustain its army.
            ledger[account.Id] = 0;
        }
        if (!observation.Complete || !allowDevelopment || remainingWork - work <= 1)
            return Result(null, !observation.Complete || remainingWork - work <= 1);
        var funded = domestic with { Settlements = domestic.Settlements.Select(s => s with { Money = (uint)ledger[s.AccountId] }).ToImmutableArray() };
        // Leave capacity to inspect at least one generated candidate before the budget is exhausted.
        var plan = DomesticPlanner.Plan(funded, new(remainingWork - work - 1, 0), observation.Turn);
        work += plan.Evaluations;
        foreach (var candidate in plan.Candidates)
        {
            if (!Spend()) return Result(null, true);
            if (rejected?.Contains(CanonicalJson.Hash(candidate)) == true) continue;
            var account = observation.Accounts.Single(a => a.Id == funded.Settlements.Single(s => s.Id == candidate.SettlementId).AccountId);
            if (goals.Any(g => g.AccountId == account.Id) && candidate.Activity != DomesticActivity.Train) continue;
            return Result(new DevelopIntent(account.Id, candidate), plan.BudgetExhausted);
        }
        return Result(null, plan.BudgetExhausted);
    }
}

internal static class EconomicTurnRunner
{
    internal static AiSessionState Run(SimulationEngine engine, ControlSeat seat, CommandContext context,
        AiSessionState state, Func<string, Guid> identity, List<DomainEvent> events)
    {
        long remaining = state.Configuration.WorkBudget;
        int purchases = 0;
        bool developed = false;
        HashSet<string> rejected = [];
        while (remaining > 0)
        {
            var observation = EconomicObservation.Capture(engine, seat, context, remaining);
            remaining -= observation.ProjectionWork;
            var decision = EconomicPlanner.Decide(observation, state.FoodSecurity, context.ControllerId,
                remaining, !developed, rejected);
            remaining -= decision.Work;
            state = state with { FoodSecurity = state.FoodSecurity.Where(g => g.ControllerId != context.ControllerId ||
                    !observation.Accounts.Any(a => a.Id == g.AccountId))
                .Concat(decision.Goals).OrderBy(g => g.ForceId).ThenBy(g => g.ControllerId, StringComparer.Ordinal).ThenBy(g => g.AccountId).ToImmutableArray() };
            if (decision.Intent is null) break;
            GameCommand command = decision.Intent switch
            {
                PurchaseFoodIntent food => new FoodTradeCommand(identity("supply"), seat.ForceId,
                    engine.Snapshot.Simulation.NextSequence, food.SettlementId, food.OfficerIds, FoodTradeDirection.Buy, food.Amount),
                DevelopIntent develop => new DevelopCommand(identity("domestic"), seat.ForceId,
                    engine.Snapshot.Simulation.NextSequence, develop.Development),
                _ => throw new InvalidOperationException("Unknown economic intent.")
            };
            var result = engine.Execute(context, command);
            if (!result.Submission.Accepted)
            {
                if (decision.Intent is DevelopIntent failure) { rejected.Add(CanonicalJson.Hash(failure.Development)); continue; }
                break;
            }
            events.AddRange(result.Events);
            if (decision.Intent is PurchaseFoodIntent purchase)
            {
                var account = observation.Accounts.Single(a => a.Id == purchase.AccountId);
                if (account.Food + purchase.Amount >= account.RequiredFood)
                    state = state with { FoodSecurity = state.FoodSecurity.Where(g =>
                        g.ControllerId != context.ControllerId || g.AccountId != purchase.AccountId).ToImmutableArray() };
            }
            if (decision.Intent is DevelopIntent) developed = true;
            else if (++purchases >= state.Configuration.MaximumPurchases) break;
        }
        return state;
    }
}
