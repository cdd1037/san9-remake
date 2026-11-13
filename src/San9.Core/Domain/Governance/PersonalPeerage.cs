using System.Collections.Immutable;

namespace San9.Core.Domain;

/// <summary>Personal titles are separate from offices and a force's political rank.</summary>
public enum PersonalPeerageGrade { ImperialMarquis, VillageMarquis, DistrictMarquis, CountyMarquis, Duke, King }

public sealed record PersonalPeerage(PersonalPeerageGrade Grade, string? Title)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Enum.IsDefined(Grade) && (Title is null || !string.IsNullOrWhiteSpace(Title));
}

public sealed record PersonalPeerageGradeRule(PersonalPeerageGrade Grade, int Salary, int LoyaltyBonus, int? Limit);
public sealed record PersonalPeerageRegimeDefinition(string Id, ImmutableArray<PersonalPeeragePeriod> Periods)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }
}

/// <summary>An explicitly selected regime period; political ranks and royal lineages are content identities.</summary>
public sealed record PersonalPeeragePeriod(int StartYear, int EndYear,
    ImmutableDictionary<PersonalPeerageGrade, ImmutableArray<int>> AuthorizedRanks,
    ImmutableArray<int> RoyalBloodlines)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<PersonalPeerageGradeRule>? GradeRules { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? RevocationPenalty { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => StartYear >= 0 && EndYear >= StartYear && AuthorizedRanks is not null &&
        !RoyalBloodlines.IsDefault && RoyalBloodlines.All(id => id >= 0) &&
        RoyalBloodlines.Distinct().Count() == RoyalBloodlines.Length &&
        AuthorizedRanks.All(pair => Enum.IsDefined(pair.Key) && !pair.Value.IsDefault &&
            pair.Value.All(id => id >= 0) && pair.Value.Distinct().Count() == pair.Value.Length) &&
        RevocationPenalty is not < 0 && (GradeRules is not { } grades || !grades.IsDefault && grades.Length == Enum.GetValues<PersonalPeerageGrade>().Length &&
            grades.All(r => r is not null && Enum.IsDefined(r.Grade) && r.Salary >= 0 && r.LoyaltyBonus >= 0 && r.Limit is not < 0) &&
            grades.Select(r => r.Grade).Distinct().Count() == grades.Length);

    public PersonalPeeragePolicy Policy(PersonalPeeragePolicy fallback) => GradeRules is not null || RevocationPenalty is not null
        ? new PersonalPeeragePolicy(GradeRules is { } configured ? configured : fallback.Grades.Values,
            RevocationPenalty ?? fallback.RevocationPenalty) : fallback;
}

public sealed class PersonalPeerageRegime
{
    public ImmutableArray<PersonalPeeragePeriod> Periods { get; }
    public PersonalPeerageRegime(IEnumerable<PersonalPeeragePeriod> periods)
    {
        ArgumentNullException.ThrowIfNull(periods);
        var values = periods.ToArray();
        if (values.Any(p => p is null || !p.IsValid)) throw new ArgumentException("Invalid personal peerage regime periods.");
        Periods = values.OrderBy(p => p.StartYear).ToImmutableArray();
        if (
            Periods.Zip(Periods.Skip(1)).Any(pair => pair.First.EndYear >= pair.Second.StartYear))
            throw new ArgumentException("Invalid personal peerage regime periods.");
    }
    public PersonalPeeragePeriod? Period(int year) => Periods.FirstOrDefault(p => p.StartYear <= year && year <= p.EndYear);
}

/// <summary>Pure policy for a specific regime and period; authority is supplied by the caller.</summary>
public sealed class PersonalPeeragePolicy
{
    /// <summary>Default game balance; a regime can provide different grade rules.</summary>
    public static PersonalPeeragePolicy Default { get; } = new([
        new(PersonalPeerageGrade.ImperialMarquis, 1, 2, null),
        new(PersonalPeerageGrade.VillageMarquis, 2, 4, 12),
        new(PersonalPeerageGrade.DistrictMarquis, 4, 6, 8),
        new(PersonalPeerageGrade.CountyMarquis, 6, 8, 4),
        new(PersonalPeerageGrade.Duke, 10, 10, 2),
        new(PersonalPeerageGrade.King, 15, 12, 1)]);
    public ImmutableDictionary<PersonalPeerageGrade, PersonalPeerageGradeRule> Grades { get; }
    public int RevocationPenalty { get; }

    public int Salary(PersonalPeerage? title) => title is null ? 0 : Grades.TryGetValue(title.Grade, out var rule)
        ? rule.Salary : throw new ArgumentException("Unknown personal peerage grade.");

    public PersonalPeeragePolicy(IEnumerable<PersonalPeerageGradeRule> grades, int revocationPenalty = 10)
    {
        ArgumentNullException.ThrowIfNull(grades);
        var definitions = grades.ToArray();
        if (revocationPenalty < 0 || definitions.Any(r => r is null || !Enum.IsDefined(r.Grade) ||
            r.Salary < 0 || r.LoyaltyBonus < 0 || r.Limit is < 0) ||
            definitions.Select(r => r.Grade).Distinct().Count() != definitions.Length)
            throw new ArgumentException("Invalid personal peerage policy.");
        Grades = definitions.ToImmutableDictionary(r => r.Grade);
        RevocationPenalty = revocationPenalty;
    }

    public bool CanGrant(PersonalPeerageGrade grade, int currentCount, bool hasAuthority, bool eligibleRoyalBlood)
    {
        if (currentCount < 0 || !hasAuthority || !Grades.TryGetValue(grade, out var rule) ||
            grade == PersonalPeerageGrade.King && !eligibleRoyalBlood) return false;
        return rule.Limit is not int limit || currentCount < limit;
    }

    public int LoyaltyChange(PersonalPeerageGrade? previous, PersonalPeerageGrade? next)
    {
        if (previous == next) return 0;
        if (next is null) return previous is null ? 0 : -RevocationPenalty;
        if (!Grades.TryGetValue(next.Value, out var rule)) throw new ArgumentException("Unknown peerage grade.");
        return previous is null || next.Value > previous.Value ? rule.LoyaltyBonus : 0;
    }
}
