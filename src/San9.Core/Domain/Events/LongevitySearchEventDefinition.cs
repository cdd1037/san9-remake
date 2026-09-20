using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record LongevitySearchEventDefinition(
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] int DivinerOfficerId,
    [property: JsonRequired] PersistentEventKey RequiredHistoryKey,
    [property: JsonRequired] int IntelligenceChanceDivisor,
    [property: JsonRequired] uint MinimumMoneyExclusive,
    [property: JsonRequired] uint Cost,
    [property: JsonRequired] int MinimumLifespanTier,
    [property: JsonRequired] bool LifespanChangesAllowed) : SearchEventDefinition;
