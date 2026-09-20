using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record WomenVisitSite(
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] int VisitorOfficerId,
    [property: JsonRequired] ImmutableArray<int> RewardTacticIds,
    [property: JsonRequired] ImmutableArray<int> RequiredLeaderIds,
    [property: JsonRequired] PersistentEventKey? RequiredWorldKey,
    [property: JsonRequired] GameClock? EarliestScenarioStart,
    [property: JsonRequired] PersistentEventKey? AlternativeForceHistoryKey);

public sealed record WomenVisitSearchEventDefinition(
    [property: JsonRequired] ImmutableArray<WomenVisitSite> Sites,
    [property: JsonRequired] int PoliticsChanceDivisor,
    [property: JsonRequired] int MinimumBaseTrustExclusive,
    [property: JsonRequired] int MaximumTrustExclusive) : SearchEventDefinition;
