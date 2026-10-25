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
                Condition = UnitCondition.Confused, ConditionDaysRemaining = 3,
                Order = new(MilitaryOrderKind.Move, new(8, 2), null) }],
            controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 10000)) { IsRoot = true }]), EconomyRulebook.Empty, PersonnelRulebook.Empty,
            TreasureRulebook.Empty, military: rules);
        var context = new CommandContext("player");
        Assert.False(engine.Execute(context, new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0,
            new(MilitaryOrderKind.Move, new(9, 2), null))).Submission.Accepted);
        engine.EndControl(context);
        var result = engine.AdvanceXun();
        Assert.All(result.Events.OfType<UnitMovedEvent>(), e => Assert.True(e.Clock.Day >= 5));
        Assert.Equal(4, Assert.Single(result.Events.OfType<BattleConditionChangedEvent>()).Clock.Day);
        var unit = Assert.Single(engine.Snapshot.Units);
        Assert.Equal(new WorldPosition(8, 2), unit.Position);
        Assert.Equal(UnitCondition.Normal, unit.Condition);
        Assert.Equal(0, unit.ConditionDaysRemaining);
        Assert.Null(unit.Order);
    }

    [Theory]
    [InlineData(UnitCondition.Disguised)]
    [InlineData(UnitCondition.NoFormation)]
    [InlineData(UnitCondition.Confused)]
    [InlineData(UnitCondition.Routed)]
    public void PublicOrderEligibilityRejectsDisabledStates(UnitCondition condition)
    {
        var rules = UnitStrategyTests.Rules();
        var state = UnitStrategyTests.World().BeginMutation(rules.Abilities);
        state.Replace(state.Units[5] with { Condition = condition, ConditionDaysRemaining = 3 });
        var before = state.Units[5];
        Assert.Equal(CommandRejection.InvalidTask, MilitaryMovement.Order(state,
            new(Guid.NewGuid(), 2, 0, 5, new(MilitaryOrderKind.Move, new(20, 2), null)), rules.Military, []));
        Assert.Equal(before, state.Units[5]);
    }

    [Theory]
    [InlineData(UnitCondition.NoFormation)]
    [InlineData(UnitCondition.Confused)]
    [InlineData(UnitCondition.HighSpirits)]
    public void OrdinaryStatusChangesAndRecoveryPreserveContact(UnitCondition condition)
    {
        var before = NavigationUnit();
        var changed = MilitaryUnitTransitions.Apply(before, MilitaryConditions.Apply(before, condition, 1));
        Assert.Equal(before.ContactTarget, changed.ContactTarget);
        Assert.Equal(before.Order, changed.Order);
        Assert.Equal(before.Waypoint, changed.Waypoint);
        Assert.Null(changed.Lookahead);
        var recovered = MilitaryUnitTransitions.Apply(changed, MilitaryConditions.Tick(changed));
        Assert.Equal(UnitCondition.Normal, recovered.Condition);
        Assert.Equal(before.ContactTarget, recovered.ContactTarget);
        Assert.Equal(before.EffectiveOrder, recovered.EffectiveOrder);
    }

    [Theory]
    [InlineData(UnitCondition.Disguised)]
    [InlineData(UnitCondition.Routed)]
    public void ForcedReturnPreservesIntentAndResumesItOnExpiration(UnitCondition condition)
    {
        var before = NavigationUnit() with { EngagementTargetId = 2, ActiveEngagement = true };
        var changed = MilitaryUnitTransitions.Apply(before, MilitaryConditions.Apply(before, condition, 1));
        Assert.Equal(before.Order, changed.Order);
        Assert.Equal(before.Waypoint, changed.Waypoint);
        Assert.Null(changed.ContactTarget);
        Assert.Null(changed.EngagementTargetId);
        Assert.False(changed.ActiveEngagement);
        Assert.Null(changed.Lookahead);
        Assert.Null(changed.ActiveWaypoint);
        Assert.Equal(MilitaryOrderKind.Return, changed.EffectiveOrder!.Kind);
        var recovered = MilitaryUnitTransitions.Apply(changed, MilitaryConditions.Tick(changed));
        Assert.Equal(before.Order, recovered.EffectiveOrder);
        Assert.Equal(before.Waypoint, recovered.ActiveWaypoint);
    }

    [Theory]
    [InlineData(UnitCondition.NoFormation, UnitCondition.Confused)]
    [InlineData(UnitCondition.Confused, UnitCondition.NoFormation)]
    [InlineData(UnitCondition.Disguised, UnitCondition.HighSpirits)]
    public void ReplacingStatusKeepsLongerDuration(UnitCondition previous, UnitCondition next)
    {
        var unit = NavigationUnit() with { Condition = previous, ConditionDaysRemaining = 9 };
        Assert.Equal(9, MilitaryConditions.Apply(unit, next, 2).ConditionDaysRemaining);
        Assert.Equal(12, MilitaryConditions.Apply(unit, next, 12).ConditionDaysRemaining);
        Assert.Same(unit, MilitaryConditions.Apply(unit, previous, 12));
    }

    [Fact]
    public void ConfusionConsumerKeepsPreviousAbnormalDuration()
    {
        var rules = UnitStrategyTests.Rules();
        var state = UnitStrategyTests.World().BeginMutation(rules.Abilities);
        state.Replace(state.Units[5] with { Condition = UnitCondition.NoFormation, ConditionDaysRemaining = 9 });
        List<DomainEvent> events = [];
        MilitaryConditions.Confuse(state, new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 5 }, 2, events);
        Assert.Equal(UnitCondition.Confused, state.Units[5].Condition);
        Assert.Equal(9, state.Units[5].ConditionDaysRemaining);
        Assert.Equal(9, Assert.Single(events.OfType<BattleConditionChangedEvent>()).DaysRemaining);
    }

    private static UnitState NavigationUnit() => new(0, 1, new(10, 2), 1000)
    {
        FormationId = 1, ServicePlaceId = 1, Morale = 80,
        Order = new(MilitaryOrderKind.Move, new(30, 2), null), Waypoint = new(20, 2),
        ContactTarget = new(1, false), Lookahead = new(new(15, 2))
    };
}
