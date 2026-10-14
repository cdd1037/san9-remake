using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ComputerDamageTests
{
    [Theory]
    [InlineData(101u, 1000u, 0, 101u)]
    [InlineData(101u, 1000u, 50, 151u)]
    [InlineData(101u, 1000u, 100, 202u)]
    [InlineData(101u, 150u, 100, 150u)]
    [InlineData(0u, 150u, 100, 0u)]
    [InlineData(uint.MaxValue, uint.MaxValue, int.MaxValue, uint.MaxValue)]
    public void ScalingFloorsBeforeAllocationAndCapsWithoutOverflow(uint damage, uint soldiers, int bonus, uint expected)
    {
        uint scaled = CombatMath.AdjustComputerDamage(damage, soldiers, bonus);
        Assert.Equal(expected, scaled);
        var loss = CombatMath.Loss(soldiers, scaled, 80, 49999, 50000);
        Assert.Equal(soldiers, loss.RemainingSoldiers + loss.AddedWounded + loss.Dead);
        Assert.True(loss.AddedWounded <= 1);
    }

    [Fact]
    public void InvalidSettingsAreRejectedAndStableRestorationPreservesTheValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatMath.AdjustComputerDamage(1, 1, -1));
        Assert.Throws<ArgumentException>(() => WorldState.Create(new(190, 1, 1), 42, [], [], [], computerDamageBonusPercent: -1));
        var world = WorldState.Create(new(190, 1, 1), 42, [], [], [], computerDamageBonusPercent: 100);
        var engine = new SimulationEngine(world);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()));
        Assert.Equal(100, restored.Snapshot.Simulation.ComputerDamageBonusPercent);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
    }
}
