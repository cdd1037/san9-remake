using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MovementOccupancyTests
{
    [Fact]
    public void SnapshotRouteAndLiveMovementIgnoreDormantWorkObstacles()
    {
        var clock = new GameClock(190, 1, 1);
        var world = WorldState.Create(clock, 1, [],
            [new(1, null, 0, 100, 100, 50, 0, 0, 100, 0, 100) { IsDormant = true, Kind = SettlementKind.Facility },
             new(2, null, 0, 100, 100, 50, 0, 0, 100, 0, 100) { Kind = SettlementKind.Facility }], []);
        var rules = new MilitaryRulebook(new(20, 20, new int[400].ToImmutableArray(), [new(0, 10, null)], [],
            [new(1, new(30, 30), new(5, 5)) { FacilityTypeId = 100 }, new(2, new(70, 70), new(5, 5)) { FacilityTypeId = 100 }])
            { Facilities = [new(100, 0, 100, new(5, 5), 0, false, false, false, 100000, null, null, null, []) { IsWork = true }] });
        var policy = new UnitPolicies();
        var preview = MilitaryRouteQuery.Occupancy(world.CreateSnapshot(), rules, 99, policy);
        var live = world.BeginMutation().MovementOccupancy(rules, 99, policy, null, null);
        Assert.Equal(live, preview);
        Assert.Equal(new WorldPosition(70, 70), Assert.Single(preview).Position);
    }
}
