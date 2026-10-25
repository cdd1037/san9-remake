using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed partial class MilitaryCommandLifecycleTests
{
    [Theory]
    [InlineData(MilitaryOrderKind.Return)]
    [InlineData(MilitaryOrderKind.Transport)]
    public void CapturedDestinationRepairsServicePlaceBeforeReturning(MilitaryOrderKind kind)
    {
        var world = World(new(kind, null, 1), enemyUnit: true);
        world.Replace(world.Units[1] with { ServicePlaceId = 1, CargoFood = 100 });
        world.Replace(world.Settlements[1] with { Soldiers = 0, Durability = 0 });
        world.Replace(world.Units[2] with { Position = new(7, 2), Order = new(MilitaryOrderKind.AttackSettlement, null, 1) });
        var rules = Rules();
        BattleAftermath.ResolveSettlementDefeat(world, 1, new(2, false), new() { Military = rules }, []);
        Assert.Equal(2, world.Settlements[1].OwnerId);
        Assert.Equal(2, world.Units[1].ServicePlaceId);
        Assert.Equal(new(MilitaryOrderKind.Return, null, 2), world.Units[1].Order);
        var position = world.Units[1].Position;
        MilitaryMovement.AdvanceDay(world, rules, new([]), PersonnelRulebook.Empty, []);
        Assert.NotEqual(position, world.Units[1].Position);
        Assert.Equal(100u, world.Units[1].CargoFood);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void VoluntarySettlementTransferLetsItsAttackerEnter()
    {
        var world = World(new(MilitaryOrderKind.AttackSettlement, null, 3));
        world.AddSettlement(world.Settlements[3] with { Id = 5 });
        world.Replace(world.Officers[201] with { SettlementId = 5 });
        SettlementAffiliation.Transfer(world, world.Settlements[3], 1, world.Officers[22], [world.Officers[22]],
            SettlementDefectionKind.Isolated, new() { Military = Rules() }, []);
        Assert.Equal(new(MilitaryOrderKind.Return, null, 3), world.Units[1].Order);
        Assert.False(world.Units[1].AwaitingPostAction);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void ThirdPartyCapturePreservesStillValidAttack()
    {
        var order = new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 3);
        var world = World(order);
        world.AddSettlement(world.Settlements[3] with { Id = 5 });
        world.Replace(world.Officers[201] with { SettlementId = 5 });
        world.Replace(world.Officers[22] with { OwnerId = 3, SettlementId = null, UnitId = 3 });
        world.AddUnit(new(3, 3, new(45, 2), 5000) { CorpsId = 3, ServicePlaceId = 4, FormationId = 1,
            Morale = 80, CommanderId = 22, Order = order,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(22, BattleRank.Front) });
        world.Replace(world.Settlements[3] with { Soldiers = 0, Durability = 0 });
        BattleAftermath.ResolveSettlementDefeat(world, 3, new(3, false), new() { Military = Rules() }, []);
        MilitaryOrders.ResolveOrders(world, Rules(), []);
        Assert.Equal(3, world.Settlements[3].OwnerId);
        Assert.Equal(order, world.Units[1].Order);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void CrossingWaterRetainsInlandMissionWithoutFiringFromTheShip()
    {
        var order = new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 3);
        var world = World(order, position: new(22, 17), naval: true);
        var rules = Rules();
        Assert.Equal(2, rules.Formation(world.Units[1]).Id);
        List<DomainEvent> events = [];
        MilitaryOrders.ResolveOrders(world, rules, events);
        MilitaryCombat.AdvanceDay(world, new() { Military = rules }, events);
        Assert.Equal(order, world.Units[1].Order);
        Assert.Empty(events.OfType<BattleDamageEvent>());
        for (int i = 0; i < 30 && rules.Map.IsWater(world.Units[1].Position); i++)
            MilitaryMovement.AdvanceDay(world, rules, new([]), PersonnelRulebook.Empty, events);
        Assert.False(rules.Map.IsWater(world.Units[1].Position));
        Assert.Equal(order, world.Units[1].Order);
    }

    [Theory]
    [InlineData(MilitaryOrderKind.Return)]
    [InlineData(MilitaryOrderKind.Transport)]
    public void FailedReturnRetainsContextUntilAServicePlaceReopens(MilitaryOrderKind kind)
    {
        var order = new MilitaryOrder(kind, null, 1);
        var world = World(order);
        foreach (int id in new[] { 1, 2 }) world.Replace(world.Settlements[id] with { Durability = 0 });
        var rules = Rules();
        MilitaryOrders.RemoveTarget(world, 1, true, []);
        MilitaryOrders.ResolveOrders(world, rules, []);
        MilitaryMovement.AdvanceDay(world, rules, new([]), PersonnelRulebook.Empty, []);
        Assert.Equal(new(MilitaryOrderKind.Return, null, 2), world.Units[1].Order);
        world.Replace(world.Settlements[2] with { Durability = 100 });
        MilitaryOrders.ResolveOrders(world, rules, []);
        Assert.Equal(new(MilitaryOrderKind.Return, null, 2), world.Units[1].Order);
        WorldInvariants.Validate(world);
    }

    [Theory]
    [InlineData(80, 20000u, MilitaryOrderKind.AttackUnit)]
    [InlineData(30, 20000u, MilitaryOrderKind.Return)]
    [InlineData(80, 1000u, MilitaryOrderKind.Return)]
    public void TransferredArmySelectsNewActionUsingMoraleAndStrength(int morale, uint soldiers, MilitaryOrderKind expected)
    {
        var world = World(new(MilitaryOrderKind.Move, new(67, 2), null), enemyUnit: true);
        world.Replace(world.Units[1] with { Soldiers = soldiers, Morale = morale, Position = new(22, 7),
            Condition = UnitCondition.Confused, ConditionDaysRemaining = 5 });
        world.Replace(world.Units[2] with { Position = new(27, 7) });
        MilitaryPolicies.AfterTransfer(world, 1, Rules(), []);
        Assert.Equal(expected, world.Units[1].Order!.Kind);
        Assert.Equal(UnitCondition.Normal, world.Units[1].Condition);
        Assert.Equal(0, world.Units[1].ConditionDaysRemaining);
        if (expected == MilitaryOrderKind.AttackUnit) Assert.Equal(2, world.Units[1].Order!.UnitId);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void FinishedTransportWithoutAServiceReferenceWaitsWithoutRevivingDelivery()
    {
        var world = World(new(MilitaryOrderKind.Transport, null, 1));
        world.Replace(world.Units[1] with { ServicePlaceId = null });
        foreach (int id in new[] { 1, 2 }) world.Replace(world.Settlements[id] with { Durability = 0 });
        List<DomainEvent> events = [];
        MilitaryOrders.ReturnAfterTask(world, world.Units[1], Rules(), events, "target-invalid");
        var pending = new MilitaryOrder(MilitaryOrderKind.Return, null, null);
        Assert.Equal(pending, world.Units[1].Order);
        Assert.Empty(events.OfType<UnitPolicyRespondedEvent>());
        WorldInvariants.Validate(world);
        world.Replace(world.Settlements[1] with { Durability = 100 });
        MilitaryOrders.ResolveOrders(world, Rules(), events);
        Assert.Equal(new(MilitaryOrderKind.Return, null, 1), world.Units[1].Order);
        Assert.Equal(1, world.Units[1].ServicePlaceId);
        Assert.Single(events.OfType<UnitPolicyRespondedEvent>(), e => e.Response == UnitPolicyResponse.ReturnAfterAction);
        int changes = events.OfType<UnitOrderChangedEvent>().Count();
        MilitaryOrders.ResolveOrders(world, Rules(), events);
        Assert.Equal(changes, events.OfType<UnitOrderChangedEvent>().Count());
    }

    [Fact]
    public void RebuildingARemovedTargetBeforeDailyResolutionDoesNotResumeTransport()
    {
        var world = World(new(MilitaryOrderKind.Transport, null, 1));
        var place = world.Settlements[1];
        world.Replace(place with { IsDestroyed = true, OwnerId = null, CorpsId = null, Soldiers = 0, Durability = 0 });
        MilitaryOrders.RemoveTarget(world, 1, true, []);
        world.Replace(place);
        MilitaryOrders.ResolveOrders(world, Rules(), []);
        Assert.Equal(new(MilitaryOrderKind.Return, null, 2), world.Units[1].Order);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void TransferredArmyCanChooseAPopulatedBuildingButIdleArmyStaysIdle()
    {
        var world = World(new(MilitaryOrderKind.Move, new(67, 2), null), position: new(42, 2));
        world.Replace(world.Units[1] with { Soldiers = 20000 });
        MilitaryPolicies.AfterTransfer(world, 1, Rules(), []);
        Assert.Equal(new(MilitaryOrderKind.AttackSettlement, null, 3), world.Units[1].Order);
        world.Replace(world.Units[1] with { Order = null });
        MilitaryPolicies.AfterTransfer(world, 1, Rules(), []);
        Assert.Null(world.Units[1].Order);
    }

    private static MilitaryRulebook Rules() => new(new(20, 8,
        Enumerable.Range(0, 160).Select(i => i / 20 == 3 ? 1 : 0).ToImmutableArray(),
        [new(0, 1, null), new(1, null, 1) { IsWater = true }],
        [new(1, 10, 0, 100000, 1, new(1, 1), TravelMode.Land)
            { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true), CaptureRanks = [BattleRank.Front] },
         new(2, 10, 0, 100000, 1, new(1, 1), TravelMode.Water)
            { Combat = new(10, 10, 10, 10, 50), Attack = new(1, false) { RequiresWaterContact = true }, CaptureRanks = [BattleRank.Front] },
         new(3, 10, 0, 100000, 1, new(1, 1), TravelMode.Land)],
        [new(1, new(2, 2), new(1, 1)) { Combat = new(10, 10, 10, 10, 50) },
         new(2, new(92, 2), new(1, 1)) { Combat = new(10, 10, 10, 10, 50) },
         new(3, new(47, 2), new(1, 1)) { Combat = new(10, 10, 10, 10, 50) },
         new(4, new(77, 2), new(1, 1)) { Combat = new(10, 10, 10, 10, 50) }]) { TransportFormations = new(3, null) });

    private static WorldMutation World(MilitaryOrder order, bool enemyUnit = false, WorldPosition? position = null, bool naval = false)
        => CreateWorld(order, enemyUnit, position, naval).BeginMutation();

    private static WorldState CreateWorld(MilitaryOrder order, bool enemyUnit = false, WorldPosition? position = null, bool naval = false)
    {
        OfficerState Person(int id, int owner, int? home) => new(id, owner, home, new(80, 80, 80, 80), false);
        UnitState unit = new(1, 1, position ?? new(22, 7), 5000)
        {
            FormationId = order.Kind == MilitaryOrderKind.Transport ? 3 : 1, NavalFormationId = naval ? 2 : null,
            CommanderId = 11, CorpsId = 1, ServicePlaceId = 2, Morale = 80, Order = order,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front)
        };
        return WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100) { LeaderId = 101 }, new(2, 100) { LeaderId = 201 }, new(3, 100) { LeaderId = 301 }],
            Enumerable.Range(1, 4).Select(id => new SettlementState(id, id <= 2 ? 1 : id - 1, 5000, 100, 100, 80, 500, 0, 100, 0, 100)
                { CorpsId = id <= 2 ? 1 : id - 1 }).ToArray(),
            [Person(11, 1, null) with { UnitId = 1 }, Person(101, 1, 2) with { Status = OfficerStatus.Ruler },
             Person(201, 2, 3) with { Status = OfficerStatus.Ruler }, Person(301, 3, 4) with { Status = OfficerStatus.Ruler },
             Person(22, 2, enemyUnit ? null : 3) with { UnitId = enemyUnit ? 2 : null }],
            enemyUnit ? [unit, new(2, 2, new(37, 7), 5000)
                { FormationId = 1, CommanderId = 22, CorpsId = 2, ServicePlaceId = 3, Morale = 80,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(22, BattleRank.Front) }] : [unit],
            relations: [new(1, 2, 0), new(1, 3, 0), new(2, 3, 0)], controls: [new("player", 1, ControllerKind.Player)],
            corps: [new CorpsState(1, 1, 101, new(10000, 10000)) { IsRoot = true },
                new CorpsState(2, 2, 201, new(10000, 10000)) { IsRoot = true },
                new CorpsState(3, 3, 301, new(10000, 10000)) { IsRoot = true }]);
    }
}
