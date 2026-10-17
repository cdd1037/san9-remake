using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Application.AI;

/// <summary>Direct-corps inputs, with current abilities and already authorized relation priority.</summary>
internal sealed record OriginalDomesticInputs(int ForceId, uint Money, uint Food,
    long SalaryDemand, long RationDemand, bool RecruitmentAllowed,
    ImmutableArray<OriginalDomesticSite> Sites, ImmutableDictionary<int, OfficerAbilities> Abilities);

internal sealed record OriginalDomesticSite(DomesticSettlementObservation Settlement, bool IsCity, bool HasPriorityRelation);

internal enum OriginalDomesticEnd
{
    NoCandidates,
    PreparationFailed,
    ReserveReached,
    AttemptsExhausted,
}

internal enum OriginalDomesticAttempt
{
    PreparationFailed,
    Rejected,
    Submitted,
}

internal sealed record OriginalDomesticStep(int SettlementId, OriginalDomesticAttempt Outcome)
{
    internal bool ProbabilityFailed { get; init; }
}
internal sealed record OriginalDomesticResult(OriginalDomesticEnd End, ImmutableArray<OriginalDomesticStep> Steps);

/// <summary>
/// Original works 0–5. The host owns authority projection, planning randomness and public submission.
/// Candidate order belongs to this work; account and personnel availability are reread after feedback.
/// Evidence: research/ai.md, economy and schedule; adopted B13/B14/B18/B25/B28/B30.
/// </summary>
internal static class OriginalDomesticWork
{
    internal static OriginalDomesticResult Run(DomesticActivity activity, int attemptLimit,
        Func<OriginalDomesticInputs> capture, Func<int> nextPercent,
        Func<DevelopmentCommand, bool> submit)
    {
        if (!Enum.IsDefined(activity)) throw new ArgumentOutOfRangeException(nameof(activity));
        ArgumentOutOfRangeException.ThrowIfNegative(attemptLimit);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(nextPercent);
        ArgumentNullException.ThrowIfNull(submit);
        var steps = ImmutableArray.CreateBuilder<OriginalDomesticStep>();
        var initial = capture();
        var targets = initial.Sites.Where(site => Eligible(initial, site, activity))
            .OrderByDescending(site => Priority(site, activity))
            .Select(site => site.Settlement.Id).ToArray();
        for (int index = 0; index < targets.Length; index++)
        {
            if (steps.Count >= attemptLimit) return new(OriginalDomesticEnd.AttemptsExhausted, steps.ToImmutable());
            // Preparation pops one target. A failed preparation ends the entire work, without
            // spending a command attempt or silently skipping to a more convenient settlement.
            var current = capture();
            var site = current.Sites.FirstOrDefault(s => s.Settlement.Id == targets[index]);
            bool eligible = site is not null && Eligible(current, site, activity);
            var people = eligible
                ? SelectPeople(site!.Settlement.OfficerIds, current.Abilities, activity, nextPercent) : [];
            if (people.IsEmpty)
            {
                bool probabilityFailed = eligible &&
                    site!.Settlement.OfficerIds.Any(id => activity == DomesticActivity.Train || Ability(current.Abilities[id], activity) >= 35);
                steps.Add(new(targets[index], OriginalDomesticAttempt.PreparationFailed) { ProbabilityFailed = probabilityFailed });
                return new(OriginalDomesticEnd.PreparationFailed, steps.ToImmutable());
            }
            var command = new DevelopmentCommand(current.ForceId, targets[index], people, activity);
            bool accepted = submit(command);
            steps.Add(new(targets[index], accepted ? OriginalDomesticAttempt.Submitted : OriginalDomesticAttempt.Rejected));
            // There is no private money debit: only an accepted public command changes funds.
            // Ordinary development has an additional continuation reserve gate.
            if (activity <= DomesticActivity.Repair && index + 1 < targets.Length)
            {
                var after = capture();
                if ((long)after.Money < after.SalaryDemand + 250)
                    return new(OriginalDomesticEnd.ReserveReached, steps.ToImmutable());
            }
        }
        return new(OriginalDomesticEnd.NoCandidates, steps.ToImmutable());
    }

