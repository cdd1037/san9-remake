using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Application.AI;

public sealed record DomesticPlan(ImmutableArray<DevelopmentCommand> Candidates, long Evaluations, bool BudgetExhausted);

/// <summary>A bounded stage policy over authorized immutable inputs. The caller owns validation and command identity.</summary>
public static class DomesticPlanner
{
    public static DomesticPlan Plan(DomesticObservation observation, AiPlanningBudget budget, int turn, bool scripted = false)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentOutOfRangeException.ThrowIfNegative(turn);
        if (budget.MaxNodeEvaluations == 0) throw new ArgumentException("Domestic planning requires a node budget.", nameof(budget));
        var candidates = ImmutableArray.CreateBuilder<DevelopmentCommand>();
        long evaluations = 0;
        bool Spend()
        {
            if (evaluations >= budget.MaxNodeEvaluations) return false;
            evaluations++;
            return true;
        }
        DomesticPlan Result(bool exhausted) => new(candidates.ToImmutable(), evaluations, exhausted);
        foreach (var city in observation.Settlements)
        {
            if (!Spend()) return Result(true);
            var people = city.OfficerIds.Take(5).ToArray();
            if (people.Length == 0) continue;
            DomesticActivity[] priorities = scripted ? Enumerable.Range(0, 6).Select(i => (DomesticActivity)((turn % 6 + i) % 6)).ToArray() :
                [DomesticActivity.Cultivation, DomesticActivity.Commerce, DomesticActivity.Patrol, DomesticActivity.Recruit, DomesticActivity.Train, DomesticActivity.Repair];
            if (!scripted)
            {
                var urgent = city.Soldiers < 3000 ? DomesticActivity.Recruit : city.Morale < 65 ? DomesticActivity.Train :
                    city.PublicOrder < 500 ? DomesticActivity.Patrol : city.Durability < city.MaxDurability / 2 ? DomesticActivity.Repair : priorities[turn % priorities.Length];
                priorities = new[] { urgent }.Concat(priorities).Distinct().ToArray();
            }
            foreach (var activity in priorities)
            {
                if (!Spend()) return Result(true);
                if (city.UnavailableActivities.Contains(activity)) continue;
                if (activity == DomesticActivity.Recruit && (city.Soldiers >= 10000 || city.Food < (city.Soldiers + 5000L) / 50)) continue;
                if (!scripted && activity == DomesticActivity.Train)
                {
                    foreach (var trainers in TrainingCrews(city.OfficerIds, observation.Abilities))
                    {
                        if (!Spend()) return Result(true);
                        candidates.Add(new(observation.Perspective.ObserverId, city.Id, trainers, activity));
                    }
                    continue;
                }
                for (int count = people.Length; count > 0; count--)
                {
                    if (!Spend()) return Result(true);
                    // Recruitment pays for actual recruits; ordinary development costs exactly 50
                    // per participant and training is free.
                    if (activity == DomesticActivity.Recruit ? city.Money == 0 :
                        activity != DomesticActivity.Train && city.Money < 50 * count) continue;
                    candidates.Add(new(observation.Perspective.ObserverId, city.Id, people.Take(count).ToImmutableArray(), activity));
                }
            }
        }
        return Result(false);
    }

    internal static long Value(DevelopmentCommand command, DomesticObservation observation)
    {
        var city = observation.Settlements.Single(s => s.Id == command.SettlementId);
        var abilities = command.OfficerIds.Select(id => observation.Abilities.GetValueOrDefault(id)).ToArray();
        long opportunity = abilities.Sum(OpportunityCost);
        if (command.Activity == DomesticActivity.Train)
            return DomesticDevelopment.TrainingIncrement(city.Soldiers, city.Morale, observation.Trust, abilities) *
                (long)city.Soldiers / 100 - opportunity;
        if (command.Activity == DomesticActivity.Recruit)
            return (command.RecruitAmount ?? (uint)DomesticDevelopment.RecruitCount(city.DraftPopulation,
                city.Soldiers, city.SoldierCapacity, city.Money, abilities)) / 2L - opportunity;
        var (current, maximum) = command.Activity switch
        {
            DomesticActivity.Commerce => (city.Commerce, city.MaxCommerce),
            DomesticActivity.Cultivation => (city.Cultivation, city.MaxCultivation),
            DomesticActivity.Patrol => (city.PublicOrder, 1000),
            _ => (city.Durability, city.MaxDurability)
        };
        long gain = DomesticDevelopment.CalculateBaseEffect(command.Activity, current, maximum,
            (int)Math.Min(int.MaxValue, city.Money), abilities);
        return gain * (command.Activity is DomesticActivity.Commerce or DomesticActivity.Cultivation ? 9 : 2) -
            command.OfficerIds.Length * 50 - opportunity;
    }

    // Every officer is considered individually. Group seeds compare effect and opportunity cost;
    // the public five-participant limit bounds group size, not the pool of available officers.
    private static IEnumerable<ImmutableArray<int>> TrainingCrews(IEnumerable<int> available,
        ImmutableDictionary<int, OfficerAbilities> abilities)
    {
        var people = available.OrderByDescending(id => abilities.GetValueOrDefault(id).War).ThenBy(id => id).ToArray();
        foreach (int id in people) yield return [id];
        var economical = people.OrderBy(id => OpportunityCost(abilities.GetValueOrDefault(id)) - abilities.GetValueOrDefault(id).War)
            .ThenByDescending(id => abilities.GetValueOrDefault(id).War).ThenBy(id => id).ToArray();
        for (int count = 2; count <= Math.Min(5, people.Length); count++)
        {
            var strongest = people.Take(count).Order().ToImmutableArray();
            yield return strongest;
            var cheapest = economical.Take(count).Order().ToImmutableArray();
            if (!strongest.AsSpan().SequenceEqual(cheapest.AsSpan())) yield return cheapest;
        }
    }

    private static long OpportunityCost(OfficerAbilities ability) => Math.Max(ability.Politics,
        Math.Max(ability.Intelligence, Math.Max(ability.War, ability.Leadership)));
}
