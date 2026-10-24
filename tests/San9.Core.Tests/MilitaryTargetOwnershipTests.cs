using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Core.Tests;

public sealed partial class MilitaryCommandLifecycleTests
{
    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(false, 39, true)]
    [InlineData(false, 40, false)]
    [InlineData(false, 80, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, 80, false)]
    public void CaptureReconsidersExistingAttackAgainstTheNewOwner(bool allied, int relation, bool retain)
    {
        var world = OwnershipWorld(allied, relation);
        var oldOrder = world.Units[1].Order;
        List<DomainEvent> events = [];

        BattleAftermath.ResolveSettlementDefeat(world, 3, new(3, false), new() { Military = Rules() }, events);

        Assert.Equal(3, world.Settlements[3].OwnerId);
        Assert.Equal(retain, world.Units[1].Order == oldOrder);
        if (!retain)
        {
            Assert.Equal(new(MilitaryOrderKind.Return, null, 2), world.Units[1].Order);
            Assert.Null(world.Units[1].ContactTarget);
            Assert.Null(world.Units[1].Waypoint);
            Assert.Null(world.Units[1].Lookahead);
        }
        MilitaryDiplomacy.BeginAdvance(world, new() { Military = Rules() }, events);
        if (allied)
        {
            Assert.NotNull(world.Simulation.Alliance);
            Assert.DoesNotContain(events, e => e is WarDeclaredEvent { BetrayedAlliance: true });
        }
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void VoluntaryTransferToAnAllyReconsidersAnExistingAttack()
    {
        var world = OwnershipWorld(true, 70);
        world.RemoveUnit(3);
        world.Replace(world.Officers[22] with { UnitId = null, OwnerId = 2, SettlementId = 3 });
        List<DomainEvent> events = [];

        SettlementAffiliation.Transfer(world, world.Settlements[3], 3, world.Officers[22], [world.Officers[22]],
            SettlementDefectionKind.Isolated, new() { Military = Rules() }, events);

        Assert.Equal(new(MilitaryOrderKind.Return, null, 2), world.Units[1].Order);
        Assert.NotNull(world.Simulation.Alliance);
        WorldInvariants.Validate(world);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureByAnAttackRequestPartnerEndsTheOldAttack(bool reverseRequest)
    {
        var world = OwnershipWorld(false, 0);
        AttackRequests.Establish(world, reverseRequest ? 3 : 1, reverseRequest ? 1 : 3, 5, []);
        List<DomainEvent> events = [];

        BattleAftermath.ResolveSettlementDefeat(world, 3, new(3, false), new() { Military = Rules() }, events);

        Assert.Equal(new(MilitaryOrderKind.Return, null, 2), world.Units[1].Order);
        Assert.Single(world.Simulation.AttackRequests);
        MilitaryDiplomacy.BeginAdvance(world, new() { Military = Rules() }, events);
        Assert.DoesNotContain(events, e => e is WarDeclaredEvent);
        WorldInvariants.Validate(world);
    }

    [Theory]
    [InlineData(UnitCondition.Disguised, false)]
    [InlineData(UnitCondition.Disguised, true)]
    [InlineData(UnitCondition.NoFormation, false)]
    [InlineData(UnitCondition.NoFormation, true)]
    [InlineData(UnitCondition.Confused, false)]
    [InlineData(UnitCondition.Confused, true)]
    [InlineData(UnitCondition.Routed, false)]
    [InlineData(UnitCondition.Routed, true)]
    [InlineData(UnitCondition.HighSpirits, false)]
    [InlineData(UnitCondition.HighSpirits, true)]
    public void TargetOwnershipChangePreservesConditionsAndReconsidersTheSavedAttack(UnitCondition condition, bool allied)
    {
        var world = OwnershipWorld(allied, 0);
        world.Replace(world.Units[1] with { Condition = condition, ConditionDaysRemaining = 9,
            HighSpiritsMoralePenaltyPending = condition == UnitCondition.HighSpirits });
        var oldOrder = world.Units[1].Order;
        List<DomainEvent> events = [];

        BattleAftermath.ResolveSettlementDefeat(world, 3, new(3, false), new() { Military = Rules() }, events);

        Assert.Equal(condition, world.Units[1].Condition);
        Assert.Equal(9, world.Units[1].ConditionDaysRemaining);
        Assert.Equal(condition == UnitCondition.HighSpirits, world.Units[1].HighSpiritsMoralePenaltyPending);
        bool deferred = allied && condition != UnitCondition.HighSpirits;
        Assert.Equal(allied ? deferred ? null : new(MilitaryOrderKind.Return, null, 2) : oldOrder, world.Units[1].Order);
        Assert.Equal(deferred, world.Units[1].AwaitingPostAction);
        Assert.DoesNotContain(events.OfType<BattleConditionChangedEvent>(), e => e.ObjectId == 1 && !e.IsSettlement);
        MilitaryDiplomacy.BeginAdvance(world, new() { Military = Rules() }, events);
        Assert.DoesNotContain(events, e => e is WarDeclaredEvent { BetrayedAlliance: true });

        for (int day = 1; day <= 9; day++)
        {
            MilitaryConditions.Tick(world, events);
            MilitaryOrders.ResolveOrders(world, Rules(), events);
            if (day < 9)
            {
                Assert.Equal(condition, world.Units[1].Condition);
                Assert.Equal(9 - day, world.Units[1].ConditionDaysRemaining);
                if (deferred)
                {
                    Assert.Null(world.Units[1].Order);
                    Assert.True(world.Units[1].AwaitingPostAction);
                }
            }
        }
        Assert.Equal(UnitCondition.Normal, world.Units[1].Condition);
        Assert.Equal(allied ? new(MilitaryOrderKind.Return, null, 2) : oldOrder, world.Units[1].Order);
        Assert.False(world.Units[1].AwaitingPostAction);
        WorldInvariants.Validate(world);
    }

    [Theory]
    [InlineData(UnitCondition.Disguised)]
    [InlineData(UnitCondition.NoFormation)]
    [InlineData(UnitCondition.Confused)]
    [InlineData(UnitCondition.Routed)]
    [InlineData(UnitCondition.HighSpirits)]
    public void TargetDefeatClearsTheAttackWithoutChangingItsCondition(UnitCondition condition)
    {
        var world = World(new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 }, enemyUnit: true);
        world.Replace(world.Units[1] with { Condition = condition, ConditionDaysRemaining = 9,
            HighSpiritsMoralePenaltyPending = condition == UnitCondition.HighSpirits });
        world.Replace(world.Units[2] with { Soldiers = 0 });
        List<DomainEvent> events = [];

        BattleAftermath.TerminateUnit(world, 2, null, new() { Military = Rules() }, events);

        Assert.False(world.Units.ContainsKey(2));
        Assert.Equal(condition, world.Units[1].Condition);
        Assert.Equal(9, world.Units[1].ConditionDaysRemaining);
        Assert.Equal(condition == UnitCondition.HighSpirits, world.Units[1].HighSpiritsMoralePenaltyPending);
        Assert.DoesNotContain(events.OfType<BattleConditionChangedEvent>(), e => e.ObjectId == 1 && !e.IsSettlement);
        Assert.Null(world.Units[1].Order);
        Assert.True(world.Units[1].AwaitingPostAction);
        WorldInvariants.Validate(world);
    }

    [Theory]
    [InlineData(UnitCondition.Disguised)]
    [InlineData(UnitCondition.Routed)]
    public void ChangedTargetDuringForcedReturnSurvivesRestoreAndNaturalRecovery(UnitCondition condition)
    {
        var state = OwnershipState(true, 0);
        var setup = state.BeginMutation();
        setup.Replace(setup.Units[1] with { Condition = condition, ConditionDaysRemaining = 5 });
        BattleAftermath.ResolveSettlementDefeat(setup, 3, new(3, false), new() { Military = Rules() }, []);
        Assert.Null(setup.Units[1].Order);
        Assert.True(setup.Units[1].AwaitingPostAction);
        Assert.Equal(condition, setup.Units[1].Condition);
        Assert.Equal(5, setup.Units[1].ConditionDaysRemaining);
        state.Commit(setup);
        var first = new SimulationEngine(state, new SimulationRules { Military = Rules() });
        var restored = System.Text.Json.JsonSerializer.Deserialize<WorldSnapshot>(
            CanonicalJson.Text(first.Snapshot), CanonicalJson.Options)!;
        var second = new SimulationEngine(WorldState.Restore(restored), new SimulationRules { Military = Rules() });

        first.EndControl(new("player"));
        second.EndControl(new("player"));
        var result = first.AdvanceXun();
        EventAssert.Equal(result.Events, second.AdvanceXun().Events);
        StateAssert.Equal(first.Snapshot, second.Snapshot);
        Assert.Contains(result.Events.OfType<BattleConditionChangedEvent>(), e =>
            e.ObjectId == 1 && !e.IsSettlement && e.Condition == UnitCondition.Normal);
        Assert.Contains(result.Events.OfType<UnitOrderChangedEvent>(), e =>
            e.UnitId == 1 && e.Order == new MilitaryOrder(MilitaryOrderKind.Return, null, 2));
        Assert.DoesNotContain(result.Events, e => e is WarDeclaredEvent { BetrayedAlliance: true });
        Assert.NotNull(first.Snapshot.Simulation.Alliance);
    }

    [Fact]
    public void ExplicitAttackOnAnAllySurvivesOrdinaryResolutionUntilDeclaration()
    {
        var world = OwnershipWorld(true, 70);
        world.Replace(world.Settlements[3] with { OwnerId = 3, CorpsId = 3, Soldiers = 1000, Durability = 100 });
        var order = world.Units[1].Order;
        List<DomainEvent> events = [];

        MilitaryOrders.ResolveOrders(world, Rules(), events);
        Assert.Equal(order, world.Units[1].Order);
        MilitaryDiplomacy.BeginAdvance(world, new() { Military = Rules() }, events);

        Assert.Contains(events, e => e is WarDeclaredEvent { AttackerForceId: 1, DefenderForceId: 3, BetrayedAlliance: true });
        Assert.Null(world.Simulation.Alliance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedFieldTargetClearsOnlyTheAffectedAttack(bool temporary)
    {
        var world = World(new(MilitaryOrderKind.Move, new(67, 2), null), enemyUnit: true);
        world.SetSimulation(world.Simulation with { Alliance = new(2, 1, [1, 3], 30) });
        var unit = world.Units[1];
        world.Replace(unit with
        {
            Order = temporary ? unit.Order : new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 },
            EngagementTargetId = temporary ? 2 : null,
            ContactTarget = new(2, false)
        });
        world.Replace(world.Units[2] with { OwnerId = 3, CorpsId = 3, ServicePlaceId = 4 });
        world.Replace(world.Officers[22] with { OwnerId = 3 });
        List<DomainEvent> events = [];

        MilitaryOrders.UnitChanged(world, 2, Rules(), events);

        Assert.Null(world.Units[1].EngagementTargetId);
        Assert.Null(world.Units[1].ContactTarget);
        Assert.Equal(temporary ? unit.Order : new(MilitaryOrderKind.Return, null, 2), world.Units[1].Order);
        WorldInvariants.Validate(world);
    }

    private static WorldMutation OwnershipWorld(bool allied, int relation)
        => OwnershipState(allied, relation).BeginMutation();

    private static WorldState OwnershipState(bool allied, int relation)
    {
        var order = new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 3);
        var state = CreateWorld(order);
        var world = state.BeginMutation();
        world.AddSettlement(world.Settlements[3] with { Id = 5 });
        world.Replace(world.Officers[201] with { SettlementId = 5 });
        world.Replace(world.Officers[22] with { OwnerId = 3, SettlementId = null, UnitId = 3 });
        world.AddUnit(new(3, 3, new(45, 2), 5000) { CorpsId = 3, ServicePlaceId = 4, FormationId = 1,
            Morale = 80, CommanderId = 22, Order = order,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(22, BattleRank.Front) });
        world.Replace(world.Settlements[3] with { Soldiers = 0, Durability = 0 });
        world.SetRelation(new(1, 3, relation));
        if (allied) world.SetSimulation(world.Simulation with { Alliance = new(2, 1, [1, 3], 30) });
        state.Commit(world);
        return state;
    }
}