    private static bool Eligible(OriginalDomesticInputs input, OriginalDomesticSite site, DomesticActivity activity)
    {
        var city = site.Settlement;
        if (city.OfficerIds.IsDefaultOrEmpty || city.UnavailableActivities.Contains(activity)) return false;
        if (!site.IsCity && activity is not (DomesticActivity.Repair or DomesticActivity.Train)) return false;
        return activity switch
        {
            DomesticActivity.Patrol => city.PublicOrder < 1000 && HasDevelopmentFunds(input),
            DomesticActivity.Commerce => city.Commerce < city.MaxCommerce && HasDevelopmentFunds(input),
            DomesticActivity.Cultivation => city.Cultivation < city.MaxCultivation && HasDevelopmentFunds(input),
            DomesticActivity.Repair => city.Durability < city.MaxDurability && HasDevelopmentFunds(input),
            DomesticActivity.Recruit => input.RecruitmentAllowed && city.DraftPopulation > 0 && city.Soldiers < 500000 &&
                (city.Soldiers < 10000 || city.Morale >= 90) &&
                (long)input.Money >= input.SalaryDemand + 1000 &&
                (long)input.Food >= 3 * input.RationDemand / 2 + 1000,
            DomesticActivity.Train => city.Morale < 100 && city.Soldiers > 0,
            _ => false,
        };
    }

    private static bool HasDevelopmentFunds(OriginalDomesticInputs input) => (long)input.Money >= input.SalaryDemand + 250;

    private static long Priority(OriginalDomesticSite site, DomesticActivity activity)
    {
        var city = site.Settlement;
        return activity switch
        {
            DomesticActivity.Patrol => 1000L * city.DraftPopulation + Math.Max(0, 1000 - city.PublicOrder),
            DomesticActivity.Commerce => 1000L * city.DraftPopulation + Math.Max(0, 1000 - city.Commerce),
            DomesticActivity.Cultivation => 1000L * city.DraftPopulation + Math.Max(0, 1000 - city.Cultivation),
            DomesticActivity.Repair => (long)city.MaxDurability - city.Durability + (site.HasPriorityRelation ? 1000 : 0),
            DomesticActivity.Train => 100L - city.Morale + (site.HasPriorityRelation ? 100 : 0),
            DomesticActivity.Recruit => (long)city.DraftPopulation + (site.HasPriorityRelation ? 500000 : 0),
            _ => throw new ArgumentOutOfRangeException(nameof(activity)),
        };
    }

    private static ImmutableArray<int> SelectPeople(ImmutableArray<int> available,
        ImmutableDictionary<int, OfficerAbilities> abilities, DomesticActivity activity, Func<int> nextPercent)
    {
        // N is measured before ability filtering. OrderBy is stable, including ties after
        // the specialization adjustment; entity IDs are never used as a secondary score.
        int checks = Math.Max(1, available.Length / 2);
        var ordered = available.Where(id => activity == DomesticActivity.Train || Ability(abilities[id], activity) >= 35)
            .OrderByDescending(id => Adjusted(abilities[id], activity));
        if (activity == DomesticActivity.Recruit) return ordered.Take(5).ToImmutableArray();
        var selected = ImmutableArray.CreateBuilder<int>();
        foreach (int id in ordered.Take(checks))
        {
            int percent = nextPercent();
            if (percent is < 0 or >= 100) throw new InvalidOperationException("Planning percent must be in [0, 100).");
            var ability = abilities[id];
            int chance = activity == DomesticActivity.Patrol ? ability.Politics : Ability(ability, activity);
            // Even guaranteed success/failure consumes one planning draw.
            if (percent < chance) selected.Add(id);
            if (selected.Count == 5) break;
        }
        return selected.ToImmutable();
    }

    private static int Ability(OfficerAbilities ability, DomesticActivity activity) => activity switch
    {
        DomesticActivity.Patrol => ability.Intelligence,
        DomesticActivity.Commerce or DomesticActivity.Cultivation => ability.Politics,
        DomesticActivity.Train => ability.War,
        _ => ability.Leadership,
    };

    internal static long Adjusted(OfficerAbilities ability, DomesticActivity activity) => Ability(ability, activity) -
        Math.Min(((long)ability.War + ability.Intelligence + ability.Politics + ability.Leadership) / 46, 10);
}
