using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Content.Scenarios;

public sealed record OfficerInitialState(
    [property: JsonRequired] string NameKey,
    [property: JsonRequired] OfficerState State)
{
    [JsonRequired] public string? CourtesyNameKey { get; init; }
    [JsonRequired] public string? BiographyKey { get; init; }
}
