using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class UnitCorpsTransferTests
{
    private static UnitState Unit => new(1, 1, new(900, 900), 100) { ServicePlaceId = 2 };

    [Theory]
    [InlineData(UnitCondition.Normal, false)]
    [InlineData(UnitCondition.Disguised, true)]
    [InlineData(UnitCondition.Routed, true)]
    [InlineData(UnitCondition.Confused, false)]
    public void CurrentConditionOverridesOrderWithoutOverwritingTheStoredOrder(UnitCondition condition, bool follows)
    {
        var unit = Unit with { Condition = condition, OrderClass = UnitOrderClass.NonMilitary };
        Assert.Equal(follows, unit.FollowsServicePlace(true));
        Assert.Equal(follows, unit.FollowsServicePlace(false));
        Assert.Equal(UnitOrderClass.NonMilitary, unit.OrderClass);
        Assert.False((unit with { Condition = UnitCondition.Normal }).FollowsServicePlace(false));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, false, true)]
    public void PersonnelAndEmbeddedClassificationRemainIndependent(bool embedded, bool personnel, bool create, bool change)
    {
        var unit = Unit with { IsEmbedded = embedded, IsPersonnel = personnel };
        Assert.Equal(create, unit.FollowsServicePlace(true));
        Assert.Equal(change, unit.FollowsServicePlace(false));
    }

    [Fact]
    public void MilitaryOrderRequiresServiceReferenceAndIsIndependentOfPosition()
    {
        var unit = Unit with { OrderClass = UnitOrderClass.Military };
        Assert.True(unit.FollowsServicePlace(true));
        Assert.True((unit with { Position = new(0, 0) }).FollowsServicePlace(true));
        Assert.False((unit with { ServicePlaceId = null }).FollowsServicePlace(false));
        Assert.False((unit with { IsEmbedded = true }).FollowsServicePlace(true));
        Assert.True((unit with { IsEmbedded = true }).FollowsServicePlace(false));
    }
}
