using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record MerchantTradeCategory(
    [property: JsonRequired] string Id,
    [property: JsonRequired] ImmutableArray<int> PlaceIds,
    [property: JsonRequired] ResourceKind RewardResource,
    [property: JsonRequired] PersistentEventKey BanHistoryKey);
public sealed record MerchantInvestmentDefinition(
    [property: JsonRequired] ImmutableArray<MerchantTradeCategory> Categories,
    [property: JsonRequired] int PoliticsChanceDivisor,
    [property: JsonRequired] int MinimumBaseTrustExclusive,
    [property: JsonRequired] uint MinimumMoneyExclusive,
    [property: JsonRequired] uint MinimumInvestment,
    [property: JsonRequired] uint InvestmentBalanceDivisor,
    [property: JsonRequired] int MinimumSuccessChance,
    [property: JsonRequired] int ReturnNumerator,
    [property: JsonRequired] int ReturnDenominator,
    [property: JsonRequired] int PoliticsReturnMultiplier,
    [property: JsonRequired] int ExecutionTrustPenalty) : SearchEventDefinition;
