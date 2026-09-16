using System.Text.Json.Serialization;

namespace San9.Content.Scenarios;

public sealed record AttackRequestInitialState(
    [property: JsonRequired] int RequestedForceId,
    [property: JsonRequired] int RequesterForceId,
    [property: JsonRequired] int TargetSettlementId,
    [property: JsonRequired] int RemainingXun);
