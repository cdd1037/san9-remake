using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class CombatApproachTests
{
    [Fact]
    public void LandTroopsCanApproachWaterTargetWithoutEnteringItsTerrainOrFootprint()
    {
        var map = new BattlefieldMap(2, 1, [0, 1], [new(0, 1, null), new(1, null, 1)]);
        var target = new MapOccupancy(new(5, 2), new(1, 1));
        Assert.Null(map.PassageCost(target.Position, new(1, 1), TravelMode.Land, []));
        var result = map.AdvanceIntoRange(new(0, 2), target, new(1, 1), TravelMode.Land, [], 100, 1);
        Assert.True(result.Arrived);
        Assert.Equal(new WorldPosition(4, 2), result.Position);
        Assert.Equal(4, result.Spent);
        Assert.False(new MapOccupancy(result.Position, new(1, 1)).Overlaps(target));
    }

    [Fact]
    public void MovingTargetReplansAndRangeCannotSpendMoreThanDailyBudget()
    {
        var map = new BattlefieldMap(4, 2, Enumerable.Repeat(0, 8), [new(0, 2, null)]);
        var target = new MapOccupancy(new(15, 2), new(3, 3));
        var first = map.AdvanceIntoRange(new(2, 2), target, new(3, 3), TravelMode.Land, [], 5, 1);
        Assert.Equal(4, first.Spent);
        Assert.False(first.Arrived);
        var relocated = target with { Position = new(15, 7) };
        var second = map.AdvanceIntoRange(first.Position, relocated, new(3, 3), TravelMode.Land, [], 100, 1);
        Assert.True(second.Arrived);
        var area = new MapOccupancy(second.Position, new(3, 3));
        Assert.Equal(1, area.DistanceTo(relocated));
        Assert.False(area.Overlaps(relocated));
        Assert.NotEqual(first.Position, second.Position);
    }

    [Fact]
    public void BlockedApproachWaitsAndAlreadyInRangeDoesNotMove()
    {
        var map = new BattlefieldMap(3, 1, [0, 0, 0], [new(0, 1, null)]);
        var target = new MapOccupancy(new(12, 2), new(1, 1));
        var blocked = map.AdvanceIntoRange(new(2, 2), target, new(1, 1), TravelMode.Land,
            [new(new(7, 2), new(1, 5))], 100, 1);
        Assert.False(blocked.Arrived);
        Assert.True(blocked.Position.X < 7);
        Assert.True(blocked.Reachable);
        var arrived = map.AdvanceIntoRange(new(11, 2), target, new(1, 1), TravelMode.Land, [], 100, 1);
        Assert.Equal(new DailyMovement(new(11, 2), true, true, 0), arrived);
        Assert.Throws<ArgumentOutOfRangeException>(() => map.AdvanceIntoRange(new(2, 2), target, new(1, 1), TravelMode.Land, [], 100, 0));
    }
}
