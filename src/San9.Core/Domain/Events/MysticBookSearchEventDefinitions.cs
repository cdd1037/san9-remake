using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public sealed record MysticBookOfferSearchEventDefinition(
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] int MysticOfficerId,
    [property: JsonRequired] int TreasureId,
    [property: JsonRequired] int LeadershipChanceDivisor,
    [property: JsonRequired] uint MinimumMoneyExclusive,
    [property: JsonRequired] uint AcceptCost,
    [property: JsonRequired] uint DeclineCost) : SearchEventDefinition;

public sealed record MysticBookReturnSearchEventDefinition(
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] int MysticOfficerId,
    [property: JsonRequired] int TreasureId,
    [property: JsonRequired] int LeadershipChanceDivisor,
    [property: JsonRequired] ImmutableArray<int> RewardTacticIds) : SearchEventDefinition;
