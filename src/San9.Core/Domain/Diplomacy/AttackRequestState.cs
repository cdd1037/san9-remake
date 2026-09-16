using System.Text.Json.Serialization;

namespace San9.Core.Domain;

/// <summary>A directed obligation; target ownership records the agreement, not a live lookup.</summary>
public sealed record AttackRequestState(
    [property: JsonRequired] int RequestedForceId,
    [property: JsonRequired] int RequesterForceId,
    [property: JsonRequired] int TargetSettlementId,
    [property: JsonRequired] int TargetForceId,
    [property: JsonRequired] int RemainingXun)
{
    public bool Connects(int first, int second) => first != second &&
        (RequestedForceId == first && RequesterForceId == second || RequestedForceId == second && RequesterForceId == first);
}
