using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class CorpsControlScopeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(10)]
    public void RootReferencesHaveTheSameAuthorityForPlacesAndUnits(int? rootReference)
    {
        // Scope queries do not need military eligibility; test their result against the same command authorization.
        var world = new SimulationEngine(WorldState.Create(new(190, 1, 1), 190)).Snapshot with
        {
            Corps = [new(10, 1, null, new(1000, 1000)) { IsRoot = true, ControlMode = CorpsControlMode.Automatic },
                new(11, 1, null, new(1000, 1000)) { ControlMode = CorpsControlMode.Automatic }],
            Settlements = [new(1, 1, 100, 100, 100, 50, 500, 100, 500, 100, 500) { CorpsId = rootReference }],
            Units = [new(1, 1, new(0, 0), 100) { CorpsId = rootReference }]
        };
        var seat = new ControlSeat("player", 1, ControllerKind.Player);
        var command = new SetUnitPoliciesCommand(Guid.NewGuid(), 1, 0, 1, new());
        var root = new CommandContext(CorpsControl.ControllerId(10));
        foreach (var context in new[] { root, new CommandContext("player"), new CommandContext(CorpsControl.ControllerId(11)), new CommandContext("unknown") })
        {
            bool expected = context == root;
            Assert.Equal(expected, CorpsControl.CanControlSettlement(world, seat, context, 1));
            Assert.Equal(expected, CorpsControl.CanControlUnit(world, seat, context, 1));
            Assert.Equal(expected ? CommandRejection.None : CommandRejection.NotActiveController,
                CorpsControl.Authorize(world, seat, context, command));
        }
        var returned = world with { Corps = world.Corps.Select(c => c.IsRoot ? c with { ControlMode = CorpsControlMode.Direct } : c).ToArray() };
        Assert.True(CorpsControl.CanControlUnit(returned, seat, new("player"), 1));
        Assert.False(CorpsControl.CanControlUnit(returned, seat, root, 1));
    }
}
