using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed partial class UnitPolicyTests
{
    [Theory]
    [InlineData(40, false)]
    [InlineData(100, false)]
    [InlineData(0, true)]
    public void PeacefulForeignTroopsDoNotBecomeRetreatPressure(int relation, bool allied)
    {
        var state = World(policies: new(AutonomousRetreat: true));
        if (allied)
        {
            var snapshot = state.CreateSnapshot();
            state = WorldState.Create(snapshot.Clock, 42, snapshot.Forces.Append(new(3, 100)),
                snapshot.Settlements, snapshot.Officers, snapshot.Units, controls: snapshot.Simulation.Controls,
                corps: snapshot.Corps.Append(new(3, 3, null, new(0, 0)) { IsRoot = true }));
        }
        var world = state.BeginMutation();
        world.SetRelation(new(1, 2, relation));
        if (allied) world.SetSimulation(world.Simulation with { Alliance = new(3, 1, [1, 2], 30) });
        world.Replace(world.Units[1] with { Soldiers = 1000 });
        world.Replace(world.Units[2] with { Position = new(21, 10) });
        List<DomainEvent> events = [];
        MilitaryPolicies.ResolveRetreat(world, Rules(), events);
        Assert.False(world.Units[1].AutonomousReturn);
        Assert.DoesNotContain(events, e => e is UnitPolicyRespondedEvent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnActualFightCountsAsRetreatPressureEvenBeforeDiplomacyChanges(bool sourceAttacks)
    {
        var world = World(policies: new(AutonomousRetreat: true)).BeginMutation();
        world.SetRelation(new(1, 2, 100));
        world.Replace(world.Units[1] with { Soldiers = 1000, Order = sourceAttacks ? Attack(2) : Mission });
        world.Replace(world.Units[2] with { Position = new(21, 10), Order = sourceAttacks ? Mission : Attack(1) });
        MilitaryPolicies.ResolveRetreat(world, Rules(), []);
        Assert.True(world.Units[1].AutonomousReturn);
    }

    [Fact]
    public void APeacefulForeignGarrisonDoesNotCauseAnOutnumberedRetreat()
    {
        var world = World(policies: new(AutonomousRetreat: true)).BeginMutation();
        world.SetRelation(new(1, 2, 100));
        world.Replace(world.Units[1] with { Soldiers = 1000 });
        world.Replace(world.Units[2] with { Position = new(80, 10) });
        var standard = Rules();
        var rules = new MilitaryRulebook(new(20, 5, Enumerable.Repeat(0, 100).ToImmutableArray(),
            [new(0, 1, null)], standard.Formations.Values.ToImmutableArray(),
            [standard.Places[1], standard.Places[2] with { Position = new(21, 10), Attack = new(1, true) }])
            { PolicyRadius = 10 });
        MilitaryPolicies.ResolveRetreat(world, rules, []);
        Assert.False(world.Units[1].AutonomousReturn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndingTemporaryPursuitCannotBeUndoneByTheNextApproachPass(bool pursue)
    {
        var world = World(1, new(AttackApproachingEnemies: true, Pursue: pursue)).BeginMutation();
        world.Replace(world.Units[1] with { EngagementTargetId = 2 });
        world.Replace(world.Units[2] with { Order = new(MilitaryOrderKind.Return, null, 2),
            AutonomousReturn = true, PursuitResponsePending = true });
        List<DomainEvent> events = [];
        MilitaryPolicies.ResolvePursuit(world, Rules(), events);
        MilitaryPolicies.FinishPursuitResponses(world);
        for (int day = 0; day < 3; day++) MilitaryPolicies.ApproachDay(world, Rules(), events);
        Assert.Null(world.Units[1].EngagementTargetId);
        Assert.Equal(Mission, world.Units[1].Order);
        Assert.Single(events.OfType<UnitPolicyRespondedEvent>(), e => e.Unit.Id == 1 && e.Response == UnitPolicyResponse.ResumeOrders);
        Assert.DoesNotContain(events.OfType<UnitEngagementChangedEvent>(), e => e.UnitId == 1 && e.TargetId == 2);
        // Once this is an advancing enemy again, ordinary interception is available without a cooldown.
        world.Replace(world.Units[2] with { Order = Attack(1), AutonomousReturn = false });
        MilitaryPolicies.ApproachDay(world, Rules(), events);
        Assert.Equal(2, world.Units[1].EngagementTargetId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicDailyAdvanceResumesTheMissionInsteadOfReacquiringTheRetreatingEnemy(bool pursue)
    {
        var snapshot = World(1, new(AttackApproachingEnemies: true, Pursue: pursue)).CreateSnapshot();
        snapshot = snapshot with
        {
            Simulation = snapshot.Simulation with { Boundary = SimulationBoundary.Stable, ActiveControl = snapshot.Simulation.Controls.Length },
            Units = snapshot.Units.Select(u => u.Id == 1 ? u with { EngagementTargetId = 2 } : u with
                { Order = Attack(1), Morale = 20, Policies = new(AutonomousRetreat: true) }).ToArray()
        };
        var engine = new SimulationEngine(WorldState.Restore(snapshot), EconomyRulebook.Empty,
            PersonnelRulebook.Empty, TreasureRulebook.Empty, military: Rules());
        for (int day = 0; day < 3; day++)
        {
            var result = engine.AdvanceDay();
            Assert.Equal(Mission, result.Snapshot.Units.Single(u => u.Id == 1).EffectiveOrder);
            Assert.DoesNotContain(result.Events.OfType<UnitPolicyRespondedEvent>(), e => e.Unit.Id == 1 &&
                e.Response is UnitPolicyResponse.AttackOrdered or UnitPolicyResponse.AttackDespiteOrders);
        }
    }

    [Fact]
    public void PostActionCannotStartAPursuitThatInterceptionWouldRefuse()
    {
        var world = World(1, new(AttackApproachingEnemies: true, AttackAfterAction: true)).BeginMutation();
        world.Replace(world.Units[1] with { Order = null, AwaitingPostAction = true });
        world.Replace(world.Units[2] with { Position = new(21, 10), Order = new(MilitaryOrderKind.Return, null, 2),
            AutonomousReturn = true });
        List<DomainEvent> events = [];
        MilitaryPolicies.ResolvePostAction(world, world.Units[1], Rules(), events);
        Assert.Equal(UnitPolicyResponse.PostActionNoTarget, Assert.Single(events.OfType<UnitPolicyRespondedEvent>()).Response);
        Assert.Equal(MilitaryOrderKind.Return, world.Units[1].Order!.Kind);
    }

    [Fact]
    public void PostActionCanContinueCombatWithoutAReturnPlace()
    {
        var world = World(1, new(AttackAfterAction: true)).BeginMutation();
        world.Replace(world.Units[1] with { Order = null, AwaitingPostAction = true, ServicePlaceId = null });
        world.Replace(world.Units[2] with { Position = new(21, 10) });
        world.Replace(world.Settlements[1] with { CanHostPersons = false });
        List<DomainEvent> events = [];
        MilitaryPolicies.ResolvePostAction(world, world.Units[1], Rules(), events);
        Assert.Equal(Attack(2), world.Units[1].Order);
        Assert.False(world.Units[1].AwaitingPostAction);
        Assert.Equal(UnitPolicyResponse.PostActionAttackOrdered, Assert.Single(events.OfType<UnitPolicyRespondedEvent>()).Response);
    }

    [Fact]
    public void FailedPostActionReturnConsumesTheDecisionAndOnlyRepairsItsDestinationLater()
    {
        var state = World(3);
        _ = new SimulationEngine(state, EconomyRulebook.Empty, PersonnelRulebook.Empty,
            TreasureRulebook.Empty, military: Rules());
        var world = state.BeginMutation();
        world.Replace(world.Units[1] with { Order = null, AwaitingPostAction = true, ServicePlaceId = null });
        world.Replace(world.Units[2] with { Position = new(80, 10) });
        world.Replace(world.Settlements[1] with { CanHostPersons = false });
        List<DomainEvent> events = [];
        MilitaryPolicies.ResolvePostAction(world, world.Units[1], Rules(), events);
        var failed = Assert.Single(events.OfType<UnitPolicyRespondedEvent>());
        Assert.Equal(UnitPolicyResponse.PostActionNoTarget, failed.Response);
        Assert.Equal(UnitPolicyReturnOutcome.NoDestination, failed.ReturnOutcome);
        Assert.Equal(new(MilitaryOrderKind.Return, null, null), world.Units[1].Order);
        Assert.True(world.Units[1].AutonomousReturn);
        Assert.False(world.Units[1].AwaitingPostAction);
        Assert.False(world.Units[1].PursuitResponsePending);

        state.Commit(world);
        var restored = WorldState.Restore(state.CreateSnapshot()).BeginMutation();
        List<DomainEvent> later = [];
        for (int day = 0; day < 3; day++) MilitaryOrders.ResolveOrders(restored, Rules(), later);
        Assert.Empty(later);
        // A newly available enemy must not reopen a completed decision to return.
        restored.Replace(restored.Units[1] with { Policies = new(AttackAfterAction: true) });
        restored.Replace(restored.Units[2] with { Position = new(21, 10) });
        restored.Replace(restored.Settlements[1] with { CanHostPersons = true });
        MilitaryOrders.ResolveOrders(restored, Rules(), later);
        Assert.Equal(new(MilitaryOrderKind.Return, null, 1), restored.Units[1].Order);
        var started = Assert.Single(later.OfType<UnitPolicyRespondedEvent>());
        Assert.Equal(UnitPolicyReturnOutcome.Started, started.ReturnOutcome);
        Assert.Equal(UnitPolicyResponse.ReturnAfterAction, started.Response);
    }

    [Fact]
    public void FailedPursuitReturnPreservesTheDecisionAndReportsTheMissingDestination()
    {
        var world = World(1, new(), Attack(2)).BeginMutation();
        world.Replace(world.Units[1] with { ServicePlaceId = null });
        world.Replace(world.Units[2] with { Order = new(MilitaryOrderKind.Return, null, 2),
            AutonomousReturn = true, PursuitResponsePending = true });
        List<DomainEvent> events = [];
        MilitaryPolicies.ResolvePursuit(world, Rules(), events);
        var response = Assert.Single(events.OfType<UnitPolicyRespondedEvent>());
        Assert.Equal(UnitPolicyResponse.ReturnHome, response.Response);
        Assert.Equal(UnitPolicyReturnOutcome.NoDestination, response.ReturnOutcome);
        Assert.Equal(Attack(2), world.Units[1].Order);
        Assert.False(world.Units[1].AutonomousReturn);
        Assert.Empty(events.OfType<UnitOrderChangedEvent>());
    }
}
