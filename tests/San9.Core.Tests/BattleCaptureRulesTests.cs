using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class BattleCaptureRulesTests
{
    [Theory]
    [InlineData(0, BattleRank.Front, false, 58)]
    [InlineData(0, BattleRank.Middle, false, 39)]
    [InlineData(0, BattleRank.Rear, false, 19)]
    [InlineData(80, BattleRank.Front, true, 69)]
    [InlineData(80, BattleRank.Middle, true, 46)]
    [InlineData(80, BattleRank.Rear, true, 23)]
    [InlineData(100, BattleRank.Middle, false, 6)]
    [InlineData(110, BattleRank.Middle, false, 0)]
    public void PreservesAbilityTransformAndRankTruncation(int war, BattleRank rank, bool doubleCapture, int expected)
    {
        Assert.Equal(expected, BattleCaptureRules.Chance(war, 1, rank, true, false, doubleCapture));
    }

    [Fact]
    public void MountPreventsProbabilisticCaptureButCannotCreateARetreatRoute()
    {
        Assert.Equal(0, BattleCaptureRules.Chance(0, 5, BattleRank.Front, true, true, true));
        Assert.Equal(100, BattleCaptureRules.Chance(100, 1, BattleRank.Rear, false, true, false));
        Assert.Equal(0, BattleCaptureRules.Chance(0, 0, BattleRank.Front, true, false, true));
    }

    [Fact]
    public void ExtremeInputsRemainBoundedAndHigherWarCannotIncreaseCaptureChance()
    {
        Assert.Equal(100, BattleCaptureRules.Chance(0, int.MaxValue, BattleRank.Front, true, false, true));
        Assert.Equal(0, BattleCaptureRules.Chance(int.MaxValue, int.MaxValue, BattleRank.Front, true, false, true));
        int previous = 100;
        for (int war = 0; war <= 200; war++)
        {
            int current = BattleCaptureRules.Chance(war, 2, BattleRank.Front, true, false, false);
            Assert.True(current <= previous);
            previous = current;
        }
    }

    [Fact]
    public void InvalidInputsAreRejectedEvenWhenCaptureWouldBeGuaranteed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BattleCaptureRules.Chance(-1, 1, BattleRank.Middle, false, false, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => BattleCaptureRules.Chance(80, -1, BattleRank.Middle, false, false, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => BattleCaptureRules.Chance(80, 1, (BattleRank)99, false, false, false));
    }
}
