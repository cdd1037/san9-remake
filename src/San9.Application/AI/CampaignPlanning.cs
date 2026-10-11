using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

public enum CampaignPhase { Preparing, Assembling, Attacking, Consolidating, Withdrawing, CoolingDown }
public sealed record CampaignGoal(int ForceId, string ControllerId, int TargetId, int? ExpectedOwnerId,
    CampaignPhase Phase, GameClock Started, GameClock LastProgress, long Progress,
    int ExpectedDays, ImmutableArray<int> UnitIds, string Conditions)
{
    [JsonRequired] public ImmutableArray<int> SourceIds { get; init; } = [];
}

public sealed record CampaignPlace(int Id, int? OwnerId, uint Soldiers, int Morale, int Durability,
    int MaxDurability, int PublicOrder, int Leadership, CombatProfile Profile, long Value, string Conditions)
{
    public bool CanRetaliate { get; init; } = true;
}
public sealed record CampaignForce(AllocationCandidate Action, uint Soldiers, int Morale, int Leadership,
    CombatProfile Profile, bool DamagesWalls, int? UnitId = null, long TacticValue = 0, bool NeedsOrder = false);
public sealed record CampaignWave(int TargetId, int Days, uint Soldiers);
public sealed record CampaignPurchase(string ControllerId, FoodPurchaseObservation Quote);
public sealed record CampaignObservation(GameClock Clock, int ForceId, ImmutableArray<CampaignPlace> Places,
    ImmutableArray<CampaignForce> Forces, ImmutableArray<CampaignWave> Waves,
    ImmutableArray<AllocationCandidate> Returns,
    ImmutableArray<AllocationCandidate> Consolidation, ImmutableArray<EconomicAccountObservation> Accounts, bool Complete)
{
    public ImmutableArray<int> ActiveUnitIds { get; init; } = [];
    public ImmutableArray<CampaignPurchase> Purchases { get; init; } = [];
    public ImmutableArray<int> WaitingSources { get; init; } = [];
}
public sealed record CampaignForecast(bool Feasible, int Days, long Survivors, long Losses);
public sealed record CampaignDecision(ImmutableArray<AllocationCandidate> Candidates, ImmutableArray<CampaignGoal> Goals);

/// <summary>Bounded ordinary-combat estimate, not a second simulation or a prediction of hidden tactics.</summary>
public static class CampaignCombatForecast
{
    public static CampaignForecast Evaluate(CampaignPlace target, ImmutableArray<CampaignForce> forces,
        ImmutableArray<CampaignWave> waves, int delay, Func<bool> spend)
    {
        if (forces.IsDefaultOrEmpty) return new(false, 0, 0, 0);
        var soldiers = forces.Select(f => f.Soldiers).ToArray();
        uint defenders = target.Soldiers;
        long arrivedReinforcements = 0;
        int walls = target.Durability;
        long original = soldiers.Sum(s => (long)s);
        int arrival = forces.Max(f => f.Action.Arrival!.Days!.Value) + delay;
        // Ninety days bounds the forecast horizon. No assumed tactic proc can make an infeasible siege feasible.
        for (int day = 0; day <= arrival + 90; day++)
        {
            if (!spend()) return new(false, day, 0, original);
            foreach (var wave in waves.Where(w => w.TargetId == target.Id && w.Days == day))
            {
                defenders = (uint)Math.Min(uint.MaxValue, (long)defenders + wave.Soldiers);
                arrivedReinforcements += wave.Soldiers;
            }
            bool retaliated = false;
            int active = forces.Where((f, i) => soldiers[i] > 0 && day >= f.Action.Arrival!.Days + delay).Count();
            for (int i = 0; i < forces.Length; i++)
            {
                var force = forces[i];
                if (day < force.Action.Arrival!.Days + delay || soldiers[i] == 0) continue;
                var attacker = new CombatStrength(soldiers[i], force.Morale, force.Leadership, 1, 10);
                var defender = new CombatStrength(defenders, target.Morale, target.Leadership, Math.Max(1, active), 10);
                uint loss = CombatMath.TroopDamage(attacker, defender, force.Profile, target.Profile, true, walls);
                uint reply = target.CanRetaliate && !retaliated ? CombatMath.TroopDamage(defender, attacker, target.Profile, force.Profile, false, 0) : 0;
                retaliated |= target.CanRetaliate;
                defenders -= loss;
                if (force.DamagesWalls) walls -= CombatMath.DurabilityDamage(soldiers[i], force.Leadership, force.Profile.AgainstWalls, walls);
                soldiers[i] -= reply;
                if ((defenders == 0 || walls == 0) && soldiers[i] > 0)
                {
                    long survivors = soldiers.Where((s, j) => day >= forces[j].Action.Arrival!.Days + delay).Sum(s => (long)s);
                    // Keep enough strength for the first counterattack window after capture, including a two-day arrival error.
                    // Reinforcements already fought are bounded by the remaining defenders. Wall collapse
                    // does not erase surviving field forces, but defeated forces cannot attack a second time.
                    long counter = Math.Min(arrivedReinforcements, defenders) +
                        waves.Where(w => w.TargetId == target.Id && w.Days > day && w.Days <= day + 12).Sum(w => (long)w.Soldiers);
                    return new(survivors >= Math.Max(1000, counter), day, survivors, original - soldiers.Sum(s => (long)s));
                }
            }
            if (soldiers.All(s => s == 0)) break;
        }
        return new(false, arrival + 90, soldiers.Sum(s => (long)s), original - soldiers.Sum(s => (long)s));
    }
}

