using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal sealed record TrainingPreparation(ImmutableArray<int> Trainers, int Morale, int DepartureDays);

/// <summary>Training changes the garrison now; only participant reuse and the city quota require another xun.</summary>
internal static class TrainingPlans
{
    internal static IEnumerable<TrainingPreparation> Enumerate(uint soldiers, int morale, int trust,
        IEnumerable<int> available, ImmutableArray<int> departing, ImmutableDictionary<int, OfficerAbilities> abilities)
    {
        // Evaluate the next preparation only. Later xuns replan from actual morale and availability.
        var frontier = new List<(TrainingPreparation Plan, long Cost)>();
        foreach (var trainers in Crews(available, abilities))
        {
            int gain = DomesticDevelopment.TrainingIncrement(soldiers, morale, trust, trainers.Select(id => abilities[id]).ToArray());
            if (gain <= 0) continue;
            int delay = trainers.Intersect(departing).Any() ? 10 : 0;
            long cost = trainers.Sum(id => OpportunityCost(abilities[id]));
            var plan = new TrainingPreparation(trainers, morale + gain, delay);
            if (frontier.Any(p => p.Plan.Morale >= plan.Morale && p.Plan.DepartureDays <= delay && p.Cost <= cost)) continue;
            frontier.RemoveAll(p => p.Plan.Morale <= plan.Morale && p.Plan.DepartureDays >= delay && p.Cost >= cost);
            frontier.Add((plan, cost));
        }
        foreach (var entry in frontier.OrderBy(p => p.Plan.DepartureDays).ThenByDescending(p => p.Plan.Morale).ThenBy(p => p.Cost))
            yield return entry.Plan;
    }

    // Every officer is considered individually. Group seeds compare effect and opportunity cost;
    // the public five-participant limit bounds group size, not the pool of available officers.
    internal static IEnumerable<ImmutableArray<int>> Crews(IEnumerable<int> available,
        ImmutableDictionary<int, OfficerAbilities> abilities)
    {
        var people = available.OrderByDescending(id => abilities.GetValueOrDefault(id).Leadership).ThenBy(id => id).ToArray();
        foreach (int id in people) yield return [id];
        var economical = people.OrderBy(id => OpportunityCost(abilities.GetValueOrDefault(id)) - abilities.GetValueOrDefault(id).Leadership)
            .ThenByDescending(id => abilities.GetValueOrDefault(id).Leadership).ThenBy(id => id).ToArray();
        for (int count = 2; count <= Math.Min(5, people.Length); count++)
        {
            var strongest = people.Take(count).Order().ToImmutableArray();
            yield return strongest;
            var cheapest = economical.Take(count).Order().ToImmutableArray();
            if (!strongest.AsSpan().SequenceEqual(cheapest.AsSpan())) yield return cheapest;
        }
    }

    internal static long OpportunityCost(OfficerAbilities ability) => Math.Max(ability.Politics,
        Math.Max(ability.Intelligence, Math.Max(ability.War, ability.Leadership)));

    internal static AllocationCandidate Schedule(AllocationCandidate departure, TrainingPreparation preparation)
    {
        var deployment = (DeployUnitCommand)departure.Intent;
        return departure with
        {
            Intent = new DevelopCommand(Guid.Empty, departure.Intent.IssuerId, 0,
                new(departure.Intent.IssuerId, deployment.SettlementId, preparation.Trainers, DomesticActivity.Train)),
            Officers = preparation.Trainers, Continuation = new(departure, preparation.DepartureDays)
        };
    }
}
