using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal sealed class AllocationWork(long maximum)
{
    public long Maximum { get; } = maximum;
    public long Remaining { get; private set; } = maximum >= 0 ? maximum : throw new ArgumentOutOfRangeException(nameof(maximum));
    public bool Spend() { if (Remaining == 0) return false; Remaining--; return true; }
    public void Charge(long amount)
    {
        if (amount < 0 || amount > Remaining) throw new InvalidOperationException("Planning exceeded its work budget.");
        Remaining -= amount;
    }
    public NavigationWorkBudget Navigation() => new((int)Math.Min(int.MaxValue, Math.Max(1, Remaining)), Spend);
}

public sealed record AllocationCandidate(string ControllerId, int AccountId, GameCommand Intent,
    ImmutableArray<int> Officers, int Priority, long Benefit, long MoneyCost, long AdditionalFood,
    int? SourceId = null, int? TargetId = null, ArrivalEstimate? Arrival = null, int? DeadlineDays = null,
    string Purpose = "support")
{
    public int? ReceivingAccountId { get; init; }
    public long ReceivingFood { get; init; }
    public long OfficerOpportunityCost { get; init; }
    public CampaignGoal? Campaign { get; init; }
    public OpportunityOption? Opportunity { get; init; }
}

public sealed record AllocationDecision(AllocationCandidate? Candidate, EconomicIntent? Economic,
    string? ControllerId, ImmutableArray<FoodSecurityGoal> Goals, long Work, bool Exhausted);

/// <summary>Joint choice over projected values. Identity, validation and mutation belong to the session.</summary>
public static class SharedAllocator
{
    public static AllocationDecision Decide(ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies,
        ImmutableArray<AllocationCandidate> candidates, ImmutableArray<FoodSecurityGoal> memory,
        long budget, IReadOnlySet<string> developed, IReadOnlySet<string> purchaseLimited,
        RejectedPlanningIntents? rejected = null)
    {
        var work = new AllocationWork(budget);
        var goals = memory;
        var ranked = candidates.Where(c => rejected?.Contains(c.Intent) != true && HasAvailableOfficers(c, economies))
            .OrderBy(c => c.Priority).ThenByDescending(c => c.Benefit)
            .ThenBy(c => c.Arrival?.Days ?? int.MaxValue).ThenBy(c => c.MoneyCost)
            .ThenBy(c => c.OfficerOpportunityCost).ThenBy(c => c.SourceId).ThenBy(c => c.TargetId).ThenBy(c => c.Officers.FirstOrDefault()).ToArray();
        AllocationDecision Result(AllocationCandidate? candidate = null, EconomicIntent? economic = null,
            string? controller = null, bool exhausted = false) => new(candidate, economic, controller, goals, budget - work.Remaining, exhausted);
        // A rescue's only usable officer is a genuine opportunity cost of a purchase or an administrative job.
        var reserved = ranked.Where(c => c.Priority == 0 && c.Arrival is { Status: ArrivalEstimateStatus.Known } &&
            (c.DeadlineDays is null || c.Arrival.Days <= c.DeadlineDays)).FirstOrDefault();
        List<(string Controller, EconomicDecision Decision)> supply = [];
        foreach (var (controller, original) in economies)
        {
            var observation = original;
            if (reserved is not null)
            {
                observation = observation with
                {
                    Accounts = observation.Accounts.Select(a => a with { Money = a.Money - (a.Id == reserved.AccountId ? reserved.MoneyCost : 0), RequiredFood = a.RequiredFood +
                        (a.Id == reserved.AccountId ? reserved.AdditionalFood : 0) +
                        (a.Id == reserved.ReceivingAccountId ? reserved.ReceivingFood : 0) }).ToImmutableArray(),
                    Purchases = observation.Purchases.Where(p => !p.OfficerIds.Intersect(reserved.Officers).Any()).ToImmutableArray()
                };
            }
            var decision = EconomicPlanner.Decide(observation, goals, controller, work.Remaining, false, rejected);
            work.Charge(decision.Work);
            // If the only buyer is also the proposed commander, that joint plan is infeasible; save the account first.
            if (decision.Intent is null && reserved is not null && original.Purchases.Any(p => p.OfficerIds.Intersect(reserved.Officers).Any()))
            {
                decision = EconomicPlanner.Decide(original, goals, controller, work.Remaining, false, rejected);
                work.Charge(decision.Work);
            }
            goals = goals.Where(g => g.ControllerId != controller || !observation.Accounts.Any(a => a.Id == g.AccountId))
                .Concat(decision.Goals).OrderBy(g => g.ForceId).ThenBy(g => g.ControllerId, StringComparer.Ordinal)
                .ThenBy(g => g.AccountId).ToImmutableArray();
            if (!purchaseLimited.Contains(controller) && decision.Intent is PurchaseFoodIntent)
                supply.Add((controller, decision));
        }
        // Fund an immediate, feasible rescue first; otherwise supply is the prerequisite for all paid work.
        foreach (var candidate in ranked.Where(c => c.Priority == 0))
        {
            if (!work.Spend()) break;
            if (Funded(candidate)) return Result(candidate, controller: candidate.ControllerId);
        }
        if (supply.Count > 0)
        {
            var best = supply.OrderByDescending(s => ((PurchaseFoodIntent)s.Decision.Intent!).Amount)
                .ThenBy(s => s.Controller, StringComparer.Ordinal).First();
            return Result(economic: best.Decision.Intent, controller: best.Controller);
        }
        if (economies.Any(e => !e.Observation.Complete)) return Result(exhausted: true);
        foreach (var candidate in ranked.Where(c => c.Priority > 0))
        {
            if (!work.Spend()) return Result(exhausted: true);
            if (Funded(candidate)) return Result(candidate, controller: candidate.ControllerId);
        }
        foreach (var (controller, observation) in economies)
        {
            if (developed.Contains(controller)) continue;
            // People held for a timely rescue may train, but cannot disappear into unrelated development.
            var domestic = observation.Domestic with { Settlements = observation.Domestic.Settlements.Select(s => s with
                { OfficerIds = s.OfficerIds.Where(id => reserved is null || !reserved.Officers.Contains(id)).ToImmutableArray() }).ToImmutableArray() };
            var decision = EconomicPlanner.Decide(observation with { Domestic = domestic }, goals, controller,
                work.Remaining, true, rejected);
            work.Charge(decision.Work);
            if (decision.Intent is DevelopIntent develop) return Result(economic: develop, controller: controller);
        }
        return Result(exhausted: work.Remaining == 0);

        bool Funded(AllocationCandidate candidate) => CanFund(candidate, economies, rejected);
    }

