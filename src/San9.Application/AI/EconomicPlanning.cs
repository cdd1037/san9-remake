using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

public sealed record AiPolicyConfiguration(string Version, long WorkBudget, int MaximumPurchases)
{
    public static AiPolicyConfiguration Standard { get; } = new("domains-v1", 65536, 8);
}

public sealed record FoodSecurityGoal(int ForceId, string ControllerId, int AccountId, GameClock Started);

/// <summary>Only durable intentions and reproducible policy inputs belong to the session.</summary>
public sealed record AiSessionState
{
    [JsonRequired] public AiPolicyConfiguration Configuration { get; init; } = AiPolicyConfiguration.Standard;
    [JsonRequired] public ImmutableArray<FoodSecurityGoal> FoodSecurity { get; init; } = [];
    [JsonRequired] public ImmutableArray<CampaignGoal> Campaigns { get; init; } = [];
    [JsonRequired] public ImmutableArray<OpportunityAttempt> Opportunities { get; init; } = [];

    internal void Validate(WorldSnapshot world)
    {
        if (Configuration is null || Configuration.Version != "domains-v1" || Configuration.WorkBudget is < 1 or > 1000000 ||
            Configuration.MaximumPurchases is < 1 or > 256 || FoodSecurity.IsDefault || FoodSecurity.Any(g => g is null) ||
            FoodSecurity.DistinctBy(g => (g.ControllerId, g.AccountId)).Count() != FoodSecurity.Length ||
            FoodSecurity.Any(g => string.IsNullOrWhiteSpace(g.ControllerId) || g.Started.DayNumber < world.Simulation.Started.DayNumber ||
                g.Started.DayNumber > world.Clock.DayNumber || !Authorized(world, g)) ||
            Campaigns.IsDefault || Campaigns.Any(g => g is null || !Enum.IsDefined(g.Phase) || g.UnitIds.IsDefault || g.SourceIds.IsDefault ||
                g.SourceIds.Any(id => id < 0) || g.SourceIds.Distinct().Count() != g.SourceIds.Length ||
                g.SourceIds.Any(id => !world.Settlements.Any(s => s.Id == id)) ||
                g.ExpectedOwnerId is int owner && !world.Forces.Any(f => f.Id == owner) ||
                g.UnitIds.Any(id => world.Units.Any(u => u.Id == id && u.OwnerId != g.ForceId)) ||
                g.UnitIds.Any(id => id < 0) || g.UnitIds.Distinct().Count() != g.UnitIds.Length ||
                string.IsNullOrWhiteSpace(g.Conditions) || !CampaignAuthorized(world, g) || g.ExpectedDays < 0 || g.Progress < 0 ||
                g.Started.DayNumber < world.Simulation.Started.DayNumber || g.LastProgress.DayNumber < g.Started.DayNumber ||
                g.LastProgress.DayNumber > world.Clock.DayNumber) ||
            Campaigns.DistinctBy(g => (g.ForceId, g.TargetId)).Count() != Campaigns.Length ||
            Opportunities.IsDefault || Opportunities.Any(a => a is null || string.IsNullOrWhiteSpace(a.Key) ||
                string.IsNullOrWhiteSpace(a.Conditions) || a.TaskId == Guid.Empty || a.ActorId < 0 ||
                a.Started.DayNumber > world.Clock.DayNumber || a.Started.DayNumber < world.Simulation.Started.DayNumber ||
                a.RetryAfter.DayNumber < a.Started.DayNumber || a.Outcome is not ("pending" or "succeeded" or "failed" or "finished") ||
                a.Outcome == "pending" && !world.Simulation.Tasks.Any(t => t.Id == a.TaskId &&
                    t.OfficerId == a.ActorId && t.ForceId == a.ForceId && t.Journey?.ReturnLeg is null) ||
                !OpportunityAuthorized(world, a)) || Opportunities.DistinctBy(a => (a.ControllerId, a.Key)).Count() != Opportunities.Length)
            throw new ArgumentException("Invalid AI policy state.");
    }

    internal AiSessionState Prune(WorldSnapshot world) => this with
    {
        FoodSecurity = FoodSecurity.Where(g => Authorized(world, g)).ToImmutableArray(),
        Campaigns = Campaigns.Where(g => CampaignAuthorized(world, g)).ToImmutableArray(),
        Opportunities = Opportunities.Where(a => OpportunityAuthorized(world, a)).ToImmutableArray()
    };

    private static bool OpportunityAuthorized(WorldSnapshot world, OpportunityAttempt attempt)
    {
        var seat = world.Simulation.Controls.FirstOrDefault(s => s.ForceId == attempt.ForceId);
        return seat is not null && (seat.Kind == ControllerKind.Automatic || attempt.ControllerId.StartsWith("corps:", StringComparison.Ordinal)) &&
            world.Settlements.Any(s => !s.IsDormant && !s.IsDestroyed &&
                CorpsControl.CanControlSettlement(world, seat, new(attempt.ControllerId), s.Id));
    }

