namespace San9.Content.Scenarios;

public sealed record AttackRequestInitialState(
    [property: System.Text.Json.Serialization.JsonRequired] int RequestedForceId,
    [property: System.Text.Json.Serialization.JsonRequired] int RequesterForceId,
    [property: System.Text.Json.Serialization.JsonRequired] int TargetSettlementId,
    [property: System.Text.Json.Serialization.JsonRequired] int RemainingXun);
