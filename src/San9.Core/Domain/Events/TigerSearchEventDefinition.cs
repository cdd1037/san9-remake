using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public sealed record TigerSearchEventDefinition(
    [property: JsonRequired] ImmutableArray<int> ProvinceIds,
    [property: JsonRequired] int WarChanceDivisor,
    [property: JsonRequired] int MaximumBaseTrustExclusive,
    [property: JsonRequired] int MaximumInjuryExclusive,
    [property: JsonRequired] int MinimumBattleChance,
    [property: JsonRequired] int WarReward,
    [property: JsonRequired] int BaseTrustReward,
    [property: JsonRequired] int BaseTrustPenalty,
    [property: JsonRequired] int DefeatInjury) : SearchEventDefinition;
