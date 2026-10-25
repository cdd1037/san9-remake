using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed partial class UnitPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryReturningUnitCanRespondToEnemyApproach(bool returning)
    {
        var rules = Rules();
        var engine = new SimulationEngine(World(1, new(AttackApproachingEnemies: true)),
            EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        var order = returning ? new MilitaryOrder(MilitaryOrderKind.Return, null, 1) :
            new(MilitaryOrderKind.Move, new(90, 10), null);
        var command = new OrderUnitCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 1, order);
        Assert.Equal(CommandRejection.None, engine.GetPreparationEligibility(new("player"), command));
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        var mutation = WorldState.Restore(engine.Snapshot).BeginMutation();
        Assert.False(MilitaryEngagementQuery.Capture(engine.Snapshot, rules).Units.Contains(1));
        Assert.Null(mutation.Units[1].EngagementTargetId);
        MilitaryPolicies.ApproachDay(mutation, rules, []);
        Assert.Equal(order, mutation.Units[1].Order);
        Assert.Equal(2, mutation.Units[1].EngagementTargetId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void PursuitResponseRequiresAutonomousRetreatRatherThanAnyOrdinaryReturn(bool autonomous, bool expectedReturn)
    {
        var rules = Rules();
        var initial = World(1, new(), new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 }).CreateSnapshot();
        initial = initial with
        {
            Units = initial.Units.Select(u => u.Id == 2 ? u with
                { Morale = autonomous ? 20 : 80, Policies = new(AutonomousRetreat: autonomous) } : u).ToArray(),
            Simulation = initial.Simulation with
            {
                Controls = [new("p2", 2, ControllerKind.Player), new("p1", 1, ControllerKind.Player)],
                ActiveControl = 0, Boundary = SimulationBoundary.Stable
            }
        };
        var engine = new SimulationEngine(WorldState.Restore(initial), EconomyRulebook.Empty,
            PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        var targetOrder = autonomous ? new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } :
            new(MilitaryOrderKind.Return, null, 2);
        var command = new OrderUnitCommand(Guid.NewGuid(), 2, engine.Snapshot.Simulation.NextSequence, 2, targetOrder);
        Assert.Equal(CommandRejection.None, engine.GetPreparationEligibility(new("p2"), command));
        Assert.True(engine.Execute(new("p2"), command).Submission.Accepted);
        var mutation = WorldState.Restore(engine.Snapshot).BeginMutation();
        List<DomainEvent> events = [];
        MilitaryPolicies.AdvanceDay(mutation, rules, events);
        Assert.Equal(MilitaryOrderKind.Return, mutation.Units[2].Order!.Kind);
        Assert.Equal(autonomous, mutation.Units[2].AutonomousReturn);
        Assert.Equal(autonomous, events.OfType<UnitOrderChangedEvent>().Any(e => e.UnitId == 2 && e.Reason == "autonomous-retreat"));
        Assert.Equal(expectedReturn, mutation.Units[1].Order!.Kind == MilitaryOrderKind.Return);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AnAlreadyEngagedUnitDoesNotStartAnotherApproachResponse(bool engaged, bool expectedIntercept)
    {
        var rules = Rules();
        var initial = World(1, new(AttackApproachingEnemies: true)).CreateSnapshot();
        initial = initial with { Simulation = initial.Simulation with { Boundary = SimulationBoundary.Stable },
            Units = initial.Units.Select(u => u.Id == 2 ? u with
        {
            Position = new(engaged ? 21 : 25, 10),
            Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 }
        } : u).ToArray() };
        var engine = new SimulationEngine(WorldState.Restore(initial), EconomyRulebook.Empty,
            PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        var command = new OrderUnitCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 1,
            new(MilitaryOrderKind.Move, new(90, 10), null));
        Assert.Equal(CommandRejection.None, engine.GetPreparationEligibility(new("player"), command));
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(engaged, MilitaryEngagementQuery.Capture(engine.Snapshot, rules).Units.Contains(1));
        var mutation = WorldState.Restore(engine.Snapshot).BeginMutation();
        List<DomainEvent> events = [];
        MilitaryCombat.AdvanceDay(mutation, new() { Military = rules }, events);
        Assert.Equal(engaged, mutation.Units[1].PassiveEngagement);
        Assert.Equal(engaged, events.OfType<BattleDamageEvent>().Any(e => e.TargetUnitId == 1));
        Assert.Null(mutation.Units[1].EngagementTargetId);
        MilitaryPolicies.ApproachDay(mutation, rules, events);
        Assert.Equal(expectedIntercept, mutation.Units[1].EngagementTargetId == 2);
    }

    [Fact]
    public void LaterApproachCandidatesObserveTheEngagementCreatedEarlierInTheSameScan()
    {
        var mutation = World(policies: new(AttackApproachingEnemies: true)).BeginMutation();
        mutation.Replace(mutation.Officers[22] with { Personality = 1 });
        mutation.Replace(mutation.Units[2] with
        {
            Position = new(21, 10), Order = new(MilitaryOrderKind.Move, new(5, 10), null),
            Policies = new(AttackApproachingEnemies: true)
        });
        var rules = Rules();
        Assert.False(MilitaryEngagements.Engaged(mutation, mutation.Units[2], rules));
        List<DomainEvent> events = [];
        MilitaryPolicies.ApproachDay(mutation, rules, events);
        Assert.Equal(2, mutation.Units[1].EngagementTargetId);
        Assert.True(MilitaryEngagements.Engaged(mutation, mutation.Units[2], rules));
        Assert.Null(mutation.Units[2].EngagementTargetId);
        Assert.DoesNotContain(events.OfType<UnitPolicyRespondedEvent>(), e => e.Unit.Id == 2);
    }

    [Fact]
    public void ANewPublicOrderClearsAutonomousReturnEvenWhenTheDestinationIsTheSame()
    {
        var rules = Rules();
        var initial = World(order: new(MilitaryOrderKind.Return, null, 1)).CreateSnapshot();
        initial = initial with
        {
            Simulation = initial.Simulation with { Boundary = SimulationBoundary.Stable },
            Units = initial.Units.Select(u => u.Id == 1 ? u with { AutonomousReturn = true, PursuitResponsePending = true } : u).ToArray()
        };
        var engine = new SimulationEngine(WorldState.Restore(initial), EconomyRulebook.Empty,
            PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        var command = new OrderUnitCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 1,
            new(MilitaryOrderKind.Return, null, 1));
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        Assert.False(engine.Snapshot.Units.Single(u => u.Id == 1).AutonomousReturn);
        Assert.False(engine.Snapshot.Units.Single(u => u.Id == 1).PursuitResponsePending);
    }
}
