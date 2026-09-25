using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MilitaryConditionTests
{
    [Fact]
    public void ConfusionPausesMovementAndExpirationResumesTheRetainedOrder()
    {
        var rules = new MilitaryRulebook(new(3, 1, [0, 0, 0], [new(0, 1, null)],
            [new(1, 1, 0, 10000, 1, new(1, 1), TravelMode.Land)], [new(1, new(2, 2), new(1, 1))]));
        var engine = new SimulationEngine(WorldState.Create(new(190, 1, 1), 42, [new(1, 100)],
            [new(1, 1, 100, 100, 100, 100, 0, 0, 100, 0, 100)],
            [new(11, 1, null, new(70, 70, 70, 70), false) { UnitId = 0 }],
            units: [new(0, 1, new(4, 2), 100) { FormationId = 1, Morale = 100,
                Condition = UnitCondition.Confused, ConditionDaysRemaining = 3 }],
            controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 10000)) { IsRoot = true }]), EconomyRulebook.Empty, PersonnelRulebook.Empty,
            TreasureRulebook.Empty, military: rules);
        var context = new CommandContext("player");
        Assert.True(engine.Execute(context, new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0,
            new(MilitaryOrderKind.Move, new(8, 2), null))).Submission.Accepted);
        engine.EndControl(context);
        var result = engine.AdvanceXun();
        Assert.All(result.Events.OfType<UnitMovedEvent>(), e => Assert.True(e.Clock.Day >= 3));
        Assert.Equal(3, Assert.Single(result.Events.OfType<BattleConditionChangedEvent>()).Clock.Day);
        var unit = Assert.Single(engine.Snapshot.Units);
        Assert.Equal(new WorldPosition(8, 2), unit.Position);
        Assert.Equal(UnitCondition.Normal, unit.Condition);
        Assert.Equal(0, unit.ConditionDaysRemaining);
        Assert.Null(unit.Order);
    }
}
