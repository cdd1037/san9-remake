using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed partial class UnitPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void OrdinaryReturnOnlyReconsidersRetreatWhileItHasACurrentTarget(bool engaged, bool expected)
    {
        var world = World(policies: new(AutonomousRetreat: true), order: new(MilitaryOrderKind.Return, null, 1)).BeginMutation();
        world.Replace(world.Units[1] with { Morale = 20, EngagementTargetId = engaged ? 2 : null });
        List<DomainEvent> events = [];
        MilitaryPolicies.ResolveRetreat(world, Rules(), events);
        Assert.Equal(expected, world.Units[1].AutonomousReturn);
        Assert.Equal(expected, events.OfType<UnitPolicyRespondedEvent>().Any(e => e.Unit.Id == 1));
        if (expected)
            Assert.Equal(UnitPolicyReturnOutcome.Started, Assert.Single(events.OfType<UnitPolicyRespondedEvent>()).ReturnOutcome);
    }

    [Theory]
    [InlineData(21, true)]
    [InlineData(28, false)]
    public void RetreatThreatsRequireHostilityAndActualAttackReachButNotDeparture(int enemyX, bool retreats)
    {
        var world = World(policies: new(AutonomousRetreat: true)).BeginMutation();
        world.Replace(world.Units[1] with { Soldiers = 1000 });
        world.Replace(world.Units[2] with { Position = new(enemyX, 10), HasEnteredDailyCycle = false, DepartureDaysRemaining = 5,
            Order = new(MilitaryOrderKind.Move, new(90, 10), null) });
        Assert.True(MilitaryDiplomacy.AutomaticEnemy(world, 1, 2));
        MilitaryPolicies.ResolveRetreat(world, Rules(), []);
        Assert.Equal(retreats, world.Units[1].AutonomousReturn);
    }

    [Fact]
    public void AnOccupiedForeignGarrisonCanCauseAnOutnumberedRetreat()
    {
        var world = World(policies: new(AutonomousRetreat: true)).BeginMutation();
        world.Replace(world.Units[1] with { Soldiers = 1000 });
        world.Replace(world.Units[2] with { Position = new(80, 10) });
        var standard = Rules();
        var rules = new MilitaryRulebook(new(20, 5, Enumerable.Repeat(0, 100).ToImmutableArray(),
            [new(0, 1, null)], standard.Formations.Values.ToImmutableArray(),
            [standard.Places[1], standard.Places[2] with { Position = new(21, 10), Attack = new(1, true) }])
            { PolicyRadius = 10 });
        MilitaryPolicies.ResolveRetreat(world, rules, []);
        Assert.True(world.Units[1].AutonomousReturn);
    }

    [Fact]
    public void WaitingDoesNotDelayAnOtherwiseEligibleAutonomousRetreat()
    {
        var world = World(policies: new(AutonomousRetreat: true)).BeginMutation();
        world.Replace(world.Units[1] with { Morale = 20, DepartureDaysRemaining = 5, HasEnteredDailyCycle = false });
        MilitaryPolicies.ResolveRetreat(world, Rules(), []);
        Assert.True(world.Units[1].AutonomousReturn);
        Assert.Equal(0, world.Units[1].DepartureDaysRemaining);
    }

    [Theory]
    [InlineData(true, 1, UnitPolicyResponse.PostActionAttackOrdered, MilitaryOrderKind.AttackUnit)]
    [InlineData(true, 3, UnitPolicyResponse.PostActionAvoidDespiteOrders, MilitaryOrderKind.Return)]
    [InlineData(false, 0, UnitPolicyResponse.PostActionAttackDespiteOrders, MilitaryOrderKind.AttackUnit)]
    [InlineData(false, 3, UnitPolicyResponse.PostActionReturnOrdered, MilitaryOrderKind.Return)]
    public void PostActionReportsThePolicyDecisionAfterApplyingItsOutcome(bool permitted, int personality,
        UnitPolicyResponse response, MilitaryOrderKind order)
    {
        var world = World(personality, new(AttackAfterAction: permitted)).BeginMutation();
        world.Replace(world.Units[2] with { Position = new(21, 10) });
        world.Replace(world.Units[1] with { Order = null, AwaitingPostAction = true });
        List<DomainEvent> events = [];
        MilitaryPolicies.ResolvePostAction(world, world.Units[1], Rules(), events);
        Assert.Equal(UnitCondition.Normal, world.Units[1].Condition);
        Assert.Equal(0, world.Units[1].ConditionDaysRemaining);
        Assert.Equal(order, world.Units[1].Order!.Kind);
        Assert.Equal(response, Assert.Single(events.OfType<UnitPolicyRespondedEvent>()).Response);
        Assert.Equal(order == MilitaryOrderKind.Return ? UnitPolicyReturnOutcome.Started : UnitPolicyReturnOutcome.NotRequested,
            Assert.Single(events.OfType<UnitPolicyRespondedEvent>()).ReturnOutcome);
    }

    [Fact]
    public void PostActionCannotChooseATargetThatHasNotEnteredDailyActivity()
    {
        var world = World(policies: new(AttackAfterAction: true)).BeginMutation();
        world.Replace(world.Units[1] with { Order = null, AwaitingPostAction = true });
        world.Replace(world.Units[2] with { HasEnteredDailyCycle = false });
        List<DomainEvent> events = [];
        MilitaryPolicies.ResolvePostAction(world, world.Units[1], Rules(), events);
        Assert.Equal(MilitaryOrderKind.Return, world.Units[1].Order!.Kind);
        Assert.Equal(UnitPolicyResponse.PostActionNoTarget, Assert.Single(events.OfType<UnitPolicyRespondedEvent>()).Response);
    }

    [Fact]
    public void PostActionRejectsANearbyTargetAcrossAnImpassableBarrier()
    {
        var world = World(policies: new(AttackAfterAction: true)).BeginMutation();
        world.Replace(world.Units[1] with { Order = null, AwaitingPostAction = true });
        world.Replace(world.Units[2] with { Position = new(30, 10) });
        var standard = Rules();
        var rules = new MilitaryRulebook(new(20, 5,
            Enumerable.Range(0, 100).Select(index => index % 20 == 5 ? 1 : 0).ToImmutableArray(),
            [new(0, 1, null), new(1, null, null)], standard.Formations.Values.ToImmutableArray(),
            standard.Places.Values.ToImmutableArray()) { PolicyRadius = 15 });
        List<DomainEvent> events = [];
        MilitaryPolicies.ResolvePostAction(world, world.Units[1], rules, events);
        Assert.Equal(MilitaryOrderKind.Return, world.Units[1].Order!.Kind);
        Assert.Equal(UnitPolicyResponse.PostActionNoTarget, Assert.Single(events.OfType<UnitPolicyRespondedEvent>()).Response);
    }
}