    private static bool CampaignAuthorized(WorldSnapshot world, CampaignGoal goal)
    {
        var seat = world.Simulation.Controls.FirstOrDefault(s => s.ForceId == goal.ForceId);
        return seat is not null && !string.IsNullOrWhiteSpace(goal.ControllerId) &&
            world.Settlements.Any(s => s.Id == goal.TargetId && !s.IsDormant) &&
            (seat.Kind == ControllerKind.Automatic || goal.ControllerId.StartsWith("corps:", StringComparison.Ordinal)) &&
            (world.Settlements.Any(s => !s.IsDormant && !s.IsDestroyed && CorpsControl.CanControlSettlement(world, seat, new(goal.ControllerId), s.Id)) ||
                world.Units.Any(u => CorpsControl.CanControlUnit(world, seat, new(goal.ControllerId), u.Id)));
    }

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

public sealed record EconomicAccountObservation(int Id, long Money, long Food, long RequiredFood)
{
    public long DailyConsumption { get; init; }
    public int HorizonDays { get; init; }
}
public sealed record FoodPurchaseObservation(int SettlementId, int AccountId, ImmutableArray<int> OfficerIds, int ExchangeRate, uint Maximum);
public sealed record EconomicObservation(DomesticObservation Domestic, ImmutableArray<EconomicAccountObservation> Accounts,
    ImmutableArray<FoodPurchaseObservation> Purchases, int Turn)
{
    public long ProjectionWork { get; init; }
    public bool Complete { get; init; } = true;

    internal static EconomicObservation Capture(SimulationEngine engine, ControlSeat seat, CommandContext context, long budget, bool quoteAll = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        var world = engine.Snapshot;
        var domestic = DomesticObservation.Capture(world, seat, context);
        var abilities = engine.GetAbilities(world);
        domestic = domestic with { Abilities = domestic.Settlements.SelectMany(s => s.OfficerIds).Distinct()
            .ToImmutableDictionary(id => id, id => abilities[id]) };
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
            var input = FoodSupplyForecast.Capture(world, seat.ForceId, id);
            var food = FoodSupplyForecast.Evaluate(input);
            var treasury = world.Corps.Single(c => c.Id == id).Treasury;
            accounts.Add(new(id, treasury.Money, food.Balance, food.Expenditure)
                { DailyConsumption = input.DailyConsumption, HorizonDays = FoodSupplyForecast.DaysUntilHarvest(input) });
        }
        foreach (var settlement in domestic.Settlements)
        {
            if (!Spend()) return Result(false);
            var account = accounts.Single(a => a.Id == settlement.AccountId);
            if (!quoteAll && account.Food >= account.RequiredFood) continue;
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
                .Take(6).Select(p => p.OfficerIds[0]).ToImmutableArray();
            for (int mask = 1; mask < 1 << officers.Length; mask++)
            {
                int count = System.Numerics.BitOperations.PopCount((uint)mask);
                if (count is < 2 or > 5) continue;
                if (!Spend()) return Result(false);
                var participants = officers.Where((id, index) => (mask & 1 << index) != 0).ToImmutableArray();
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
                var intent = new PurchaseFoodIntent(account.Id, purchase.SettlementId, purchase.OfficerIds, (uint)amount);
                if (rejected?.Contains(CanonicalJson.Hash(intent)) == true) continue;
                ledger[account.Id] -= cost;
                return Result(intent);
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
        foreach (var generated in plan.Candidates)
        {
            if (!Spend()) return Result(null, true);
            var candidate = generated;
            var account = observation.Accounts.Single(a => a.Id == funded.Settlements.Single(s => s.Id == candidate.SettlementId).AccountId);
            if (goals.Any(g => g.AccountId == account.Id) && candidate.Activity != DomesticActivity.Train) continue;
            if (candidate.Activity == DomesticActivity.Recruit)
            {
                var city = funded.Settlements.Single(s => s.Id == candidate.SettlementId);
                if (candidate.OfficerIds.Any(id => !domestic.Abilities.ContainsKey(id))) continue;
                uint maximum = (uint)DomesticDevelopment.RecruitCount(city.DraftPopulation, city.Soldiers, city.SoldierCapacity,
                    city.Money, candidate.OfficerIds.Select(id => domestic.Abilities[id]).ToArray());
                uint amount = city.ConsumesFood ? FoodSupplyForecast.SustainableRecruitment(city.Soldiers, city.Wounded, maximum,
                    account.HorizonDays, account.Food - account.RequiredFood) : maximum;
                if (amount == 0) continue;
                candidate = candidate with { RecruitAmount = amount };
            }
            if (rejected?.Contains(CanonicalJson.Hash(candidate)) == true) continue;
            return Result(new DevelopIntent(account.Id, candidate), plan.BudgetExhausted);
        }
        return Result(null, plan.BudgetExhausted);
    }
}
