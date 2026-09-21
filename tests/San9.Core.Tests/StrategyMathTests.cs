using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class StrategyMathTests
{
    [Theory]
    [InlineData(80, 80, false, 7, 7)]
    [InlineData(80, 80, true, 3, 3)]
    [InlineData(99, 61, false, 35, 30)]
    [InlineData(99, 61, true, 17, 15)]
    [InlineData(40, 90, false, 0, 0)]
    public void OpposedThresholdsUseEffectiveAbilitiesAndFormationHalvesAfterCap(
        int actor, int target, bool defensive, int report, int disorder)
    {
        Assert.Equal(report, StrategyMath.FalseReportThreshold(actor, target, defensive));
        Assert.Equal(disorder, StrategyMath.DisorderThreshold(actor, target, defensive));
    }

    [Fact]
    public void FriendlyThresholdsPreserveSignedTruncationAndTacticBonus()
    {
        Assert.Equal(0, StrategyMath.InspireThreshold(29, false));
        Assert.Equal(-1, StrategyMath.InspireThreshold(27, false));
        Assert.Equal(55, StrategyMath.InspireThreshold(100, true));
        Assert.Equal(-1, StrategyMath.ReinforcementThreshold(29));
        Assert.Equal(87, StrategyMath.ReinforcementThreshold(100));
        Assert.Equal(1500u, StrategyMath.FailedReinforcement(100));
        Assert.Equal(15u, StrategyMath.FailedReinforcement(0));
        Assert.Equal(3000u, StrategyMath.FailedReinforcement(int.MaxValue));
    }

    [Fact]
    public void AlienateUsesRighteousnessAndGossipOnlyDefaultsWhenAdviserAbsent()
    {
        Assert.Equal(30, StrategyMath.AlienateThreshold(100, 70, 0, 0));
        Assert.Equal(-14, StrategyMath.AlienateThreshold(100, 70, 15, 0));
        Assert.Equal(1, StrategyMath.AlienateLoyaltyLoss(15, 0));
        Assert.Equal(6, StrategyMath.AlienateLoyaltyLoss(0, 2));
        Assert.Equal(20, StrategyMath.GossipThreshold(100, 100, null));
        Assert.Equal(40, StrategyMath.GossipThreshold(100, 100, 10));
        Assert.Equal(-10, StrategyMath.GossipThreshold(80, 100, 80));
    }

    [Theory]
    [InlineData(100, 70, 5, 0, 16)]
    [InlineData(100, 70, 5, 50, 26)]
    [InlineData(70, 71, 7, 0, 0)]
    [InlineData(70, 74, 7, 0, -1)]
    [InlineData(100, 70, 5, 4, 16)]
    [InlineData(100, 70, 5, 5, 17)]
    [InlineData(1000, 0, 15, 50, 10)]
    [InlineData(int.MaxValue, 0, 0, 50, 40)]
    [InlineData(0, int.MaxValue, 15, 0, -715827906)]
    public void JapaneseAlienateUsesBothBranchesAndTruncatesSignedDifference(int actor, int target, int righteousness, int distance, int expected)
    {
        Assert.Equal(expected, StrategyMath.AlienateThreshold(actor, target, righteousness, distance));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(51)]
    public void AlienateRejectsInvalidAffinityDistance(int distance) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StrategyMath.AlienateThreshold(100, 70, 5, distance));

    [Fact]
    public void BuildingThresholdsRetainDistinctIntegerOperationOrder()
    {
        Assert.Equal(0, StrategyMath.BurnThreshold(100, 50, 100));
        Assert.Equal(47, StrategyMath.BurnThreshold(100, 50, 101));
        Assert.Equal(50, StrategyMath.BurnThreshold(100, 0, 101));
        Assert.Equal(-20, StrategyMath.BurnThreshold(0, 0, 101));
        Assert.Equal(17, StrategyMath.SeizeMoneyThreshold(80, 50, 20999));
        Assert.Equal(25, StrategyMath.SeizeMoneyThreshold(100, 0, 0));
    }

    [Fact]
    public void EffectsAreBoundedAndDoNotCountWoundedAsLostSoldiers()
    {
        Assert.Equal(17, StrategyMath.BurnDurabilityLoss(100, 300, 0));
        Assert.Equal(0, StrategyMath.BurnDurabilityLoss(0, 101, 0));
        Assert.Equal(0, StrategyMath.BurnDurabilityLoss(100, 5, 4));
        Assert.Equal(10, StrategyMath.BurnDurabilityLoss(100, 20, 4));
        Assert.Equal(1549u, StrategyMath.BurnFoodLoss(100, 30000, 0, 2000, 49));
        Assert.Equal(100u, StrategyMath.BurnFoodLoss(100, 30000, 0, 100, 49));
        Assert.Equal(484u, StrategyMath.SeizeMoney(30000, 4000, 10000, 49));
        Assert.Equal(100u, StrategyMath.SeizeMoney(30000, 4000, 100, 49));
    }
}
