using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class AttackRequestTests
{
    private static WorldMutation World() => WorldState.Create(new(190, 1, 1), 1,
        Enumerable.Range(1, 4).Select(id => new ForceState(id, 0, 0, 100) { BaseTrust = 100 }),
        Enumerable.Range(1, 4).Select(id => new SettlementState(id * 10, id, 0, 100, 100, 100, 0, 0, 0, 0, 0)), [],
        relations: [new(1, 2, 70), new(1, 3, 70), new(2, 3, 70)]).BeginMutation();

    [Fact]
    public void ReciprocalExpiryChargesEachHolderAndClearsBothDirections()
    {
        var world = World();
        List<DomainEvent> events = [];
        AttackRequests.Establish(world, 1, 2, 30, events);
        AttackRequests.Establish(world, 2, 1, 30, events);
        for (int xun = 0; xun < 18; xun++) AttackRequests.UpdateXun(world, events);
        Assert.All(world.Simulation.AttackRequests, r => Assert.Equal(1, r.RemainingXun));
        Assert.Equal(100, world.Forces[1].BaseTrust);
        AttackRequests.UpdateXun(world, events);
        Assert.Empty(world.Simulation.AttackRequests);
        Assert.Equal(80, world.Forces[1].BaseTrust);
        Assert.Equal(80, world.Forces[2].BaseTrust);
        Assert.Equal(30, MilitaryDiplomacy.Relation(world, 1, 2));
        Assert.Equal(new int?[] { 1, 2 }, events.OfType<AttackRequestEndedEvent>().Select(e => e.LiableForceId));
    }

    [Fact]
    public void ActiveBreachChargesActorForBothDirectionsAndThenCannotRepeat()
    {
        var world = World();
        List<DomainEvent> events = [];
        AttackRequests.Establish(world, 1, 2, 30, events);
        AttackRequests.Establish(world, 2, 1, 30, events);
        Assert.True(AttackRequests.BreakPair(world, 2, 1, events));
        Assert.False(AttackRequests.BreakPair(world, 2, 1, events));
        Assert.Equal(100, world.Forces[1].BaseTrust);
        Assert.Equal(60, world.Forces[2].BaseTrust);
        Assert.Equal(2, events.OfType<AttackRequestEndedEvent>().Count());
    }

    [Fact]
    public void FulfillmentDoesNotRewardTwiceAndOnlyConsumesTheMatchingDirection()
    {
        var world = World();
        List<DomainEvent> events = [];
        AttackRequests.Establish(world, 1, 2, 30, events);
        AttackRequests.Establish(world, 2, 1, 40, events);
        Assert.False(AttackRequests.Fulfill(world, 1, 40, events));
        Assert.True(AttackRequests.Fulfill(world, 1, 30, events));
        Assert.False(AttackRequests.Fulfill(world, 1, 30, events));
        Assert.Equal(110, world.Forces[1].BaseTrust);
        Assert.Equal(2, Assert.Single(world.Simulation.AttackRequests).RequestedForceId);
    }

    [Fact]
    public void ChangedOwnerCancelsWithoutRewardOrPenaltyAndDuplicateDoesNotResetExpiry()
    {
        var world = World();
        List<DomainEvent> events = [];
        AttackRequests.Establish(world, 1, 2, 30, events);
        AttackRequests.UpdateXun(world, events);
        Assert.False(AttackRequests.Establish(world, 1, 2, 30, events));
        Assert.Equal(18, Assert.Single(world.Simulation.AttackRequests).RemainingXun);
        world.Replace(world.Settlements[30] with { OwnerId = 4 });
        Assert.False(AttackRequests.Fulfill(world, 1, 30, events));
        AttackRequests.Cleanup(world, events);
        Assert.Equal(AttackRequestEndReason.Invalidated, Assert.Single(events.OfType<AttackRequestEndedEvent>()).Reason);
        Assert.Equal(100, world.Forces[1].BaseTrust);
        Assert.Equal(70, MilitaryDiplomacy.Relation(world, 1, 2));
    }

    [Fact]
    public void AllianceTargetBreachPrecedesRelationNormalizationAndInternalTargetRequestSurvives()
    {
        var world = World();
        List<DomainEvent> events = [];
        AttackRequests.Establish(world, 1, 3, 40, events);
        AttackRequests.Establish(world, 2, 1, 30, events);
        AttackRequests.FormAlliance(world, 1, 3, new HashSet<int> { 1, 2 }, events);
        ForceRelations.Set(world, 1, 3, DiplomacyMath.AllianceTargetRelation(MilitaryDiplomacy.Relation(world, 1, 3)), events);
        Assert.Equal(30, MilitaryDiplomacy.Relation(world, 1, 3));
        Assert.Equal(80, world.Forces[1].BaseTrust);
        Assert.Equal(2, Assert.Single(world.Simulation.AttackRequests).RequestedForceId);
    }

    [Fact]
    public void PairProtectionAffectsAutomaticEnemiesWithoutErasingExplicitCombatEligibility()
    {
        var world = World();
        List<DomainEvent> events = [];
        ForceRelations.Set(world, 1, 2, 0, events);
        Assert.True(MilitaryDiplomacy.AutomaticEnemy(world, 1, 2));
        AttackRequests.Establish(world, 1, 2, 30, events);
        Assert.False(MilitaryDiplomacy.AutomaticEnemy(world, 1, 2));
        Assert.False(MilitaryDiplomacy.AutomaticEnemy(world, 2, 1));
        Assert.True(MilitaryDiplomacy.CanFight(world, 1, 2));
    }

    [Fact]
    public void InvalidOrDuplicateDirectedRequestsCannotBecomeCommittedState()
    {
        var world = World();
        List<DomainEvent> events = [];
        AttackRequests.Establish(world, 1, 2, 30, events);
        var request = Assert.Single(world.Simulation.AttackRequests);
        foreach (var invalid in new[] { request with { RemainingXun = 0 }, request with { TargetForceId = 4 }, request with { RequesterForceId = 1 } })
        {
            world.SetSimulation(world.Simulation with { AttackRequests = [invalid] });
            Assert.Throws<ArgumentException>(() => AttackRequests.Validate(world));
        }
        world.SetSimulation(world.Simulation with { AttackRequests = [request, request] });
        Assert.Throws<ArgumentException>(() => AttackRequests.Validate(world));
    }
}
