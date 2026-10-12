using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ArrivalBoundTests
{
    [Theory]
    [InlineData(1, 9)]
    [InlineData(10, 0)]
    [InlineData(17, 11)]
    public void ThreatBoundNeverArrivesAfterTheActualFrozenRoute(int budget, int remainder)
    {
        var map = new BattlefieldMap(12, 3, Enumerable.Range(0, 36).Select(i => i % 3 == 0 ? 1 : 0),
            [new(0, 10, null), new(1, 12, null)]);
        var travel = new UnitTravel(map, new(1, 1), TravelMode.Land, budget);
        var target = new MapOccupancy(new(47, 7), new(3, 3));
        var start = new WorldPosition(2, 7);
        var exact = NavigationArrivalForecast.Evaluate(new(travel, start, target, ImmutableArray<MapOccupancy>.Empty,
            PreparationDays: 10, MovementRemainder: remainder), new(1000000));
        Assert.Equal(ArrivalEstimateStatus.Known, exact.Status);
        Assert.True(NavigationArrivalForecast.EarliestDays(travel, start, target, 10) <= exact.Days);
        Assert.True(NavigationArrivalForecast.EarliestDays(travel, start, target, 10, remainder) <= exact.Days);
    }
}
