using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

public enum CampaignPhase { Preparing, Assembling, Attacking, Consolidating, Withdrawing, CoolingDown }
public sealed record CampaignGoal(int ForceId, string ControllerId, int TargetId, int? ExpectedOwnerId,
    CampaignPhase Phase, GameClock Started, GameClock LastProgress, long Progress,
    int ExpectedDays, ImmutableArray<int> UnitIds, CampaignConditions Conditions)
{
    [JsonRequired] public ImmutableArray<int> SourceIds { get; init; } = [];
}

public sealed record CampaignConditions(int? OwnerId, uint Soldiers, int Durability, int Morale);

public sealed record CampaignPlace(int Id, int? OwnerId, uint Soldiers, int Morale, int Durability,
    int MaxDurability, int PublicOrder, int Leadership, CombatProfile Profile, long Value)
{
    public bool CanRetaliate { get; init; } = true;
    public CampaignConditions CaptureConditions() => new(OwnerId, Soldiers, Durability, Morale);
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
    public bool AnalysisReady { get; init; }
    public ImmutableArray<int> UnresolvedUnitIds { get; init; } = [];
    public ImmutableArray<int> ActiveUnitIds { get; init; } = [];
    public ImmutableArray<CampaignPurchase> Purchases { get; init; } = [];
    public ImmutableArray<int> WaitingSources { get; init; } = [];
}
public sealed record CampaignForecast(bool Feasible, int Days, long Survivors, long Losses);
public sealed record CampaignDecision(ImmutableArray<AllocationCandidate> Candidates, ImmutableArray<CampaignGoal> Goals);