internal static class CampaignPlanner
{
    internal static CampaignDecision Decide(CampaignObservation observation, ImmutableArray<CampaignGoal> memory, AllocationWork work)
    {
        if (!observation.Complete) return new([], memory);
        var goals = memory.ToBuilder();
        var candidates = ImmutableArray.CreateBuilder<AllocationCandidate>();
        foreach (var goal in memory.Where(g => g.ForceId == observation.ForceId))
        {
            if (!work.Spend()) return new(candidates.ToImmutable(), goals.ToImmutable());
            var target = observation.Places.FirstOrDefault(p => p.Id == goal.TargetId);
            var units = observation.Forces.Where(f => f.Action.TargetId == goal.TargetId && f.UnitId is int id && goal.UnitIds.Contains(id)).ToImmutableArray();
            int index = goals.IndexOf(goal);
            if (goal.Phase == CampaignPhase.CoolingDown)
            {
                if (target is null || target.Conditions != goal.Conditions || observation.Clock.DayNumber - goal.LastProgress.DayNumber >= 30)
                    goals.Remove(goal);
                continue;
            }
            if (target?.OwnerId == observation.ForceId)
            {
                goals[index] = goal with { Phase = CampaignPhase.Consolidating };
                candidates.AddRange(observation.Consolidation.Where(c => c.TargetId == goal.TargetId));
                bool safe = target.PublicOrder >= 500 && target.Durability >= target.MaxDurability / 2 &&
                    target.Soldiers >= Math.Max(1000L, observation.Waves.Where(w => w.TargetId == target.Id && w.Days <= 12).Sum(w => (long)w.Soldiers));
                candidates.AddRange(observation.Returns.Where(c =>
                    c.Intent is OrderUnitCommand order && goal.UnitIds.Contains(order.UnitId)));
                if (safe && !goal.UnitIds.Intersect(observation.ActiveUnitIds).Any()) goals.RemoveAt(index);
                continue;
            }
            bool invalid = target is null || target.OwnerId != goal.ExpectedOwnerId;
            invalid |= goal.UnitIds.Intersect(observation.ActiveUnitIds).Any(id => !units.Any(f => f.UnitId == id));
            bool withdrawing = goal.Phase == CampaignPhase.Withdrawing;
            var continuation = observation.Forces.Where(f => f.Action.TargetId == goal.TargetId &&
                (f.UnitId is null || goal.UnitIds.Contains(f.UnitId.Value))).ToImmutableArray();
            long progress = target is null ? long.MaxValue : target.Soldiers + (long)target.Durability * 100 +
                (units.IsEmpty ? continuation.Select(f => (long)(f.Action.Arrival?.Days ?? goal.ExpectedDays) * 1000).DefaultIfEmpty(goal.ExpectedDays * 1000L).Min() :
                    units.Max(f => (long)(f.Action.Arrival?.Days ?? goal.ExpectedDays) * 1000));
            bool advanced = progress < goal.Progress;
            if (advanced && !withdrawing) goals[index] = goal with { LastProgress = observation.Clock, Progress = progress, Phase = CampaignPhase.Attacking };
            bool stalled = observation.Clock.DayNumber - (advanced ? observation.Clock : goal.LastProgress).DayNumber > Math.Max(20, goal.ExpectedDays + 10);
            if (!invalid && !stalled && goal.Phase == CampaignPhase.Preparing &&
                goal.SourceIds.Intersect(observation.WaitingSources).Any()) continue;
            var plan = !invalid && !withdrawing && !stalled
                ? Choose(target!, continuation, observation, work) : null;
            if (work.Remaining == 0) return new(candidates.ToImmutable(), goals.ToImmutable());
            if (plan is null && !invalid && !stalled && goal.Phase == CampaignPhase.Preparing &&
                goal.LastProgress.DayNumber == observation.Clock.DayNumber) continue;
            if (plan is not null)
            {
                candidates.AddRange(plan.Value.Actions);
            }
            else
            {
                candidates.AddRange(observation.Returns.Where(c => c.Intent is OrderUnitCommand order && goal.UnitIds.Contains(order.UnitId)));
                bool exited = !goal.UnitIds.Intersect(observation.ActiveUnitIds).Any();
                goals[index] = goal with { Phase = exited ? CampaignPhase.CoolingDown : CampaignPhase.Withdrawing,
                    LastProgress = exited ? observation.Clock : goal.LastProgress,
                    Conditions = target?.Conditions ?? goal.Conditions };
            }
        }
        // A force finishes or exits its current commitment before spending the same reserve on a new objective.
        if (goals.Any(g => g.ForceId == observation.ForceId && g.Phase != CampaignPhase.CoolingDown))
            return new(candidates.ToImmutable(), goals.ToImmutable());
        foreach (var target in observation.Places.Where(p => p.OwnerId != observation.ForceId).OrderByDescending(p => p.Value).ThenBy(p => p.Id))
        {
            if (!work.Spend()) break;
            if (goals.Any(g => g.ForceId == observation.ForceId && g.TargetId == target.Id)) continue;
            var forces = observation.Forces.Where(f => f.Action.TargetId == target.Id && (f.UnitId is null || f.NeedsOrder)).ToImmutableArray();
            var plan = Choose(target, forces, observation, work);
            if (plan is not null) candidates.AddRange(plan.Value.Actions);
        }
        return new(candidates.ToImmutable(), goals.ToImmutable());
    }

