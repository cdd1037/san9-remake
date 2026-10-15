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
    CombatProfile Profile, bool DamagesWalls, int? UnitId = null, long TacticValue = 0, bool NeedsOrder = false)
{
    public CampaignTacticEstimate? Tactics { get; init; }
}
public sealed record CampaignWave(int TargetId, int Days, uint Soldiers);
public sealed record CampaignPurchase(string ControllerId, FoodPurchaseObservation Quote);
public sealed record CampaignGuard(int OfficerId, int Leadership);
public sealed record CampaignSource(int Id, uint Soldiers, uint Wounded, bool ConsumesFood,
    ImmutableArray<CampaignGuard> Guards, ImmutableDictionary<int, uint> Reserves);
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
    public ImmutableArray<CampaignSource> Sources { get; init; } = [];
    public ImmutableArray<AllocationCandidate> Preparations { get; init; } = [];
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
        List<CampaignPlanSearch> searches = [];
        foreach (var target in observation.Places.Where(p => p.OwnerId != observation.ForceId).OrderByDescending(p => p.Value).ThenBy(p => p.Id))
        {
            if (!work.Spend()) break;
            if (goals.Any(g => g.ForceId == observation.ForceId && g.TargetId == target.Id)) continue;
            var forces = observation.Forces.Where(f => f.Action.TargetId == target.Id && (f.UnitId is null || f.NeedsOrder)).ToImmutableArray();
            if (!forces.IsEmpty) searches.Add(new(target, forces, observation, retainAlternatives: true));
        }
        try
        {
            // Share comparison work across objectives before deepening any one objective's search.
            // Completed searches spend nothing further; unused capacity flows to remaining searches.
            long portfolioReserve = Math.Max(1, work.Remaining / 4);
            while (work.Remaining > portfolioReserve && searches.Any(s => !s.Complete))
                foreach (var search in searches)
                    if (!search.Complete && work.Remaining > portfolioReserve) search.Advance(work);
            candidates.AddRange(ChoosePortfolio(searches.Select(s => s.Alternatives.OrderByDescending(p => p.Value).ToImmutableArray()).ToArray(),
                candidates.ToImmutable(), observation, work));
        }
        finally
        {
            foreach (var search in searches) search.Dispose();
        }
        // Preparations compete only where no verified attack has already committed the same objective.
        candidates.AddRange(observation.Preparations.Where(p => !candidates.Any(c => c.Campaign?.TargetId == p.Campaign?.TargetId)));
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
    private static ImmutableArray<AllocationCandidate> ChoosePortfolio(ImmutableArray<PortfolioOption>[] alternatives,
        ImmutableArray<AllocationCandidate> locked, CampaignObservation observation, AllocationWork work)
    {
        ImmutableArray<PortfolioOption> best = [];
        long bestValue = 0;
        // A greedy seed provides a legal portfolio before refining substitutions and target subsets.
        ImmutableArray<PortfolioOption> seed = [];
        foreach (var group in alternatives)
            foreach (var option in group)
            {
                if (!work.Spend()) return best.SelectMany(p => p.Actions).ToImmutableArray();
                var proposed = seed.Add(option);
                if (!Compatible(proposed)) continue;
                seed = proposed;
                best = seed;
                bestValue = seed.Sum(p => p.Value);
                break;
            }
        bool improved;
        do
        {
            improved = false;
            foreach (var group in alternatives)
            foreach (var option in group)
            {
                if (!work.Spend()) return best.SelectMany(p => p.Actions).ToImmutableArray();
                int? target = option.Forces[0].Action.TargetId;
                var actors = option.Forces.SelectMany(f => f.Action.Participants).ToHashSet();
                var retained = best.Where(p => p.Forces[0].Action.TargetId != target &&
                    !p.Forces.Any(f => f.Action.Participants.Any(actors.Contains))).ToImmutableArray();
                var proposed = retained.Add(option);
                long value = proposed.Sum(p => p.Value);
                if (value > bestValue && Compatible(proposed))
                { best = proposed; bestValue = value; improved = true; }
                // A substitution may release a specialist for a second objective. Evaluate that exchange together.
                foreach (var otherGroup in alternatives.Where(g => !g.IsEmpty && g[0].Forces[0].Action.TargetId != target &&
                    !retained.Any(p => p.Forces[0].Action.TargetId == g[0].Forces[0].Action.TargetId)))
                foreach (var other in otherGroup)
                {
                    if (!work.Spend()) return best.SelectMany(p => p.Actions).ToImmutableArray();
                    if (value + other.Value <= bestValue) continue;
                    var pair = proposed.Add(other);
                    if (!Compatible(pair)) continue;
                    best = pair; bestValue = value + other.Value; improved = true;
                    break;
                }
            }
        } while (improved);
        return best.SelectMany(p => p.Actions).ToImmutableArray();

        bool Compatible(ImmutableArray<PortfolioOption> plans)
        {
            var actions = locked.Concat(plans.SelectMany(p => p.Forces.Select(f => f.Action)))
                .Concat(plans.SelectMany(p => p.Actions.Where(c => c.Intent is FoodTradeCommand))).ToArray();
            var people = actions.SelectMany(c => c.Participants).ToArray();
            if (people.Distinct().Count() != people.Length) return false;
            var units = plans.SelectMany(p => p.Forces).Where(f => f.UnitId is not null).Select(f => f.UnitId!.Value)
                .Concat(locked.Select(c => c.Intent).OfType<OrderUnitCommand>().Select(c => c.UnitId)).ToArray();
            if (units.Distinct().Count() != units.Length) return false;
            if (actions.Select(c => c.Intent).OfType<FoodTradeCommand>().GroupBy(c => c.SettlementId).Any(g => g.Count() > 1)) return false;
            if (actions.Where(c => c.Intent is DeployUnitCommand or DevelopCommand or MoveOfficersCommand).GroupBy(c => c.SourceId)
                .Any(g => g.Count() > 1 && g.Any(c => c.Intent is not DeployUnitCommand))) return false;
            foreach (var group in actions.Where(c => c.Intent is DeployUnitCommand).GroupBy(c => c.SourceId))
            {
                var source = observation.Sources.FirstOrDefault(s => s.Id == group.Key);
                if (source is null) { if (group.Count() > 1) return false; continue; }
                int leadership = source.Guards.Where(g => !people.Contains(g.OfficerId)).Select(g => g.Leadership).DefaultIfEmpty(50).Max();
                int latest = group.Max(c => c.Arrival!.Days!.Value);
                long reserve = Math.Max(source.Reserves[leadership], observation.Waves
                    .Where(w => w.TargetId == source.Id && w.Days <= latest + 12).Sum(w => (long)w.Soldiers));
                if (group.Sum(c => (long)((DeployUnitCommand)c.Intent).Soldiers) + reserve > source.Soldiers) return false;
            }
            foreach (var group in actions.GroupBy(c => c.AccountId))
            {
                var account = observation.Accounts.FirstOrDefault(a => a.Id == group.Key);
                if (account is null || group.Sum(c => c.MoneyCost) > account.Money) return false;
                long food = group.Sum(c => c.AdditionalFood);
                foreach (var deployments in group.Where(c => c.Intent is DeployUnitCommand).GroupBy(c => c.SourceId))
                {
                    var source = observation.Sources.FirstOrDefault(s => s.Id == deployments.Key);
                    if (source is null || deployments.Count() < 2) continue;
                    uint soldiers = checked((uint)deployments.Sum(c => (long)((DeployUnitCommand)c.Intent).Soldiers));
                    long split = deployments.Sum(c => FoodSupplyForecast.FieldConsumption(((DeployUnitCommand)c.Intent).Soldiers, 0)) -
                        FoodSupplyForecast.FieldConsumption(soldiers, 0);
                    food += FoodSupplyForecast.DeploymentExpenditure(source.Soldiers, soldiers, account.HorizonDays, source.Wounded, source.ConsumesFood) +
                        split * account.HorizonDays - deployments.Sum(c => c.AdditionalFood);
                }
                food += plans.Sum(p => p.Forces.Where(f => f.Action.AccountId == account.Id).Sum(f => FoodSupplyForecast.FieldConsumption(f.Soldiers, 0)) *
                    Math.Max(0, p.Days + 12 - account.HorizonDays));
                long purchased = group.Select(c => c.Intent).OfType<FoodTradeCommand>().Sum(c => (long)c.FoodAmount);
                if (account.RequiredFood + food > account.Food + purchased) return false;
            }
            return true;
        }
    }

    private sealed record PortfolioOption(ImmutableArray<AllocationCandidate> Actions, ImmutableArray<CampaignForce> Forces, long Value, int Days);

    private sealed class CampaignPlanSearch(CampaignPlace target, ImmutableArray<CampaignForce> options,
        CampaignObservation observation, bool retainAlternatives = false) : IDisposable
    {
        private readonly HashSet<ImmutableArray<CampaignForce>> evaluated = new(new ForceSequenceComparer());
        private IEnumerator<ImmutableArray<CampaignForce>>? proposals;
        private IEnumerator<(ImmutableArray<AllocationCandidate> Actions, CampaignForecast Forecast, long Value)?>? evaluation;
        private ImmutableArray<CampaignForce> evaluatingForces;
        internal List<PortfolioOption> Alternatives { get; } = [];
        private ImmutableArray<CampaignForce> incumbentForces = [];
        private readonly Dictionary<string, CampaignForecast> forecasts = [];
        internal bool Complete { get; private set; }
        internal (ImmutableArray<AllocationCandidate> Actions, CampaignForecast Forecast, long Value)? Best { get; private set; }

        internal void Advance(AllocationWork work)
        {
            if (Complete || !work.Spend()) return;
            if (evaluation is not null)
            {
                bool more = evaluation.MoveNext();
                if (more && evaluation.Current is { } result)
                {
                    if (Best is null || result.Value > Best.Value.Value) { Best = result; incumbentForces = evaluatingForces; }
                    if (retainAlternatives) Alternatives.Add(new(result.Actions, evaluatingForces, result.Value, result.Forecast.Days));
                }
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
            if (chosen.IsEmpty || !CanAssemble(chosen, observation) || !retainAlternatives && Best is { } incumbent && UpperBound(chosen) <= incumbent.Value) return;
            var ordered = chosen.OrderBy(f => f.Action.SourceId).ThenBy(f => f.UnitId).ToImmutableArray();
            if (!evaluated.Add(ordered)) return;
            evaluatingForces = chosen;
            evaluation = EvaluatePlan(target, chosen, observation, forecasts).GetEnumerator();
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
                    .ThenBy(f => f.Action.MoneyCost).ThenBy(f => (f.Action.Intent as DeployUnitCommand)?.FormationId ?? -1).ToArray())
                    .OrderBy(g => g[0].Action.Arrival!.Days).ThenBy(g => g[0].Action.SourceId).ToArray();
                var seed = committed;
                if (!seed.IsEmpty) yield return seed;
                foreach (var source in fresh)
                {
                    foreach (var option in source)
                    {
                        var proposed = seed.Add(option);
                        yield return proposed;
                        if (!CanAssemble(proposed, observation)) continue;
                        seed = proposed;
                        break;
                    }
                }
                // Also seed a disjoint split from each source where the available troops support it.
                foreach (var source in fresh)
                {
                    var split = committed;
                    var home = observation.Sources.FirstOrDefault(s => s.Id == source[0].Action.SourceId);
                    if (home is null) continue;
                    foreach (var option in source.Where(f => f.Soldiers <= home.Soldiers / 2))
                    {
                        var proposed = split.Add(option);
                        yield return proposed;
                        if (CanAssemble(proposed, observation)) split = proposed;
                    }
                }
            }
            // Improve executable seeds with one insertion/replacement/removal at a time.
            // The budget bounds useful comparisons rather than a Cartesian product of source subsets.
            long previous;
            do
            {
                previous = Best?.Value ?? long.MinValue;
                foreach (var option in options.Where(f => f.UnitId is null).OrderBy(f => f.Action.Arrival!.Days)
                    .ThenBy(f => f.Action.MoneyCost + f.Action.OfficerOpportunityCost).ThenBy(f => f.Action.SourceId))
                {
                    var actors = option.Action.Participants;
                    var basis = incumbentForces.IsEmpty ? committed : incumbentForces;
                    var chosen = basis.Where(f => f.UnitId is not null || !f.Action.Participants.Intersect(actors).Any()).ToImmutableArray();
                    yield return chosen.Add(option);
                }
                var incumbent = incumbentForces;
                foreach (var remove in incumbent.Where(f => f.UnitId is null))
                    yield return incumbent.Remove(remove);
            } while (Best is { } improved && improved.Value > previous);
        }

        public void Dispose()
        {
            evaluation?.Dispose();
            proposals?.Dispose();
        }

        private sealed class ForceSequenceComparer : IEqualityComparer<ImmutableArray<CampaignForce>>
        {
            public bool Equals(ImmutableArray<CampaignForce> left, ImmutableArray<CampaignForce> right) => left.SequenceEqual(right);

            public int GetHashCode(ImmutableArray<CampaignForce> forces)
            {
                var hash = new HashCode();
                foreach (var force in forces) hash.Add(force);
                return hash.ToHashCode();
            }
        }
    }
    private static bool CanAssemble(ImmutableArray<CampaignForce> chosen, CampaignObservation observation)
    {
        var officers = chosen.SelectMany(f => f.Action.Participants).ToArray();
        if (officers.Distinct().Count() != officers.Length) return false;
        int latest = chosen.Max(f => f.Action.Arrival!.Days!.Value);
        if (chosen.Any(f => f.UnitId is null && latest - f.Action.Arrival!.Days > 9)) return false;
        foreach (var group in chosen.Where(f => f.UnitId is null).GroupBy(f => f.Action.SourceId))
        {
            var source = observation.Sources.FirstOrDefault(s => s.Id == group.Key);
            if (source is null) { if (group.Count() > 1) return false; continue; }
            // Preparation describes one future departure, never several simultaneous uses of projected recruits.
            if (group.Any(f => f.Action.Intent is not DeployUnitCommand))
            { if (group.Count() > 1) return false; continue; }
            int leadership = source.Guards.Where(g => !officers.Contains(g.OfficerId)).Select(g => g.Leadership).DefaultIfEmpty(50).Max();
            long reserve = Math.Max(source.Reserves[leadership], observation.Waves
                .Where(w => w.TargetId == source.Id && w.Days <= latest + 12).Sum(w => (long)w.Soldiers));
            if (group.Sum(f => (long)f.Soldiers) + reserve > source.Soldiers) return false;
        }
        foreach (var group in chosen.GroupBy(f => f.Action.AccountId))
        {
            var account = observation.Accounts.FirstOrDefault(a => a.Id == group.Key);
            if (account is null || account.Money < group.Sum(f => f.Action.MoneyCost)) return false;
        }
        return true;
    }

    private static IEnumerable<(ImmutableArray<AllocationCandidate> Actions, CampaignForecast Forecast, long Value)?> EvaluatePlan(CampaignPlace target,
        ImmutableArray<CampaignForce> chosen, CampaignObservation observation, Dictionary<string, CampaignForecast> forecasts)
    {
        var officers = chosen.SelectMany(f => f.Action.Participants).ToArray();
        // A signed departure can wait at most nine days. A larger split is not an executable assembly plan.
        int latest = chosen.Max(f => f.Action.Arrival!.Days!.Value);
        chosen = chosen.Select(f => f.UnitId is null ? f with
            { Action = DelayDeparture(f.Action, latest - f.Action.Arrival!.Days!.Value, latest) } : f).ToImmutableArray();
        IEnumerable<CampaignForecast?> Project(int delay, bool expectedTactics = false)
        {
            // This cache belongs to one target in one frozen observation. Identity and opportunity
            // costs do not affect combat; arrival order, all combat inputs and reinforcement times do.
            string key = $"{delay}:{expectedTactics}:" + string.Join(";", chosen.Select(f =>
                $"{f.Soldiers},{f.Morale},{f.Leadership},{f.Profile.AgainstUnits},{f.Profile.AgainstGarrison},{f.Profile.AgainstWalls},{f.Profile.Defense},{f.Profile.WoundedPercent},{f.DamagesWalls},{f.Action.Arrival!.Days},{(expectedTactics ? f.Tactics?.Damage : 0)},{(expectedTactics ? f.Tactics?.OrdinaryHits : 0)}"));
            if (forecasts.TryGetValue(key, out var saved)) { yield return saved; yield break; }
            foreach (var step in CampaignCombatForecast.Project(target, chosen, observation.Waves, delay, expectedTactics))
            {
                if (step is not null) forecasts[key] = step;
                yield return step;
            }
        }
        CampaignForecast? forecast = null;
        foreach (var step in Project(0))
        {
            forecast = step;
            yield return null;
        }
        if (forecast?.Feasible != true) yield break;
        CampaignForecast? delayed = null;
        foreach (var step in Project(2))
        {
            delayed = step;
            yield return null;
        }
        if (delayed?.Feasible != true) yield break;
        // Compare expected gains only after both conservative scenarios have passed. Equipment never
        // turns an otherwise unverified attack into a guaranteed win by assuming future dice.
        var valued = delayed;
        if (chosen.Any(f => f.Tactics?.Damage > 0))
            foreach (var step in Project(2, true))
            {
                if (step is { Feasible: true }) valued = step;
                yield return null;
            }
        var supply = ImmutableArray.CreateBuilder<AllocationCandidate>();
        foreach (var group in chosen.GroupBy(f => f.Action.AccountId))
        {
            yield return null;
            var account = observation.Accounts.FirstOrDefault(a => a.Id == group.Key);
            if (account is null || account.Money < group.Sum(f => f.Action.MoneyCost)) yield break;
            long food = group.GroupBy(f => f.Action.SourceId).Sum(sourceGroup =>
            {
                var source = observation.Sources.FirstOrDefault(s => s.Id == sourceGroup.Key);
                if (source is null || sourceGroup.Count() == 1 || sourceGroup.Any(f => f.Action.Intent is not DeployUnitCommand))
                    return sourceGroup.Sum(f => f.Action.AdditionalFood);
                uint soldiers = checked((uint)sourceGroup.Sum(f => (long)f.Soldiers));
                long split = sourceGroup.Sum(f => FoodSupplyForecast.FieldConsumption(f.Soldiers, 0)) - FoodSupplyForecast.FieldConsumption(soldiers, 0);
                return FoodSupplyForecast.DeploymentExpenditure(source.Soldiers, soldiers, account.HorizonDays, source.Wounded, source.ConsumesFood) +
                    split * account.HorizonDays;
            }) + group.Sum(f => FoodSupplyForecast.FieldConsumption(f.Soldiers, 0)) *
                Math.Max(0, delayed.Days + 12 - account.HorizonDays);
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
        long exposure = chosen.Sum(f => (f.Tactics?.OfficerExposure ?? 0) * valued.Losses /
            Math.Max(1L, chosen.Sum(c => (long)c.Soldiers)));
        long value = target.Value - valued.Losses - valued.Days * 100L - exposure - chosen.Sum(f => f.Action.MoneyCost + f.Action.OfficerOpportunityCost);
        if (value <= 0) yield break;
        var next = supply.Count > 0 ? supply.ToImmutable() : chosen.Where(f => f.UnitId is null || f.NeedsOrder).Select(f => f.Action).ToImmutableArray();
        bool Preparing(AllocationCandidate c) => c.Intent is not (DeployUnitCommand or OrderUnitCommand) &&
            c.Continuation is not { DelayDays: 0 };
        if (next.Any(Preparing)) next = next.Where(Preparing).ToImmutableArray();
        if (next.IsEmpty) { yield return ([], delayed, value); yield break; }
        yield return (next.Select(c => c with
        { Benefit = value, Priority = 2, Purpose = "campaign", Campaign = new(observation.ForceId, c.ControllerId, target.Id,
            target.OwnerId, Preparing(c) ? CampaignPhase.Preparing : CampaignPhase.Assembling, observation.Clock, observation.Clock,
            target.Soldiers + target.Durability * 100L + latest * 1000L, delayed.Days, [], target.CaptureConditions())
            { SourceIds = chosen.Select(f => f.Action.SourceId).OfType<int>().Distinct().Order().ToImmutableArray() } }).ToImmutableArray(), delayed, value);
    }

    private static AllocationCandidate DelayDeparture(AllocationCandidate action, int delay, int arrival) => action with
    {
        Intent = action.Intent is DeployUnitCommand deploy ? deploy with { DepartureDelayDays = delay } : action.Intent,
        Arrival = action.Arrival is null ? null : action.Arrival with { Days = arrival },
        Continuation = action.Continuation is { } next
            ? next with { Action = DelayDeparture(next.Action, delay, arrival) } : null
    };
}
