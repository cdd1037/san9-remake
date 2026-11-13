using System.Text.Json.Serialization;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Content.Scenarios;

public enum PersonalPeerageEvidenceKind { Historical, Romance }
public enum FormationEvidenceKind { Historical, Romance, ExperienceInference, CommonFoundation }
public sealed record FormationEvidence([property: JsonRequired] int FormationId, [property: JsonRequired] int Proficiency,
    [property: JsonRequired] FormationEvidenceKind Kind, [property: JsonRequired] string Source, [property: JsonRequired] string Explanation)
{
    [JsonIgnore] public bool IsValid => FormationId >= 0 && Proficiency is >= 0 and <= 1000 && Enum.IsDefined(Kind) &&
        !string.IsNullOrWhiteSpace(Source) && !string.IsNullOrWhiteSpace(Explanation);
}
public sealed record PersonalPeerageEvidence(PersonalPeerage Peerage, GameClock From, GameClock? Until,
    PersonalPeerageEvidenceKind Kind, string Source)
{
    [JsonIgnore] public bool IsValid => Peerage is { IsValid: true } && From.Year > 0 && From.Month is >= 1 and <= 12 && From.Day is >= 1 and <= 30 &&
        (Until is null || Until.Value.Year > 0 && Until.Value.Month is >= 1 and <= 12 && Until.Value.Day is >= 1 and <= 30 && Until.Value.DayNumber >= From.DayNumber) &&
        Enum.IsDefined(Kind) && !string.IsNullOrWhiteSpace(Source);
    public bool Applies(GameClock clock) => IsValid && clock.DayNumber >= From.DayNumber && (Until is null || clock.DayNumber <= Until.Value.DayNumber);
}

public sealed record OfficerInitialState(
    [property: JsonRequired] string NameKey,
    [property: JsonRequired] OfficerState State)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PersonalPeerageEvidence? InitialPersonalPeerage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PersonalPeerageEvidence[]? InitialPersonalPeerageHistory { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FormationEvidence[]? InitialFormationEvidence { get; init; }
    [JsonIgnore] public IEnumerable<PersonalPeerageEvidence> PersonalPeerageEvidenceRecords =>
        (InitialPersonalPeerage is { } initial ? new[] { initial } : []).Concat(InitialPersonalPeerageHistory ?? []);
    // A year difference, not the inclusive age displayed by the UI. A value excludes a fixed State.BirthYear.
    [JsonRequired] public int? AgeAtScenarioStart { get; init; }

    public OfficerState CreateState(int startYear)
    {
        if (AgeAtScenarioStart is not int age) return State;
        if (age < 0 || age >= startYear || State.BirthYear != 0)
            throw new InvalidDataException("Relative birth requires a nonnegative age, a positive resulting year and no fixed birth year.");
        return State with { BirthYear = startYear - age };
    }

    [JsonRequired] public int FlagGlyph { get; init; } = -1;
    [JsonRequired] public string? CourtesyNameKey { get; init; }
    [JsonRequired] public string? BiographyKey { get; init; }
}