    private static (ImmutableArray<AllocationCandidate> Actions, CampaignForecast Forecast)? Choose(CampaignPlace target,
        ImmutableArray<CampaignForce> options, CampaignObservation observation, AllocationWork work)
    {
        if (options.IsEmpty) return null;
        var committed = options.Where(f => f.UnitId is not null).ToImmutableArray();
        (ImmutableArray<AllocationCandidate> Actions, CampaignForecast Forecast, long Value)? best = null;
        // Keep a mobility alternative as well as troop strength, wall strength and preparation.
        // Fast, weaker formations must reach the actual forecast before value comparison can reject them.
        foreach (int mode in new[] { 0, 1, 2, 3 })
        {
            var fresh = options.Where(f => f.UnitId is null).GroupBy(f => f.Action.SourceId).Select(g => g
                .OrderBy(f => mode < 2 && f.Action.Intent is not DeployUnitCommand ? 1 : 0)
                .ThenBy(f => mode == 3 ? f.Action.Arrival!.Days : 0)
                .ThenByDescending(f => mode == 3 ? 0 : mode == 1 ? f.DamagesWalls ? CombatMath.WallAttackStrength(f.Soldiers, f.Leadership, f.Profile.AgainstWalls) : 0 :
                    CombatMath.FormationStrength(new(f.Soldiers, f.Morale, f.Leadership, 1, 10), f.Profile).AgainstGarrison + f.TacticValue)
                .ThenBy(f => f.Action.Intent is DeployUnitCommand ? 0 : 1).ThenBy(f => f.Action.Arrival!.Days)
                .ThenBy(f => f.Action.MoneyCost).ThenBy(f => (f.Action.Intent as DeployUnitCommand)?.FormationId ?? -1).First())
                .OrderBy(f => f.Action.Arrival!.Days).ThenBy(f => f.Action.SourceId).ToImmutableArray();
            for (int count = committed.IsEmpty ? 1 : 0; count <= fresh.Length; count++)
            {
                if (!work.Spend()) return best is { } known ? (known.Actions, known.Forecast) : null;
                var plan = EvaluatePlan(target, committed.AddRange(fresh.Take(count)), observation, work);
                if (plan is { } result && (best is null || result.Value > best.Value.Value)) best = result;
            }
        }
        return best is { } selected ? (selected.Actions, selected.Forecast) : null;
    }

