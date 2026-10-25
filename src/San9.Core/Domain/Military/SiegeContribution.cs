using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Domain;

/// <summary>One force's credit in the current siege; unit shares select the receiving corps.</summary>
public sealed record SiegeContribution(
    [property: JsonRequired] long Score,
    [property: JsonRequired] ImmutableDictionary<int, long> Units);
