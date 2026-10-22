using San9.Application.AI;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class OriginalAiTerrainRoutesTests
{
    [Fact]
    public void RepeatedTerrainQueriesShareSearchOnlyWithinTheirOperation()
    {
        var map = new BattlefieldMap(5, 1, [0, 0, 0, 0, 0], [new(0, 1, null)]);
        var routes = new OriginalAiTerrainRoutes();
        using var diagnostics = new PathSearchDiagnostics();
        var first = routes.FindRoute(map, new(2, 2), new(22, 2), new(1, 1), TravelMode.Land);
        var repeated = routes.FindRoute(map, new(2, 2), new(22, 2), new(1, 1), TravelMode.Land);
        Assert.True(first.Reachable);
        Assert.Equal(first, repeated);
        Assert.Equal(1, diagnostics.Queries);

        var nextOperation = new OriginalAiTerrainRoutes();
        var next = nextOperation.FindRoute(map, new(2, 2), new(22, 2), new(1, 1), TravelMode.Land);
        Assert.Equal(first.Steps.ToArray(), next.Steps.ToArray());
        Assert.Equal(first.Cost, next.Cost);
        Assert.Equal(2, diagnostics.Queries);
    }

    [Fact]
    public void DifferentMapsModesAndEndpointAreasDoNotReuseAnotherRoute()
    {
        var open = new BattlefieldMap(7, 3, Enumerable.Repeat(0, 21), [new(0, 1, null)]);
        var divided = new BattlefieldMap(7, 3, Enumerable.Repeat(0, 21), [new(0, 1, null)],
            [new(3, 0), new(3, 1), new(3, 2)]);
        var routes = new OriginalAiTerrainRoutes();
        WorldPosition start = new(2, 7);
        foreach (var map in new[] { open, divided, open })
            foreach (var mode in new[] { TravelMode.Land, TravelMode.Water, TravelMode.Land })
                foreach (var target in new MapOccupancy[] { new(new(32, 7), new(1, 1)),
                    new(new(32, 7), new(35, 5)), new(new(12, 7), new(1, 1)) })
                {
                    var expected = map.FindRouteIntoRange(start, target, new(1, 1), mode, []);
                    var actual = routes.FindRouteIntoRange(map, start, target, new(1, 1), mode);
                    Assert.Equal(expected.Reachable, actual.Reachable);
                    Assert.Equal(expected.Cost, actual.Cost);
                    Assert.Equal(expected.Steps.ToArray(), actual.Steps.ToArray());
                }
    }
}
