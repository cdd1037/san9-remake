using System.Collections.Immutable;
using San9.Application.AI;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CampaignRoutePlannerTests
{
    private static NavigationArrivalInput Input()
    {
        var rules = new MilitaryRulebook(new(11, 5,
            Enumerable.Range(0, 55).Select(i => i / 11 > 0 && i % 11 is > 0 and < 10 ? 1 : 0).ToImmutableArray(),
            [new(0, 1, null), new(1, null, 1) { IsWater = true }],
            [new(0, 2, 0, 1000, 1, new(1, 1), TravelMode.Land),
             new(1, 20, 0, 1000, 1, new(1, 1), TravelMode.Water)], []));
        return new(rules.Travel(0, 1), new(2, 2), new(new(52, 2), new(1, 1)), []);
    }

    [Fact]
    public void HarborAlternativeIsDemandDrivenAndCarriesTheChosenWaypoint()
    {
        var input = Input();
        var cache = new ArrivalProjectionCache();
        var planner = new CampaignRoutePlanner([new(new(17, 12), new(1, 1))], cache);
        using var diagnostics = new PathSearchDiagnostics();
        var direct = planner.Choose(input, false, new(65536));
        Assert.Null(direct.Waypoint);
        Assert.Equal(1, diagnostics.Queries);
        var selected = planner.Choose(input, true, new(65536));
        Assert.NotNull(selected.Waypoint);
        Assert.True(input.Travel.Map.IsWater(selected.Waypoint.Value));
        Assert.True(selected.Arrival.Days < direct.Arrival.Days);
        Assert.Equal(3, diagnostics.Queries);
        var repeat = planner.Choose(input with { PreparationDays = 10 }, true, new(65536));
        Assert.Equal(selected.Waypoint, repeat.Waypoint);
        Assert.Equal(selected.Arrival.Days + 10, repeat.Arrival.Days);
        Assert.Equal(3, diagnostics.Queries);
        // The same waypoint is a real navigation destination, not just an ETA adjustment.
        var position = input.Start;
        int remainder = 0;
        bool reached = false;
        for (int day = 0; day < 100 && !reached; day++)
        {
            var step = input.Travel.Map.Navigate(position, new(selected.Waypoint.Value, new(1, 1)), false,
                input.Travel.Shape, input.Travel.Mode, input.Occupancy, input.Travel.DailyBudget + remainder);
            position = step.Position; remainder = step.Remainder; reached = step.Arrived;
        }
        Assert.True(reached);
        Assert.Equal(selected.Waypoint.Value, position);
    }

    [Fact]
    public void NoHarborOrNoOptionalBudgetRetainsDirectPlan()
    {
        var input = Input();
        var cache = new ArrivalProjectionCache();
        var direct = new CampaignRoutePlanner([], cache).Choose(input, true, new(65536));
        using var diagnostics = new PathSearchDiagnostics();
        var limited = new CampaignRoutePlanner([new(new(17, 12), new(1, 1))], cache).Choose(input, true, new(0));
        Assert.Equal(direct, limited);
        Assert.Equal(0, diagnostics.Queries);
        var blocked = input with { Occupancy = [new(new(25, 12), new(1, 25))] };
        var rejected = new CampaignRoutePlanner([], new()).Choose(blocked, true, new(65536));
        Assert.Equal(ArrivalEstimateStatus.Unreachable, rejected.Arrival.Status);
        Assert.Null(rejected.Waypoint);
    }
}