internal static class CampaignPlanner
{
    internal static CampaignDecision Decide(CampaignObservation observation, ImmutableArray<CampaignGoal> memory, AllocationWork work)
    {
        if (!observation.Complete && !observation.AnalysisReady) return new([], memory);
        var goals = memory.ToBuilder();
        var candidates = ImmutableArray.CreateBuilder<AllocationCandidate>();
        foreach (var goal in memory.Where(g => g.ForceId == observation.ForceId))
        {
            if (!work.Spend()) return new(candidates.ToImmutable(), goals.ToImmutable());
            if (goal.UnitIds.Intersect(observation.UnresolvedUnitIds).Any()) continue;
            var target = observation.Places.FirstOrDefault(p => p.Id == goal.TargetId);
            var units = observation.Forces.Where(f => f.Action.TargetId == goal.TargetId && f.UnitId is int id && goal.UnitIds.Contains(id)).ToImmutableArray();
            int index = goals.IndexOf(goal);
            if (goal.Phase == CampaignPhase.CoolingDown)
            {
                if (target is null || observation.Clock.DayNumber - goal.LastProgress.DayNumber >= 30 || target.CaptureConditions() != goal.Conditions)
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
            if (work.Remaining == 0)
            {
                if (plan is not null) candidates.AddRange(plan.Value.Actions);
                return new(candidates.ToImmutable(), goals.ToImmutable());
            }
            if (plan is null && !invalid && !stalled && !observation.Complete) continue;
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
                    Conditions = target?.CaptureConditions() ?? goal.Conditions };
            }
        }
        // A force finishes or exits its current commitment before spending the same reserve on a new objective.
        if (goals.Any(g => g.ForceId == observation.ForceId && g.Phase != CampaignPhase.CoolingDown))
            return new(candidates.ToImmutable(), goals.ToImmutable());
        List<CampaignPlanSearch> searches = [];
        foreach (var target in observation.Places.Where(p => p.OwnerId != observation.ForceId).OrderByDescending(p => p.Value).ThenBy(p => p.Id))
        {
            if (!work.Spend()) break;
            if (goals.Any(g => g.ForceId == observation.ForceId && g.TargetId == target.Id)) continue;
            var forces = observation.Forces.Where(f => f.Action.TargetId == target.Id && (f.UnitId is null || f.NeedsOrder)).ToImmutableArray();
            if (!forces.IsEmpty) searches.Add(new(target, forces, observation));
        }
        try
        {
            // Share comparison work across objectives before deepening any one objective's search.
            // Completed searches spend nothing further; unused capacity flows to remaining searches.
            while (work.Remaining > 0 && searches.Any(s => !s.Complete))
                foreach (var search in searches)
                    if (!search.Complete && work.Remaining > 0) search.Advance(work);
            foreach (var search in searches)
                if (search.Best is { } best) candidates.AddRange(best.Actions);
        }
        finally
        {
            foreach (var search in searches) search.Dispose();
        }
        return new(candidates.ToImmutable(), goals.ToImmutable());
    }

    private static (ImmutableArray<AllocationCandidate> Actions, CampaignForecast Forecast)? Choose(CampaignPlace target,
        ImmutableArray<CampaignForce> options, CampaignObservation observation, AllocationWork work)
    {
        using var search = new CampaignPlanSearch(target, options, observation);
        while (!search.Complete && work.Remaining > 0) search.Advance(work);
        return search.Best is { } best ? (best.Actions, best.Forecast) : null;
    }

    /// <summary>Resumable within one frozen observation. Each step visits a candidate, simulates a day or checks funding.</summary>
    private sealed class CampaignPlanSearch(CampaignPlace target, ImmutableArray<CampaignForce> options,
        CampaignObservation observation) : IDisposable
    {
        private readonly List<ImmutableArray<CampaignForce>> evaluated = [];
        private IEnumerator<ImmutableArray<CampaignForce>>? proposals;
        private IEnumerator<(ImmutableArray<AllocationCandidate> Actions, CampaignForecast Forecast, long Value)?>? evaluation;
        internal bool Complete { get; private set; }
        internal (ImmutableArray<AllocationCandidate> Actions, CampaignForecast Forecast, long Value)? Best { get; private set; }

        internal void Advance(AllocationWork work)
        {
            if (Complete || !work.Spend()) return;
            if (evaluation is not null)
            {
                bool more = evaluation.MoveNext();
                if (more && evaluation.Current is { } result && (Best is null || result.Value > Best.Value.Value)) Best = result;
                if (!more || evaluation.Current is not null)
                {
                    evaluation.Dispose();
                    evaluation = null;
                }
                return;
            }
            proposals ??= Enumerate().GetEnumerator();
            if (!proposals.MoveNext()) { Complete = true; Dispose(); return; }
            var chosen = proposals.Current;
            if (chosen.IsEmpty || !CanAssemble(chosen, observation) || Best is { } incumbent && UpperBound(chosen) <= incumbent.Value) return;
            var ordered = chosen.OrderBy(f => f.Action.SourceId).ThenBy(f => f.UnitId).ToImmutableArray();
            if (evaluated.Any(previous => previous.SequenceEqual(ordered))) return;
            evaluated.Add(ordered);
            evaluation = EvaluatePlan(target, chosen, observation).GetEnumerator();
        }

        private long UpperBound(ImmutableArray<CampaignForce> chosen, bool extend = false) => target.Value -
            ((extend ? options : chosen).Min(f => f.Action.Arrival!.Days!.Value) + 2L) * 100 -
            chosen.Sum(f => f.Action.MoneyCost + f.Action.OfficerOpportunityCost);

        private IEnumerable<ImmutableArray<CampaignForce>> Enumerate()
        {
            if (options.IsEmpty) yield break;
            var committed = options.Where(f => f.UnitId is not null).ToImmutableArray();
            // Mobility, ordinary strength, wall strength and preparation seed the incumbent.
            // These rankings do not restrict subsequent source subsets or substitutions.
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
                    yield return committed.AddRange(fresh.Take(count));
            }
            var sources = options.Where(f => f.UnitId is null).GroupBy(f => f.Action.SourceId)
                .OrderBy(g => g.Min(f => f.Action.Arrival!.Days)).ThenBy(g => g.Key)
                .Select(g => g.OrderBy(f => f.Action.Arrival!.Days).ThenBy(f => f.Action.MoneyCost + f.Action.OfficerOpportunityCost)
                    .ThenByDescending(f => f.Soldiers).ToImmutableArray()).ToArray();
            foreach (var chosen in Search(0, committed)) yield return chosen;

            IEnumerable<ImmutableArray<CampaignForce>> Search(int first, ImmutableArray<CampaignForce> chosen)
            {
                for (int source = first; source < sources.Length; source++)
                foreach (var option in sources[source])
                {
                    var next = chosen.Add(option);
                    // Yield before pruning: even rejected or duplicate nodes consume deterministic work.
                    yield return next;
                    if (!CanAssemble(next, observation) || Best is { } incumbent && UpperBound(next, extend: true) <= incumbent.Value) continue;
                    foreach (var extension in Search(source + 1, next)) yield return extension;
                }
            }
        }

        public void Dispose()
        {
            evaluation?.Dispose();
            proposals?.Dispose();
        }
    }
    private static bool CanAssemble(ImmutableArray<CampaignForce> chosen, CampaignObservation observation)
    {
        if (chosen.Any(f => f.Morale < 30)) return false;
        var officers = chosen.SelectMany(f => f.Action.Officers).ToArray();
        if (officers.Distinct().Count() != officers.Length) return false;
        int latest = chosen.Max(f => f.Action.Arrival!.Days!.Value);
        if (chosen.Any(f => f.UnitId is null && latest - f.Action.Arrival!.Days > 9)) return false;
        foreach (var group in chosen.GroupBy(f => f.Action.AccountId))
        {
            var account = observation.Accounts.FirstOrDefault(a => a.Id == group.Key);
            if (account is null || account.Money < group.Sum(f => f.Action.MoneyCost)) return false;
        }
        return true;
    }

    private static IEnumerable<(ImmutableArray<AllocationCandidate> Actions, CampaignForecast Forecast, long Value)?> EvaluatePlan(CampaignPlace target,
        ImmutableArray<CampaignForce> chosen, CampaignObservation observation)
    {
        var officers = chosen.SelectMany(f => f.Action.Officers).ToArray();
        // A signed departure can wait at most nine days. A larger split is not an executable assembly plan.
        int latest = chosen.Max(f => f.Action.Arrival!.Days!.Value);
        chosen = chosen.Select(f => f.UnitId is null && f.Action.Intent is DeployUnitCommand deploy
            ? f with { Action = f.Action with { Intent = deploy with { DepartureDelayDays = latest - f.Action.Arrival!.Days!.Value },
                Arrival = f.Action.Arrival with { Days = latest } } } : f).ToImmutableArray();
        CampaignForecast? forecast = null;
        foreach (var step in CampaignCombatForecast.Project(target, chosen, observation.Waves, 0))
        {
            forecast = step;
            yield return null;
        }
        if (forecast?.Feasible != true) yield break;
        CampaignForecast? delayed = null;
        foreach (var step in CampaignCombatForecast.Project(target, chosen, observation.Waves, 2))
        {
            delayed = step;
            yield return null;
        }
        if (delayed?.Feasible != true) yield break;
        var supply = ImmutableArray.CreateBuilder<AllocationCandidate>();
        foreach (var group in chosen.GroupBy(f => f.Action.AccountId))
        {
            yield return null;
            var account = observation.Accounts.FirstOrDefault(a => a.Id == group.Key);
            long food = group.Sum(f => f.Action.AdditionalFood) + group.Sum(f => FoodSupplyForecast.FieldConsumption(f.Soldiers, 0)) *
                Math.Max(0, delayed.Days + 12 - (account?.HorizonDays ?? 0));
            if (account is null || account.Money < group.Sum(f => f.Action.MoneyCost)) yield break;
            long shortage = account.RequiredFood + food - account.Food;
            if (shortage <= 0) continue;
            var purchase = observation.Purchases.Where(p => p.Quote.AccountId == account.Id && p.Quote.Maximum >= shortage &&
                !p.Quote.OfficerIds.Any(officers.Contains) &&
                p.Quote.ExchangeRate > 0 && account.Money >= group.Sum(f => f.Action.MoneyCost) + (shortage + p.Quote.ExchangeRate - 1) / p.Quote.ExchangeRate)
                .OrderBy(p => p.Quote.OfficerIds.Length).ThenBy(p => p.Quote.SettlementId).FirstOrDefault();
            if (purchase is null) yield break;
            var quote = purchase.Quote;
            supply.Add(new(purchase.ControllerId, account.Id, new FoodTradeCommand(Guid.Empty, observation.ForceId, 0,
                quote.SettlementId, quote.OfficerIds, FoodTradeDirection.Buy, checked((uint)shortage)), quote.OfficerIds, 2, 0,
                (shortage + quote.ExchangeRate - 1) / quote.ExchangeRate, 0, quote.SettlementId, target.Id, Purpose: "campaign"));
        }
        long value = target.Value - delayed.Losses - delayed.Days * 100L - chosen.Sum(f => f.Action.MoneyCost + f.Action.OfficerOpportunityCost);
        if (value <= 0) yield break;
        var next = supply.Count > 0 ? supply.ToImmutable() : chosen.Where(f => f.UnitId is null || f.NeedsOrder).Select(f => f.Action).ToImmutableArray();
        bool Preparing(AllocationCandidate c) => c.Intent is not (DeployUnitCommand or OrderUnitCommand);
        if (next.Any(Preparing)) next = next.Where(Preparing).ToImmutableArray();
        if (next.IsEmpty) { yield return ([], delayed, value); yield break; }
        yield return (next.Select(c => c with
        { Benefit = value, Priority = 2, Purpose = "campaign", Campaign = new(observation.ForceId, c.ControllerId, target.Id,
            target.OwnerId, Preparing(c) ? CampaignPhase.Preparing : CampaignPhase.Assembling, observation.Clock, observation.Clock,
            target.Soldiers + target.Durability * 100L + latest * 1000L, delayed.Days, [], target.CaptureConditions())
            { SourceIds = chosen.Select(f => f.Action.SourceId).OfType<int>().Distinct().Order().ToImmutableArray() } }).ToImmutableArray(), delayed, value);
    }
}
