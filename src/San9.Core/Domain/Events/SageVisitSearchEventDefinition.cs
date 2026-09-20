using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public sealed record SageVisitSearchEventDefinition(
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] int SageOfficerId,
    [property: JsonRequired] ImmutableArray<int> RecommendedOfficerIds,
    [property: JsonRequired] int PoliticsChanceDivisor,
    [property: JsonRequired] int MinimumBaseTrustExclusive,
    [property: JsonRequired] int MaximumRecipientLoyaltyInclusive,
    [property: JsonRequired] int LoyaltyReward) : SearchEventDefinition;
