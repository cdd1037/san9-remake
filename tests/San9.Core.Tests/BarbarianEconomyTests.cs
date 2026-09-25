using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class BarbarianEconomyTests
{
    private static WorldState World(uint soldiers = 100000, uint food = 300000, uint field = 0) => WorldState.Create(new(190, 1, 1), 190,
        [new(9000, 100) { IsBarbarian = true, LeaderId = 1 }, new(45, 100)],
        [new(1, 9000, soldiers, 100, 100, 50, 500, 300, 500, 200, 500) { CorpsId = 1, Population = 200000 },
         new(2, 9000, 0, 100, 100, 50, 500, 300, 500, 200, 500) { CorpsId = 1, Population = 200000 },
         new(3, 9000, 0, 100, 100, 50, 500, 300, 500, 200, 500) { CorpsId = 2, Population = 200000 },
         new(4, 45, 0, 100, 100, 50, 500, 300, 500, 200, 500) { Population = 200000 }],
        [new(1, 9000, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(2, 9000, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Governor }],
        corps: [new(1, 9000, 1, new(90000, food)) { IsRoot = true }, new(2, 9000, 2, new(1, 1)), new San9.Core.Domain.CorpsState(45, 45, null, new(1000, 1000)) { IsRoot = true }],
        units: field == 0 ? [] : [new(10, 9000, new(0, 0), field), new(11, 45, new(0, 0), 1000000)]);

    [Theory]
    [InlineData(1, 30, false, false)]
    [InlineData(4, 1, true, false)]
    [InlineData(7, 1, true, true)]
    public void SupplyUsesCurrentAffiliationOncePerAccountAndPreservesCalendarGates(int month, int day, bool quarterly, bool harvest)
    {
        var mutation = World().BeginMutation();
        mutation.SetClock(new(190, month, day));
        List<DomainEvent> events = [];
        EconomicCycle.Apply(ResolutionStage.QuarterUpdate, mutation, EconomyRulebook.Empty, events);
        Assert.Equal(quarterly ? 30000u : 90000u, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 9000).Treasury.Money);
        Assert.Equal(quarterly ? 30000u : 1u, mutation.Corps[2].Treasury!.Money);
        Assert.Equal(harvest ? 100000u : 300000u, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 9000).Treasury.Food);
        Assert.Equal(harvest ? 100000u : 1u, mutation.Corps[2].Treasury!.Food);
        var funds = events.OfType<ResourceChangedEvent>().Where(e => e.Reason == "barbarian-quarterly-funds").ToArray();
        Assert.Equal(quarterly ? 2 : 0, funds.Length);
        if (quarterly)
        {
            Assert.Equal(-60000, funds.Single(e => e.Account.Id == 1).Actual);
            Assert.Equal(29999, funds.Single(e => e.Account.Id == 2).Actual);
            Assert.Equal(4, Assert.Single(events.OfType<ResourceChangedEvent>(), e => e.Reason == "quarterly-revenue").SourceId);
        }
        Assert.Empty(mutation.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData(1000u, 1000u, 100000u)]
    [InlineData(400000u, 200000u, 600000u)]
    [InlineData(500000u, 900000u, 1000000u)]
    public void FoodTargetIncludesOwnFieldTroopsAndCapsTheAssignment(uint garrison, uint field, uint expected)
    {
        var mutation = World(garrison, field: field).BeginMutation();
        mutation.SetClock(new(190, 7, 1));
        EconomicCycle.Apply(ResolutionStage.QuarterUpdate, mutation, EconomyRulebook.Empty, []);
        Assert.Equal(expected, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 9000).Treasury.Food);
        Assert.Equal(expected, mutation.Corps[2].Treasury!.Food);
    }

    [Fact]
    public void FixedGarrisonRemainsSuppliedForTenYearsAndRestoresAcrossHarvest()
    {
        var engine = new SimulationEngine(World());
        Assert.DoesNotContain(engine.InitialEvents.OfType<ResourceChangedEvent>(), e => e.Reason.StartsWith("barbarian-"));
        for (int xun = 0; xun < 360; xun++)
        {
            var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()));
            var result = engine.AdvanceXun();
            var replayed = restored.AdvanceXun();
            Assert.Equal(result.Snapshot.StateHash, replayed.Snapshot.StateHash);
            Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(replayed.Events));
            Assert.DoesNotContain(result.Events.OfType<ResourceChangedEvent>(), e => e.Resource == ResourceKind.Food && e.Shortfall > 0);
            Assert.Equal(100000u, result.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
            if (result.Snapshot.Clock.Month == 7 && result.Snapshot.Clock.Day == 1)
                Assert.Equal(100000u, result.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 9000).Treasury.Food);
        }
    }
}
