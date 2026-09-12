using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record GovernorOfficeRule(int OfficeId, uint CommandCapacity, int Priority)
{
    public OfficerAbilities AbilityBonuses { get; init; }
    public int Salary { get; init; }
    public int? RequiredRankId { get; init; }
}

/// <summary>Appointment precedence is content, independent of office identity and source table order.</summary>
public sealed class GovernanceRulebook
{
    public const int MaximumCorpsPerForce = 8;
    public GovernanceRulebook(IEnumerable<GovernorOfficeRule> offices, IReadOnlyDictionary<int, int>? rankPrecedence = null)
    {
        Offices = offices.ToImmutableDictionary(o => o.OfficeId);
        RankPrecedence = (rankPrecedence ?? ImmutableDictionary<int, int>.Empty).ToImmutableDictionary();
        if (Offices.Values.Any(o => o.OfficeId < 0 || o.Priority < 0 || o.Salary < 0 || !o.AbilityBonuses.IsNonnegative ||
            o.RequiredRankId is int rank && !RankPrecedence.ContainsKey(rank)))
            throw new ArgumentException("Invalid governor office rule.");
    }

    public ImmutableDictionary<int, GovernorOfficeRule> Offices { get; }
    public ImmutableDictionary<int, int> RankPrecedence { get; }

    public bool CanAppoint(int? forceRankId, GovernorOfficeRule office) => office.RequiredRankId is not int required ||
        forceRankId is int rank && RankPrecedence.TryGetValue(rank, out int precedence) && precedence >= RankPrecedence[required];

    public void ValidateOffices(IEnumerable<OfficerState> people)
    {
        if (people.Any(p => p.OfficeId is int officeId && !Offices.ContainsKey(officeId) || p.OfficeBonuses != (p.OfficeId is int id && Offices.TryGetValue(id, out var office)
            ? office.AbilityBonuses : default))) throw new ArgumentException("Invalid office ability state.");
    }

    public OfficerState? SelectSuccessor(IEnumerable<OfficerState> candidates, int previousLeaderId) => candidates
        .Where(p => p.IsServing).OrderByDescending(p => p.Status == OfficerStatus.Ruler)
        .ThenByDescending(p => p.Id == previousLeaderId).ThenByDescending(p => p.Status == OfficerStatus.Governor)
        .ThenByDescending(p => Office(p)?.CommandCapacity / 1000 ?? 0)
        .ThenByDescending(p => Office(p)?.Priority ?? 0).ThenByDescending(p => p.Abilities.Leadership)
        .ThenBy(p => p.Id).FirstOrDefault();

    public OfficerState? SelectCommander(IEnumerable<OfficerState> candidates) => candidates.Where(p => p.IsServing)
        .OrderByDescending(p => p.Status == OfficerStatus.Ruler).ThenByDescending(p => p.Status == OfficerStatus.Governor)
        .ThenByDescending(p => Office(p)?.CommandCapacity / 1000 ?? 10)
        .ThenByDescending(p => Office(p) is { CommandCapacity: > 10000 } office ? office.Priority : 0)
        .ThenByDescending(p => p.Abilities.Leadership).ThenBy(p => p.Id).FirstOrDefault();

    public OfficerState? SelectGovernor(IEnumerable<OfficerState> candidates)
    {
        return candidates.Where(p => p.IsServing && p.Status != OfficerStatus.Ruler)
            .OrderByDescending(p => p.Status == OfficerStatus.Governor)
            .ThenByDescending(p => Office(p)?.CommandCapacity / 1000 ?? 10)
            .ThenByDescending(p => Office(p) is { CommandCapacity: > 10000 } office ? office.Priority : 0)
            .ThenByDescending(p => p.Abilities.Leadership)
            .ThenBy(p => p.Id).FirstOrDefault();
    }

    private GovernorOfficeRule? Office(OfficerState person) => person.OfficeId is int id
        ? Offices.TryGetValue(id, out var office) ? office : throw new ArgumentException("Unknown appointment office.")
        : null;
}
