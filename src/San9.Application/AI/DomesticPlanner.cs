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
                if (activity == DomesticActivity.Recruit && (city.Soldiers >= 10000 || city.Food < (city.Soldiers + 5000L) / 50)) continue;
                for (int count = people.Length; count > 0; count--)
                {
                    if (!Spend()) return Result(true);
                    if (activity != DomesticActivity.Train && city.Money < 50 * count + 100) continue;
                    candidates.Add(new(observation.Perspective.ObserverId, city.Id, people.Take(count).ToImmutableArray(), activity));
                }
            }
        }
        return Result(false);
    }
}
