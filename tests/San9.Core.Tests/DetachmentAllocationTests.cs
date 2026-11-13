using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class DetachmentAllocationTests
{
    [Fact]
    public void MeleeLossUsesFrontRanksBeforeRearAndConservesSoldiers()
    {
        var unit = new UnitState(1, 1, new(1, 1), 100)
        {
            MemberPositions = new Dictionary<int, FormationPosition>
            {
                [1] = new(0, BattleRank.Front), [2] = new(1, BattleRank.Front), [3] = new(0, BattleRank.Rear)
            }.ToImmutableDictionary(),
            Detachments = new Dictionary<int, DetachmentState>
            {
                [1] = new(1, 1, 30), [2] = new(1, 1, 20), [3] = new(2, 1, 50)
            }.ToImmutableDictionary()
        };
        var loss = DetachmentAllocation.ApplyMeleeLoss(unit, 60);
        Assert.Equal(0u, loss[1].Soldiers);
        Assert.Equal(0u, loss[2].Soldiers);
        Assert.Equal(40u, loss[3].Soldiers);
    }

    [Fact]
    public void ResizePreservesTotalAndUsesStableRemainder()
    {
        var units = new Dictionary<int, DetachmentState>
        {
            [2] = new(1, 1, 1), [1] = new(1, 1, 1), [3] = new(1, 1, 1)
        }.ToImmutableDictionary();
        var result = DetachmentAllocation.Resize(units, 2);
        Assert.Equal(1u, result[1].Soldiers);
        Assert.Equal(1u, result[2].Soldiers);
        Assert.Equal(0u, result[3].Soldiers);
    }

    [Fact]
    public void TacticStrengthScalesWithAssignedSoldiers()
    {
        var baseline = new MartialTacticMember(100, 0, 50, false);
        var full = TacticMath.MartialDamage([baseline], false, 10, false, 100000).Calculated;
        var half = TacticMath.MartialDamage([baseline with { StrengthNumerator = 1, StrengthDenominator = 2 }],
            false, 10, false, 100000).Calculated;
        Assert.Equal(full / 2, half);
    }
}