    private static (ImmutableArray<AllocationCandidate> Actions, CampaignForecast Forecast, long Value)? EvaluatePlan(CampaignPlace target,
        ImmutableArray<CampaignForce> chosen, CampaignObservation observation, AllocationWork work)
    {
        if (chosen.IsEmpty || chosen.Any(f => f.Morale < 30)) return null;
        var officers = chosen.SelectMany(f => f.Action.Officers).ToArray();
        if (officers.Distinct().Count() != officers.Length) return null;
        // A signed departure can wait at most nine days. A larger split is not an executable assembly plan.
        int latest = chosen.Max(f => f.Action.Arrival!.Days!.Value);
        if (chosen.Any(f => f.UnitId is null && latest - f.Action.Arrival!.Days > 9)) return null;
        chosen = chosen.Select(f => f.UnitId is null && f.Action.Intent is DeployUnitCommand deploy
            ? f with { Action = f.Action with { Intent = deploy with { DepartureDelayDays = latest - f.Action.Arrival!.Days!.Value },
                Arrival = f.Action.Arrival with { Days = latest } } } : f).ToImmutableArray();
        var forecast = CampaignCombatForecast.Evaluate(target, chosen, observation.Waves, 0, work.Spend);
        if (!forecast.Feasible) return null;
        var delayed = CampaignCombatForecast.Evaluate(target, chosen, observation.Waves, 2, work.Spend);
        if (!delayed.Feasible) return null;
        var supply = ImmutableArray.CreateBuilder<AllocationCandidate>();
        foreach (var group in chosen.GroupBy(f => f.Action.AccountId))
        {
            var account = observation.Accounts.FirstOrDefault(a => a.Id == group.Key);
            long food = group.Sum(f => f.Action.AdditionalFood) + group.Sum(f => FoodSupplyForecast.FieldConsumption(f.Soldiers, 0)) *
                Math.Max(0, delayed.Days + 12 - (account?.HorizonDays ?? 0));
            if (account is null || account.Money < group.Sum(f => f.Action.MoneyCost)) return null;
            long shortage = account.RequiredFood + food - account.Food;
            if (shortage <= 0) continue;
            var purchase = observation.Purchases.Where(p => p.Quote.AccountId == account.Id && p.Quote.Maximum >= shortage &&
                p.Quote.ExchangeRate > 0 && account.Money >= group.Sum(f => f.Action.MoneyCost) + (shortage + p.Quote.ExchangeRate - 1) / p.Quote.ExchangeRate)
                .OrderBy(p => p.Quote.OfficerIds.Length).ThenBy(p => p.Quote.SettlementId).FirstOrDefault();
            if (purchase is null || !work.Spend()) return null;
            var quote = purchase.Quote;
            supply.Add(new(purchase.ControllerId, account.Id, new FoodTradeCommand(Guid.Empty, observation.ForceId, 0,
                quote.SettlementId, quote.OfficerIds, FoodTradeDirection.Buy, checked((uint)shortage)), quote.OfficerIds, 2, 0,
                (shortage + quote.ExchangeRate - 1) / quote.ExchangeRate, 0, quote.SettlementId, target.Id, Purpose: "campaign"));
        }
        long value = target.Value - delayed.Losses - delayed.Days * 100L - chosen.Sum(f => f.Action.MoneyCost + f.Action.OfficerOpportunityCost);
        if (value <= 0) return null;
        var next = supply.Count > 0 ? supply.ToImmutable() : chosen.Where(f => f.UnitId is null || f.NeedsOrder).Select(f => f.Action).ToImmutableArray();
        bool Preparing(AllocationCandidate c) => c.Intent is not (DeployUnitCommand or OrderUnitCommand);
        if (next.Any(Preparing)) next = next.Where(Preparing).ToImmutableArray();
        return (next.Select(c => c with
        { Benefit = value, Priority = 2, Purpose = "campaign", Campaign = new(observation.ForceId, c.ControllerId, target.Id,
            target.OwnerId, Preparing(c) ? CampaignPhase.Preparing : CampaignPhase.Assembling, observation.Clock, observation.Clock,
            target.Soldiers + target.Durability * 100L + latest * 1000L, delayed.Days, [], target.Conditions)
            { SourceIds = chosen.Select(f => f.Action.SourceId).OfType<int>().Distinct().Order().ToImmutableArray() } }).ToImmutableArray(), delayed, value);
    }
}
