using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public sealed record ScholarInsultSearchEventDefinition(
    [property: JsonRequired] int ScholarOfficerId,
    [property: JsonRequired] int IntelligenceChanceDivisor,
    [property: JsonRequired] int MaximumBaseTrustExclusive,
    [property: JsonRequired] int MaximumInjuryExclusive,
    [property: JsonRequired] int TrustRewardIntelligenceThreshold,
    [property: JsonRequired] int IntelligenceReward,
    [property: JsonRequired] int BaseTrustReward,
    [property: JsonRequired] int RetaliationInjury,
    [property: JsonRequired] int BaseTrustPenalty) : SearchEventDefinition;
