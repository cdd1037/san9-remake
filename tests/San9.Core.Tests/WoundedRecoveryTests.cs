using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class WoundedRecoveryTests
{
    [Fact]
    public void DailyRationsIncludeWoundedAndRoundEachObjectBeforeAggregating()
    {
        var world = WorldState.Create(new(190, 1, 1), 42, [new(1, 100)],
            [new(1, 1, 500, 100, 100, 60, 0, 0, 100, 0, 100) { Wounded = 1 }], [],
            units: [new(1, 1, new(1, 1), 667) { Wounded = 1 }], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 10000)) { IsRoot = true }]);
        var mutation = world.BeginMutation();
        List<DomainEvent> events = [];
        EconomicCycle.Apply(ResolutionStage.BeforeDailyCommands, mutation, EconomyRulebook.Empty, events);
        Assert.Equal(9995u, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Food); // ceil(501/500) + floor(3*1001/1000).
        Assert.Equal(-5, Assert.Single(events.OfType<ResourceChangedEvent>()).Actual);
    }

    [Theory]
    [InlineData(5000, 10000, 15, 0, true, 2000)]
    [InlineData(5000, 10000, 15, 0, false, 4000)]
    [InlineData(5000, 10000, 15, 99, false, 4198)]
    [InlineData(5000, 0, 15, 0, false, 0)]
    [InlineData(5000, 300, 15, 0, false, 300)]
    [InlineData(500, 10000, 15, 0, false, 500)]
    [InlineData(50000, 10000, 0, 0, true, 1000)]
    public void RecoveryRespectsCombatCapacityAndExactIntegerFormula(uint wounded, uint capacity, int coefficient,
        int roll, bool engaged, uint expected) => Assert.Equal(expected, WoundedRecovery.Amount(wounded, capacity, coefficient, roll, engaged));

    [Fact]
    public void XunConservesTroopsAndRestoreDoesNotRepeatRecovery()
    {
        var world = WorldState.Create(new(190, 1, 1), 42, [new(1, 100)],
            [new(1, 1, 1000, 100, 100, 60, 0, 0, 100, 0, 100) { Wounded = 5000, SoldierCapacity = 1200 }], [], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 10000)) { IsRoot = true }]);
        var military = new MilitaryRulebook(new(1, 1, [0], [new(0, 1, null)], [],
            [new(1, new(2, 2), new(1, 1)) { WoundedRecovery = 15 }]));
        SimulationEngine Engine(WorldState state) => new(state, EconomyRulebook.Empty, PersonnelRulebook.Empty,
            TreasureRulebook.Empty, military: military);
        var engine = Engine(world);
        var city = Assert.Single(engine.Snapshot.Settlements);
        Assert.Equal(1200u, city.Soldiers);
        Assert.Equal(4800u, city.Wounded);
        var changes = engine.InitialEvents.OfType<ResourceChangedEvent>().Where(e => e.Reason == "wounded-recovery").ToArray();
        Assert.Equal(2, changes.Length);
        Assert.Equal(0, changes.Sum(e => e.Actual));
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        Assert.Equal(CanonicalJson.Text(engine.AdvanceXun().Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
    }
}
