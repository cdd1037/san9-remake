namespace San9.Core.Domain;

/// <summary>A directed obligation; target ownership records the agreement, not a live lookup.</summary>
public sealed record AttackRequestState(
    [property: System.Text.Json.Serialization.JsonRequired] int RequestedForceId,
    [property: System.Text.Json.Serialization.JsonRequired] int RequesterForceId,
    [property: System.Text.Json.Serialization.JsonRequired] int TargetSettlementId,
    [property: System.Text.Json.Serialization.JsonRequired] int TargetForceId,
    [property: System.Text.Json.Serialization.JsonRequired] int RemainingXun)
{
    public bool Connects(int first, int second) => first != second &&
        (RequestedForceId == first && RequesterForceId == second || RequestedForceId == second && RequesterForceId == first);
}