    internal static bool CanFund(AllocationCandidate candidate,
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies, RejectedPlanningIntents? rejected)
    {
        if (rejected?.Contains(candidate.Intent) == true) return false;
        var observation = economies.FirstOrDefault(e => e.ControllerId == candidate.ControllerId).Observation;
        var account = observation?.Accounts.FirstOrDefault(a => a.Id == candidate.AccountId);
        var receiving = economies.SelectMany(e => e.Observation.Accounts).FirstOrDefault(a => a.Id == candidate.ReceivingAccountId);
        return account is not null && account.Money >= candidate.MoneyCost &&
            HasAvailableOfficers(candidate, economies) &&
            (candidate.Intent is OrderUnitCommand { Order.Kind: MilitaryOrderKind.Return } || account.Food >= account.RequiredFood + candidate.AdditionalFood) &&
            (candidate.ReceivingAccountId is null || receiving is not null && receiving.Food >= receiving.RequiredFood + candidate.ReceivingFood) &&
            (candidate.Arrival is null || candidate.Arrival.Status == ArrivalEstimateStatus.Known &&
                (candidate.DeadlineDays is null || candidate.Arrival.Days <= candidate.DeadlineDays));
    }

    private static bool HasAvailableOfficers(AllocationCandidate candidate,
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies) =>
        candidate.Officers.IsEmpty || economies.Any(e => e.ControllerId == candidate.ControllerId &&
            candidate.Officers.All(id => e.Observation.Domestic.Settlements.Any(s => s.OfficerIds.Contains(id))));
}
