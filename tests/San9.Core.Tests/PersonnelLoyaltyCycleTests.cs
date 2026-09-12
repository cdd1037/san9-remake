using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PersonnelLoyaltyCycleTests
{
    [Fact]
    public void QuarterMovesForeignInterestTowardFiftyWithoutRandomOrChangingOwnLoyalty()
    {
        var world = WorldState.Create(new(190, 1, 1), 7,
            [new(1, 100000, 100000, 0), new(2, 100000, 100000, 0)],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false)
                { ForceLoyalties = new Dictionary<int, int> { [1] = 75, [2] = 49 }.ToImmutableDictionary() },
             new(2, null, 1, new(50, 50, 50, 50), false)
                { Status = OfficerStatus.Free, ForceLoyalties = new Dictionary<int, int> { [1] = 50, [2] = 51 }.ToImmutableDictionary() }]);
        var engine = new SimulationEngine(world);
        Assert.Equal(49, engine.Snapshot.Officers.Single(p => p.Id == 1).LoyaltyTo(2));
        for (int xun = 0; xun < 8; xun++) engine.AdvanceXun();
        Assert.Equal(49, engine.Snapshot.Officers.Single(p => p.Id == 1).LoyaltyTo(2));
        var random = engine.Snapshot.Simulation.RandomStreams;
        var result = engine.AdvanceXun();
        Assert.Equal(50, result.Snapshot.Officers.Single(p => p.Id == 1).LoyaltyTo(2));
        Assert.Equal(75, result.Snapshot.Officers.Single(p => p.Id == 1).LoyaltyTo(1));
        Assert.Equal(50, result.Snapshot.Officers.Single(p => p.Id == 2).LoyaltyTo(2));
        Assert.Equal(random, result.Snapshot.Simulation.RandomStreams);
        Assert.Equal(2, result.Events.OfType<OfficerLoyaltyChangedEvent>().Count(e => e.Reason == "quarterly-affiliation-interest"));
    }
}
