using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class NavigationLookaheadTests
{
    private static BattlefieldMap Map() => new(100, 30, new int[3000], [new(0, 10, null)]);

    [Fact]
    public void StageShortensSearchWithoutBecomingCommandArrival()
    {
        var map = Map();
        var target = new MapOccupancy(new(452, 52), new(1, 1));
        var first = map.Navigate(new(12, 52), target, false, new(1, 1), TravelMode.Land, [], 70);
        Assert.NotNull(first.Lookahead);
        long full;
        using (var diagnostics = new PathSearchDiagnostics())
        {
            map.Navigate(first.Position, target, false, new(1, 1), TravelMode.Land, [], 70);
            full = diagnostics.Expanded;
        }
        using (var diagnostics = new PathSearchDiagnostics())
        {
            var next = map.Navigate(first.Position, target, false, new(1, 1), TravelMode.Land, [], 70, lookahead: first.Lookahead);
            Assert.Equal(first.Lookahead, next.Lookahead);
            Assert.False(next.Arrived);
            Assert.True(diagnostics.Expanded < full);
        }
        var step = first;
        for (int day = 0; day < 100 && !step.Arrived; day++)
            step = map.Navigate(step.Position, target, false, new(1, 1), TravelMode.Land, [], 70, lookahead: step.Lookahead);
        Assert.True(step.Arrived);
        Assert.Equal(target.Position, step.Position);
        Assert.Null(step.Lookahead);
    }

    [Fact]
    public void RealArrivalStopsMovementBeforeStage()
    {
        var target = new MapOccupancy(new(202, 52), new(1, 1));
        var step = Map().Navigate(new(12, 52), target, false, new(1, 1), TravelMode.Land, [], 3000,
            lookahead: new(new(452, 52), target));
        Assert.True(step.Arrived);
        Assert.Equal(target.Position, step.Position);
        Assert.Equal(1900, step.Spent);
        Assert.Null(step.Lookahead);
    }

    [Fact]
    public void ChangedTargetAndBlockedStageReconsiderRealDestination()
    {
        var map = Map();
        var target = new MapOccupancy(new(452, 52), new(1, 1));
        var first = map.Navigate(new(12, 52), target, false, new(1, 1), TravelMode.Land, [], 70);
        var changed = new MapOccupancy(new(12, 102), new(1, 1));
        var expected = map.Navigate(first.Position, changed, false, new(1, 1), TravelMode.Land, [], 70);
        var actual = map.Navigate(first.Position, changed, false, new(1, 1), TravelMode.Land, [], 70, lookahead: first.Lookahead);
        Assert.Equal(expected.Position, actual.Position);
        Assert.Equal(expected.Lookahead, actual.Lookahead);
        MapOccupancy[] blocked = [new(first.Lookahead!.Position, new(3, 3))];
        var replanned = map.Navigate(first.Position, target, false, new(1, 1), TravelMode.Land, blocked, 70, lookahead: first.Lookahead);
        Assert.Equal(NavigationStatus.Moving, replanned.Status);
        Assert.NotEqual(first.Lookahead, replanned.Lookahead);
    }
}
