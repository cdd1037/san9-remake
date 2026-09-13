using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record AllianceState(int TargetForceId, int LeaderForceId, ImmutableArray<int> MemberForceIds, int RemainingXun)
{
    public bool Protects(int first, int second) => first != second && MemberForceIds.Contains(first) && MemberForceIds.Contains(second);

    public void Validate(IReadOnlySet<int> eligibleForces)
    {
        if (RemainingXun <= 0 || MemberForceIds.IsDefault || MemberForceIds.Length < 2 ||
            !MemberForceIds.SequenceEqual(MemberForceIds.Distinct().Order()) ||
            !MemberForceIds.Contains(LeaderForceId) || MemberForceIds.Contains(TargetForceId) ||
            !eligibleForces.Contains(TargetForceId) || MemberForceIds.Any(id => !eligibleForces.Contains(id)))
            throw new ArgumentException("Invalid alliance target, leader, members or duration.");
    }
}
