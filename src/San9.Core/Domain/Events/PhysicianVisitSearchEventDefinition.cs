using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public sealed record PhysicianVisitSite(
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] int PhysicianOfficerId,
    [property: JsonRequired] int TreasureId);

public sealed record PhysicianVisitSearchEventDefinition(
    [property: JsonRequired] ImmutableArray<PhysicianVisitSite> Sites,
    [property: JsonRequired] int PoliticsChanceDivisor,
    [property: JsonRequired] int MinimumBaseTrustExclusive) : SearchEventDefinition;
