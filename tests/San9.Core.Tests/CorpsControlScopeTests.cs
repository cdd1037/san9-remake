using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class CorpsControlScopeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(10)]
    [InlineData(11)]
    public void AssistanceSharesAuthorityAndProtectsPlayerOrdersWithoutChangingCorps(int? reference)
    {
        var world = new SimulationEngine(WorldState.Create(new(190, 1, 1), 190)).Snapshot with
        {
            Corps = [new(10, 1, null, new(1000, 1000)) { IsRoot = true }, new(11, 1, null, new(1000, 1000))],
            Settlements = [new(1, 1, 100, 100, 100, 50, 500, 100, 500, 100, 500) { CorpsId = reference }],
            Units = [new(1, 1, new(0, 0), 100) { CorpsId = reference }]
        };
        var seat = new ControlSeat("player", 1, ControllerKind.Player);
        var player = new CommandContext("player");
        var automatic = new CommandContext(CorpsControl.ControllerId(reference ?? 10));
        var other = new CommandContext(CorpsControl.ControllerId(reference == 11 ? 10 : 11));
        var order = new OrderUnitCommand(Guid.NewGuid(), 1, 0, 1, new(MilitaryOrderKind.Return, null, 1));
        var settings = new CompanionSettings { AllowDelegatedCorpsOrders = true, PlayerCorpsAutomation = PlayerCorpsAutomationMode.Full };
        Assert.Equal(reference != 11, CorpsControl.CanControlUnit(world, seat, player, 1));
        Assert.Equal(reference == 11, CorpsControl.CanControlUnit(world, seat, automatic, 1));
        foreach (var context in new[] { player, automatic })
        {
            Assert.True(CorpsControl.CanControlSettlement(world, seat, context, 1, settings));
            Assert.Equal(CommandRejection.None, CorpsControl.Authorize(world, seat, context, order, settings));
        }
        Assert.Equal(CommandRejection.NotActiveController, CorpsControl.Authorize(world, seat, other, order, settings));
        var commanded = world with { Units = [world.Units[0] with { CommandedThisXun = true }] };
        foreach (var command in new GameCommand[] { order,
            new SetUnitPoliciesCommand(Guid.NewGuid(), 1, 0, 1, new()),
            new OrderUnitsCommand(Guid.NewGuid(), 1, 0, [new(1)], order.Order) })
        {
            Assert.Equal(CommandRejection.None, CorpsControl.Authorize(commanded, seat, player, command, settings));
            Assert.Equal(CommandRejection.NotActiveController, CorpsControl.Authorize(commanded, seat, automatic, command, settings));
            Assert.Equal(CommandRejection.None, CorpsControl.Authorize(commanded, seat with { Kind = ControllerKind.Automatic }, automatic, command, settings));
        }
        Assert.Equal(CommandRejection.NotActiveController, CorpsControl.Authorize(world, seat, automatic,
            new SetCorpsDelegationCommand(Guid.NewGuid(), 1, 0, reference ?? 10, new GeneralDelegation()), settings));
    }
}
