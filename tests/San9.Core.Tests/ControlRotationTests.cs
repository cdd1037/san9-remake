using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ControlRotationTests
{
    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(1, 1, 2)]
    [InlineData(1, 2, 3)]
    [InlineData(1, 3, 2)]
    [InlineData(3, 1, null)]
    public void RetiringForcePreservesNextUnfinishedController(int active, int retired, int? expected)
    {
        var controls = Enumerable.Range(1, 3).Select(i => new ControlSeat($"p:{i}", i, ControllerKind.Player)).ToImmutableArray();
        var world = WorldState.Create(new(190, 1, 1), 0,
            Enumerable.Range(1, 3).Select(i => new ForceState(i, 0, 0, 0)), [], [], controls: controls).BeginMutation();
        world.SetSimulation(world.Simulation with { ActiveControl = active });
        ForceLifecycle.Retire(world, retired, []);
        Assert.Equal(expected, world.Simulation.ActiveControl == world.Simulation.Controls.Length ? null :
            (int?)world.Simulation.Controls[world.Simulation.ActiveControl].ForceId);
    }

    [Fact]
    public void FreeOfficerCannotLoseAllGeographicReferences()
    {
        var person = new OfficerState(1, null, null, new(50, 50, 50, 50), false) { Status = OfficerStatus.Free };
        Assert.False(person.IsValid());
        Assert.True((person with { RegionId = 42 }).IsValid());
    }
}
