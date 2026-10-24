using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class TacticMathTests
{
    [Fact]
    public void IntelligenceGateUsesSegmentedAbilityAndTruncatesNegativeDivisionTowardZero()
    {
        Assert.Equal(94, TacticMath.IntelligenceSuccessChance(80, 80));
        Assert.Equal(92, TacticMath.IntelligenceSuccessChance(80, 81));
        Assert.Equal(100, TacticMath.IntelligenceSuccessChance(85, 80));
        Assert.Equal(82, TacticMath.IntelligenceSuccessChance(80, 85));
        Assert.Equal(0, TacticMath.IntelligenceSuccessChance(0, int.MaxValue));
        Assert.Equal(100, TacticMath.IntelligenceSuccessChance(int.MaxValue, 0));
    }

    [Fact]
    public void MartialMembersTruncateBeforeLinkDoubleAndTargetModifiers()
    {
        MartialTacticMember[] members = [new(13, 501, 80, false), new(16, 300, 60, true)];
        // T(80)=60, T(60)=40: 1706 + 1564/2 = 2488; final result 9703.
        Assert.Equal(new MartialTacticDamage(9703, 9703), TacticMath.MartialDamage(members, true, 15, true, 50000));
        Assert.Equal(new MartialTacticDamage(9703, 300), TacticMath.MartialDamage(members, true, 15, true, 300));
        Assert.Equal(new MartialTacticDamage(248, 248), TacticMath.MartialDamage(members, false, 1, false, 50000));
    }

    [Fact]
    public void IntelligenceEffectsMatchHandCalculatedCases()
    {
        Assert.Equal(7, TacticMath.ConfusionDays(500, 80, 10));
        Assert.Equal(2, TacticMath.ConfusionDays(500, 80, 3));
        Assert.Equal(0, TacticMath.ConfusionDays(500, 80, 0));
        Assert.Equal(9, TacticMath.TauntMoraleLoss(500, 80, 0, 10, 70));
        Assert.Equal(2, TacticMath.TauntMoraleLoss(500, 80, 0, 10, 2));
        Assert.Equal(1000u, TacticMath.Defection(500, 80, 10, 10, 10000, false));
        Assert.Equal(3000u, TacticMath.Defection(500, 80, 10, 10, 100000, true));
        Assert.Equal(1300u, TacticMath.IllusionDamage(500, 80, 13, 10, 10000, false));
        Assert.Equal(1400u, TacticMath.IllusionDamage(500, 80, 14, 10, 10000, true));
        Assert.Equal(2500u, TacticMath.TrapDamage(500, 80, 10, 10, 10000));
        Assert.Equal(18, TacticMath.InspireMorale(500, 80, 10));
        Assert.Equal(3450u, TacticMath.HealSoldiers(500, 80, 19, 5000, 10000));
    }

    [Fact]
    public void ZeroTargetsResistanceAndHealingCapacityCannotCreateSoldiers()
    {
        Assert.Equal(0u, TacticMath.TrapDamage(1000, 100, 12, 10, 0));
        Assert.Equal(0u, TacticMath.IllusionDamage(1000, 100, 13, 0, 10000, false));
        Assert.Equal(10u, TacticMath.TrapDamage(0, 0, 0, 10, 10));
        Assert.Equal(150u, TacticMath.TrapDamage(0, 0, 0, 3, 1000));
        Assert.Equal(200u, TacticMath.HealSoldiers(0, 0, 0, 200, 1000));
        Assert.Equal(10u, TacticMath.HealSoldiers(0, 0, 0, 200, 10));
        Assert.Equal(0u, TacticMath.HealSoldiers(0, 0, 0, 200, 0));
    }

    [Fact]
    public void WideLegalInputsSaturateWithoutOverflow()
    {
        int max = int.MaxValue;
        Assert.Equal(new MartialTacticDamage(uint.MaxValue, uint.MaxValue), TacticMath.MartialDamage([new(max, max, max, false)], true, 15, true, uint.MaxValue));
        Assert.Equal(uint.MaxValue, TacticMath.TrapDamage(max, max, 19, 10, uint.MaxValue));
        Assert.Equal(uint.MaxValue, TacticMath.Defection(max, max, 29, 10, uint.MaxValue, false));
        Assert.Equal(uint.MaxValue, TacticMath.HealSoldiers(max, max, 19, uint.MaxValue, uint.MaxValue));
        Assert.Equal(30, TacticMath.InspireMorale(max, max, 14));
    }

    [Fact]
    public void UnsupportedEffectsAndUndefinedDivisorsFailExplicitly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TacticMath.TauntMoraleLoss(0, 0, 10, 10, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => TacticMath.HealSoldiers(0, 0, 20, 100, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => TacticMath.ConfusionDays(0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TacticMath.TrapDamage(-1, 0, 0, 10, 100));
    }
}
