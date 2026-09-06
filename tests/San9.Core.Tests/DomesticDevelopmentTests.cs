using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class DomesticDevelopmentTests
{
    [Fact]
    public void RepairCanDoubleBeyondFundsWhileChargingOnlyPerParticipant()
    {
        DomesticDevelopmentResult result = DomesticDevelopment.CalculateResult(
            DomesticActivity.Repair, 0, 1000, 50, [new(1, 1, 1, 1000)], doubled: true);
        Assert.Equal(new DomesticDevelopmentResult(50, 100, 50), result);
    }

    [Theory]
    [InlineData(false, 7)]
    [InlineData(true, 14)]
    public void FinalIncrementUsesExplicitBonusAndFixedCost(bool doubled, int expected)
    {
        DomesticDevelopmentResult result = DomesticDevelopment.CalculateResult(
            DomesticActivity.Commerce, 0, 100, 50, [new(100, 100, 100, 100)], doubled);
        Assert.Equal(new DomesticDevelopmentResult(7, expected, 50), result);
    }

    [Fact]
    public void FinalIncrementSaturatesAndUnaffordableResultIsRejected()
    {
        CurrentAbilities[] people = [new(1000, 1000, 1000, 1000)];
        Assert.Equal(10, DomesticDevelopment.CalculateResult(DomesticActivity.Repair, 90, 100, 50, people, true).Increment);
        Assert.Throws<ArgumentException>(() => DomesticDevelopment.CalculateResult(DomesticActivity.Repair, 90, 100, 49, people, true));
        Assert.Equal(int.MaxValue, DomesticDevelopment.CalculateResult(DomesticActivity.Commerce, 0, int.MaxValue, int.MaxValue,
            [new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)], true).Increment);
    }

    [Theory]
    [InlineData(DomesticActivity.Patrol, 4)]
    [InlineData(DomesticActivity.Commerce, 13)]
    [InlineData(DomesticActivity.Cultivation, 13)]
    [InlineData(DomesticActivity.Repair, 19)]
    public void UsesTheCorrespondingCurrentAbility(DomesticActivity activity, int expected)
    {
        Assert.Equal(expected, Calculate(activity, 100, 1000, new(1, 100, 200, 300)));
    }

    [Theory]
    [InlineData(DomesticActivity.Patrol, 99, 2)]
    [InlineData(DomesticActivity.Patrol, 100, 4)]
    [InlineData(DomesticActivity.Commerce, 99, 4)]
    [InlineData(DomesticActivity.Commerce, 100, 7)]
    [InlineData(DomesticActivity.Commerce, 0, 0)]
    [InlineData(DomesticActivity.Commerce, 1, 1)]
    public void PreservesIntegerBoundaries(DomesticActivity activity, int missing, int expected)
    {
        Assert.Equal(expected, Calculate(activity, missing, 1000, new(100, 100, 100, 100)));
    }

    [Fact]
    public void RepairLimitsOutputByFundsWithoutChangingSquareRootInput()
    {
        Assert.Equal(3, Calculate(DomesticActivity.Repair, 100, 3, new(100, 100, 100, 100)));
        Assert.Equal(0, Calculate(DomesticActivity.Repair, 100, 0, new(100, 100, 100, 100)));
        Assert.Equal(7, Calculate(DomesticActivity.Commerce, 100, 0, new(100, 100, 100, 100)));
    }

    [Fact]
    public void MultipleParticipantsAreOrderIndependentAndDoNotApplyBonuses()
    {
        CurrentAbilities[] people = [new(1, 1, 100, 1), new(1, 1, 50, 1)];
        Assert.Equal(8, DomesticDevelopment.CalculateBaseEffect(DomesticActivity.Commerce, 0, 100, 1000, people));
        Assert.Equal(8, DomesticDevelopment.CalculateBaseEffect(DomesticActivity.Commerce, 0, 100, 1000, people.Reverse().ToArray()));
    }

    [Theory]
    [InlineData(DomesticActivity.Patrol)]
    [InlineData(DomesticActivity.Commerce)]
    [InlineData(DomesticActivity.Cultivation)]
    [InlineData(DomesticActivity.Repair)]
    public void LargeInputsSaturateWithoutOverflow(DomesticActivity activity)
    {
        CurrentAbilities[] people = Enumerable.Repeat(new CurrentAbilities(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue), 5).ToArray();
        Assert.Equal(int.MaxValue, DomesticDevelopment.CalculateBaseEffect(activity, 0, int.MaxValue, int.MaxValue, people));
    }

    [Fact]
    public void RejectsInvalidInputs()
    {
        CurrentAbilities[] people = [new(100, 100, 100, 100)];
        Assert.Throws<ArgumentOutOfRangeException>(() => DomesticDevelopment.CalculateBaseEffect((DomesticActivity)999, 0, 100, 100, people));
        Assert.Throws<ArgumentOutOfRangeException>(() => DomesticDevelopment.CalculateBaseEffect(DomesticActivity.Commerce, -1, 100, 100, people));
        Assert.Throws<ArgumentOutOfRangeException>(() => DomesticDevelopment.CalculateBaseEffect(DomesticActivity.Commerce, 101, 100, 100, people));
        Assert.Throws<ArgumentOutOfRangeException>(() => DomesticDevelopment.CalculateBaseEffect(DomesticActivity.Commerce, 0, 100, -1, people));
        Assert.Throws<ArgumentOutOfRangeException>(() => DomesticDevelopment.CalculateBaseEffect(DomesticActivity.Commerce, 0, 100, 100, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => DomesticDevelopment.CalculateBaseEffect(DomesticActivity.Commerce, 0, 100, 100, Enumerable.Repeat(people[0], 6).ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() => DomesticDevelopment.CalculateBaseEffect(DomesticActivity.Commerce, 0, 100, 100, [new(0, 0, -1, 0)]));
    }

    private static int Calculate(DomesticActivity activity, int missing, int funds, CurrentAbilities abilities) =>
        DomesticDevelopment.CalculateBaseEffect(activity, 0, missing, funds, [abilities]);
}
