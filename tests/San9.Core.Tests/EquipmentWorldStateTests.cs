using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class EquipmentWorldStateTests
{
    private static WorldState World() => WorldState.Create(new(190, 1, 1), 42,
        [new ForceState(1, 0)], [], [], corps: [new CorpsState(1, 1, null, new(0, 0)) { IsRoot = true }]);

    [Fact]
    public void EquipmentSurvivesCommitSnapshotRestoreAndFork()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.SetEquipment(new(9, "sword", null, 1, false, 3, 27));
        world.Commit(mutation);
        var snapshot = world.CreateSnapshot() with { Simulation = world.CreateSnapshot().Simulation with { Boundary = SimulationBoundary.Stable } };
        Assert.Equal(27, Assert.Single(snapshot.Equipment).Experience);
        var restored = WorldState.Restore(snapshot);
        Assert.Equal(snapshot.Equipment, restored.CreateSnapshot().Equipment);
        Assert.Equal(snapshot.Equipment, restored.Fork().CreateSnapshot().Equipment);
        Assert.Single(restored.BeginMutation().CapturePresentation().Equipment);
    }

    [Fact]
    public void RestoreRejectsDuplicateOrUnknownOwners()
    {
        var source = World().CreateSnapshot();
        var snapshot = source with { Simulation = source.Simulation with { Boundary = SimulationBoundary.Stable } };
        var item = new EquipmentInstance(9, "sword", null, 1, false);
        Assert.Throws<ArgumentException>(() => WorldState.Restore(snapshot with { Equipment = [item, item] }));
        Assert.Throws<ArgumentException>(() => WorldState.Restore(snapshot with { Equipment = [item with { InventoryForceId = 7 }] }));
    }
}
