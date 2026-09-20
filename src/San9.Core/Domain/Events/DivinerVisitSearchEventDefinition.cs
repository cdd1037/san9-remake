using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record DivinerVisitSearchEventDefinition(
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] int DivinerOfficerId,
    [property: JsonRequired] int PoliticsChanceDivisor,
    [property: JsonRequired] int MinimumTrustExclusive,
    [property: JsonRequired] PersistentEventKey HistoryKey) : SearchEventDefinition;
