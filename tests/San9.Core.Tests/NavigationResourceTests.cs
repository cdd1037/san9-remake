using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class NavigationResourceTests
{
    [Fact]
    public void EquivalentTravelParametersShareTopologyWithoutMergingDailySpeeds()
    {
        var rules = new MilitaryRulebook(new(20, 20, new int[400].ToImmutableArray(),
            [new(0, 10, null), new(1, null, 10) { IsWater = true }],
            [new(1, 75, 0, 1000, 1, new(1, 1), TravelMode.Land),
             new(2, 150, 0, 1000, 1, new(1, 1), TravelMode.Land),
             new(3, 85, 0, 1000, 1, new(1, 1), TravelMode.Water),
             new(4, 170, 0, 1000, 1, new(1, 1), TravelMode.Water),
             new(5, 170, 0, 1000, 1, new(3, 3), TravelMode.Water)], []));
        var first = rules.Travel(1, 3);
        var faster = rules.Travel(2, 4);
        Assert.Same(first.Map, faster.Map);
        Assert.Equal(first.DailyBudget * 2, faster.DailyBudget);
        Assert.NotSame(first.Map, rules.Travel(2, 5).Map);
        Assert.NotSame(first.Map, rules.Travel(1, 4).Map);
        using var diagnostics = new PathSearchDiagnostics();
        var slow = first.Map.Navigate(new(10, 10), new(new(80, 10), new(1, 1)), false,
            first.Shape, first.Mode, _ => [], first.DailyBudget);
        var fast = faster.Map.Navigate(new(10, 10), new(new(80, 10), new(1, 1)), false,
            faster.Shape, faster.Mode, _ => [], faster.DailyBudget);
        Assert.Equal(1, diagnostics.TopologyBuilds);
        Assert.Equal(17, slow.Position.X);
        Assert.Equal(25, fast.Position.X);
    }

    [Fact]
    public void TimeUnitScalingPreservesWindowAndMovement()
    {
        (NavigationStep Step, MapOccupancy Area) Query(int scale)
        {
            var map = new BattlefieldMap(140, 140, new int[19600], [new(0, 10 * scale, null)]);
            MapOccupancy envelope = default;
            var step = map.Navigate(new(350, 350), new(new(600, 350), new(1, 1)), false, new(1, 1), TravelMode.Land,
                area => { envelope = area; return []; }, 75 * scale);
            return (step, envelope);
        }
        var first = Query(1);
        var scaled = Query(17);
        Assert.Equal(first.Area, scaled.Area);
        Assert.Equal(first.Step.Position, scaled.Step.Position);
        Assert.Equal(first.Step.Plan!.Steps.ToArray(), scaled.Step.Plan!.Steps.ToArray());
        Assert.Equal(first.Step.Remainder * 17, scaled.Step.Remainder);
    }

    [Fact]
    public void BudgetExhaustionDoesNotBecomeUnreachableOrPoisonLaterQueries()
    {
        var map = new BattlefieldMap(30, 30, new int[900], [new(0, 10, null)]);
        var target = new MapOccupancy(new(120, 120), new(1, 1));
        using var diagnostics = new PathSearchDiagnostics();
        Assert.Equal(RouteReachability.Incomplete, map.ReachabilityIntoRange(new(10, 10), target, new(1, 1),
            TravelMode.Land, [], new(1), 1));
        Assert.Equal(1, diagnostics.Expanded);
        Assert.Equal(RouteReachability.Reachable, map.ReachabilityIntoRange(new(10, 10), target, new(1, 1),
            TravelMode.Land, [], new(10000), 1));
        Assert.Equal(RouteReachability.Unreachable, map.ReachabilityIntoRange(new(10, 10), target, new(1, 1),
            TravelMode.Land, [new(new(75, 75), new(1, 150))], new(30000), 1));
    }

    [Fact]
    public void EmptyTopologyIsUnreachableAndZeroBudgetDoesNotBuildTopology()
    {
        var map = new BattlefieldMap(10, 10, new int[100], [new(0, null, 10) { IsWater = true }]);
        using var diagnostics = new PathSearchDiagnostics();
        Assert.Equal(NavigationStatus.Waiting, map.Navigate(new(10, 10), new(new(20, 20), new(1, 1)), false,
            new(1, 1), TravelMode.Land, _ => [], 0).Status);
        Assert.Equal(0, diagnostics.TopologyBuilds);
        Assert.Equal(NavigationStatus.Unreachable, map.Navigate(new(10, 10), new(new(20, 20), new(1, 1)), false,
            new(1, 1), TravelMode.Land, _ => [], 60).Status);
    }

    [Fact]
    public void CompletedBlockageSurvivesSerializationAndWakesOnChanges()
    {
        var map = new BattlefieldMap(20, 20, new int[400], [new(0, 10, null)]);
        MapOccupancy[] wall = [new(new(50, 50), new(1, 100))];
        var start = new WorldPosition(10, 50);
        var target = new MapOccupancy(new(70, 50), new(1, 1));
        var blocked = map.Navigate(start, target, true, new(1, 1), TravelMode.Land, _ => wall, 60);
        Assert.Equal(NavigationStatus.Waiting, blocked.Status);
        Assert.NotNull(blocked.Plan!.BlockedSearch);
        var plan = JsonSerializer.Deserialize<MovementPlan>(JsonSerializer.Serialize(blocked.Plan))!;
        Assert.True(plan.IsValidFrom(start));
        using var diagnostics = new PathSearchDiagnostics();
        for (int day = 0; day < 10; day++)
        {
            var waiting = map.Navigate(start, target, true, new(1, 1), TravelMode.Land, _ => wall, 60, plan);
            Assert.Equal(NavigationStatus.Waiting, waiting.Status);
            Assert.Equal(start, waiting.Position);
            plan = waiting.Plan!;
        }
        Assert.Equal(0, diagnostics.Queries);
        // Same target center with a changed footprint now offers an approach on this side of the wall.
        var resized = map.Navigate(start, target with { Footprint = new(60, 1) }, true, new(1, 1), TravelMode.Land, _ => wall, 60, plan);
        Assert.Equal(NavigationStatus.Moving, resized.Status);
        var released = map.Navigate(start, target, true, new(1, 1), TravelMode.Land, _ => [], 1000, plan);
        Assert.True(released.Arrived);
    }

    [Fact]
    public void ThreadWorkspaceReusesCapacityAcrossMapsAndAllowsNestedQueries()
    {
        var first = new BattlefieldMap(30, 30, new int[900], [new(0, 10, null)]);
        first.FindRoute(new(10, 10), new(20, 10), new(1, 1), TravelMode.Land, []);
        var second = new BattlefieldMap(15, 60, new int[900], [new(0, 10, null)]);
        using (var diagnostics = new PathSearchDiagnostics())
        {
            var route = second.FindRoute(new(10, 10), new(20, 10), new(1, 1), TravelMode.Land, []);
            Assert.Equal(100, route.Cost);
            Assert.Equal(0, diagnostics.WorkspaceGrowths);
        }
        var target = new MapOccupancy(new(60, 60), new(1, 1));
        var expected = first.AdvanceIntoRange(new(10, 10), target, new(1, 1), TravelMode.Land, [], 1000, 1);
        bool nested = false;
        var actual = first.AdvanceIntoRange(new(10, 10), target, new(1, 1), TravelMode.Land, [], 1000, _ =>
        {
            if (!nested)
            {
                nested = true;
                Assert.Equal(100, second.FindRoute(new(10, 10), new(20, 10), new(1, 1), TravelMode.Land, []).Cost);
            }
            return 1;
        }, maximumRange: 1);
        Assert.True(nested);
        Assert.Equal(expected, actual);
    }
}
