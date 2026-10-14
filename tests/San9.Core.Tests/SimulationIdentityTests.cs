using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SimulationIdentityTests
{
    [Fact]
    public void CommandCannotOccupyTheDerivedTaskNamespace()
    {
        var engine = new SimulationEngine(SimulationEngineTests.CreateWorld());
        var before = engine.Snapshot;
        var command = new StartTaskCommand(SimulationIdentity.Task(0), 1, 0, "fixture-investment", 2, 1);
        Assert.Equal(CommandRejection.InvalidCommandId, engine.Execute(new("player"), command).Submission.Rejection);
        Assert.Same(before, engine.Snapshot);
    }

    [Fact]
    public void TaskAllocationRollsBackWithItsMutationAndContinuesAfterRestore()
    {
        var world = SimulationEngineTests.CreateWorld();
        _ = new SimulationEngine(world);
        var abandoned = world.BeginMutation();
        var first = abandoned.AllocateTaskId();
        var committed = world.BeginMutation();
        Assert.Equal(first, committed.AllocateTaskId());
        world.Commit(committed);
        var restored = WorldState.Restore(world.CreateSnapshot());
        var next = world.BeginMutation().AllocateTaskId();
        Assert.NotEqual(first, next);
        Assert.Equal(next, restored.BeginMutation().AllocateTaskId());
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public void RestoreRejectsTaskIdentityOutsideAllocatedRange(long identity, long watermark)
    {
        var world = SimulationEngineTests.CreateWorld();
        _ = new SimulationEngine(world);
        var mutation = world.BeginMutation();
        var id = mutation.AllocateTaskId();
        mutation.SetSimulation(mutation.Simulation with
        {
            NextSequence = 1,
            Tasks = [new(id, "fixture", "player", 1, 2, 1, world.Clock, world.Clock.Advance(), 0, 0)]
        });
        world.Commit(mutation);
        var snapshot = world.CreateSnapshot();
        var invalid = snapshot with { Simulation = snapshot.Simulation with
        {
            NextTaskIdentity = watermark,
            Tasks = [snapshot.Simulation.Tasks[0] with { Id = SimulationIdentity.Task(identity) }]
        } };
        Assert.Contains("allocation watermark", Assert.Throws<ArgumentException>(() => WorldState.Restore(invalid)).Message);
    }
}
