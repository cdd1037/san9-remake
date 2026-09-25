using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Content.Scenarios;

public sealed record OfficerInitialState(
    [property: JsonRequired] string NameKey,
    [property: JsonRequired] OfficerState State)
{
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
