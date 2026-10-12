using San9.Application.AI;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ArrivalAnalysisTests
{
    [Fact]
    public void OnePathCostServesDifferentDepartureTimesRemaindersAndSpeeds()
    {
        var map = new BattlefieldMap(10, 3, Enumerable.Repeat(0, 30), [new(0, 1, null)]);
        var input = new NavigationArrivalInput(new(map, new(1, 1), TravelMode.Land, 10), new(2, 7), new(new(42, 7), new(1, 1)), []);
        var cache = new ArrivalProjectionCache();
        using var navigation = new PathSearchDiagnostics();
        Assert.Equal(new(ArrivalEstimateStatus.Known, 4), cache.Evaluate(input, new(65536)));
        Assert.Equal(1, navigation.Queries);
        Assert.Equal(new(ArrivalEstimateStatus.Known, 16), cache.Evaluate(input with { PreparationDays = 12 }, new(65536)));
        Assert.Equal(new(ArrivalEstimateStatus.Known, 3), cache.Evaluate(input with { MovementRemainder = 9 }, new(65536)));
        Assert.Equal(new(ArrivalEstimateStatus.Known, 2), cache.Evaluate(input with { Travel = input.Travel with { DailyBudget = 20 } }, new(65536)));
        Assert.Equal(1, navigation.Queries);
        var detour = cache.Evaluate(input with { Waypoint = new(2, 12) }, new(65536));
        Assert.Equal(new(ArrivalEstimateStatus.Known, 5), detour);
        Assert.Equal(3, navigation.Queries);
    }

    [Fact]
    public void TerrainDetoursRemainRelevantToTheCheapEstimate()
    {
        var blocked = new[] { new MapCell(5, 0), new MapCell(5, 1) };
        var map = new BattlefieldMap(10, 3, Enumerable.Repeat(0, 30), [new(0, 1, null)], blocked);
        var input = new NavigationArrivalInput(new(map, new(1, 1), TravelMode.Land, 10), new(2, 2), new(new(42, 2), new(1, 1)), []);
        var result = new ArrivalProjectionCache().Evaluate(input, new(65536));
        Assert.Equal(ArrivalEstimateStatus.Known, result.Status);
        Assert.True(result.Days > NavigationArrivalForecast.EarliestDays(input.Travel, input.Start, input.Target, movementRemainder: 0));
        Assert.Equal(ArrivalEstimateStatus.Unreachable, new ArrivalProjectionCache().Evaluate(input with
        {
            Occupancy = [new(new(27, 12), new(5, 5))]
        }, new(65536)).Status);
    }

    [Fact]
    public void RepeatedAnalysisReusesResultsButChangedOccupancyRebuildsTheRoute()
    {
        var map = new BattlefieldMap(10, 3, Enumerable.Repeat(0, 30), [new(0, 1, null)]);
        var input = new NavigationArrivalInput(new(map, new(1, 1), TravelMode.Land, 10), new(2, 7), new(new(42, 7), new(1, 1)), []);
        var cache = new ArrivalProjectionCache();
        using var diagnostics = new PathSearchDiagnostics();
        Assert.Equal(ArrivalEstimateStatus.Incomplete, cache.Evaluate(input, new(1)).Status);
        long incompleteQueries = diagnostics.Queries;
        Assert.Equal(ArrivalEstimateStatus.Incomplete, cache.Evaluate(input, new(1)).Status);
        Assert.Equal(incompleteQueries, diagnostics.Queries);
        var known = cache.Evaluate(input, new(65536));
        Assert.Equal(ArrivalEstimateStatus.Known, known.Status);
        long knownQueries = diagnostics.Queries;
        Assert.Equal(known, cache.Evaluate(input, new(65536)));
        Assert.Equal(knownQueries, diagnostics.Queries);
        var blocked = input with { Occupancy = [new(new(25, 7), new(1, 15))] };
        Assert.NotEqual(ArrivalEstimateStatus.Known, cache.Evaluate(blocked, new(65536)).Status);
        Assert.True(diagnostics.Queries > knownQueries);
    }
}
