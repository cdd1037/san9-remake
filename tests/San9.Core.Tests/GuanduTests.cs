using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class GuanduTests
{
    private static readonly PersonPlace[] Places = [new(1, new(2, 2)), new(2, new(37, 2)), new(3, new(7, 2)),
        new(4, new(12, 2)), new(5, new(27, 2)), new(6, new(37, 12))];
    private static GuanduDefinition Definition => new(11, 21, 23, 3, [1, 3], [4], 90, 20, 30000, 90, 70, 50, 15, 15, new("guandu"));
    private static MilitaryRulebook Military => new(new(8, 3, Enumerable.Repeat(0, 24).ToImmutableArray(),
        [new(0, 1, 1)], [new(1, 1, 0, 100000, 5, new(1, 1), TravelMode.Land)],
        Places.Select(p => new BattlefieldPlace(p.Id, p.Position, new(1, 1))).ToImmutableArray())
        { RegionIds = Enumerable.Range(0, 24).Select(i => (int?)(i / 8 == 2 ? 6 : (i % 8) switch
            { 0 => 1, 1 => 3, >= 2 and <= 4 => 4, 5 => 5, _ => 2 })).ToImmutableArray() });
    private static SimulationRules Rules => new() { Guandu = Definition, Military = Military, Personnel = new(Places) };
    private static OfficerState Person(int id, int owner, int? home, OfficerStatus status = OfficerStatus.Serving, int? unit = null) =>
        new OfficerState(id, owner, home, new(50, 50, 50, 50), false) { Status = status, UnitId = unit }.WithLoyalty(owner, 89);
    private static WorldState State() => WorldState.Create(new(190, 2, 1), 17,
        [new(1, 980) { LeaderId = 11, BaseTrust = 980 }, new(2, 10) { LeaderId = 21, BaseTrust = 10 },
         new(3, 100) { LeaderId = 31, BaseTrust = 100 }],
        Places.Select(p => new SettlementState(p.Id, p.Id is 1 or 3 ? 1 : p.Id == 6 ? 3 : 2,
            p.Id == 4 ? 9999u : 1000u, 100, 100, 60, 100, 100, 100, 100, 100)
            { Kind = p.Id is 1 or 2 or 6 ? SettlementKind.City : SettlementKind.Facility, ConsumesFood = false, Wounded = 20 }),
        [Person(11, 1, 3, OfficerStatus.Ruler), Person(12, 1, 1), Person(13, 1, 1).WithLoyalty(1, 250),
         Person(14, 1, 2, OfficerStatus.Captive), Person(21, 2, 2, OfficerStatus.Ruler), Person(22, 2, null, unit: 20),
         Person(23, 2, 2).WithLoyalty(1, 0), Person(24, 2, null, unit: 21), Person(31, 3, 6, OfficerStatus.Ruler)],
        units: [new UnitState(20, 2, new(12, 7), 20001) { FormationId = 1, CommanderId = 22, ServicePlaceId = 2,
            OrderClass = UnitOrderClass.Military, Order = new(MilitaryOrderKind.Move, new(27, 7), null), Wounded = 11, CargoFood = 99, Morale = 61 },
            new UnitState(21, 2, new(27, 7), 20000) { FormationId = 1, CommanderId = 24, ServicePlaceId = 2,
                OrderClass = UnitOrderClass.Military, Order = new(MilitaryOrderKind.Move, new(12, 7), null) }],
        controls: [new("player", 1, ControllerKind.Player), new("enemy", 2, ControllerKind.Automatic), new("third", 3, ControllerKind.Automatic)],
        relations: [new(1, 2, 19)], annualAppearancesEnabled: false, naturalDeathsEnabled: false, officeRequestsEnabled: false,
        freeOfficerMigrationEnabled: false, forceEmergenceEnabled: false, cityIndependenceEnabled: false, forceConsolidationEnabled: false, corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(1000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(1000, 10001)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 31, new(1000, 1000)) { IsRoot = true }]);

    [Fact]
    public void PreRecruitmentVictoryUsesCurrentRegionsAndRootResourcesWithoutRewardingTheRecruit()
    {
        var state = State(); var world = state.BeginMutation(); List<DomainEvent> events = [];
        Assert.Equal(4, Military.RegionAt(world.Units[20].Position));
        Assert.False(world.Officers[23].IsCommander);
        Assert.Equal(89, world.Officers[23].Loyalty);
        Assert.Equal(0, RecruitmentRules.Threshold(world, 12, 23, new()));
        Assert.Equal(100, RecruitmentRules.Threshold(world, 12, 23, Rules));
        Assert.True(Guandu.Apply(world, 12, 23, Rules, events));
        var result = Assert.Single(events.OfType<GuanduEvent>());
        Assert.Equal(new[] { 20 }, result.AffectedUnitIds); Assert.Equal(new[] { 4 }, result.AffectedSettlementIds);
        Assert.Equal(26999, result.SoldiersLost); Assert.Equal(7000u, result.FoodLost);
        Assert.Equal((20, 10), (result.BaseTrustGain, result.EnemyBaseTrustLoss));
        Assert.Equal(2001u, world.Units[20].Soldiers); Assert.Equal(1000u, world.Settlements[4].Soldiers);
        Assert.Equal((11u, 99u, 61), (world.Units[20].Wounded, world.Units[20].CargoFood, world.Units[20].Morale));
        Assert.Equal((20u, 100), (world.Settlements[4].Wounded, world.Settlements[4].Durability));
        Assert.Equal(20000u, world.Units[21].Soldiers); Assert.Equal(1000u, world.Settlements[2].Soldiers);
        Assert.Equal(new MilitaryOrder(MilitaryOrderKind.Return, null, 2), world.Units[20].Order);
        Assert.Equal(3001u, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Food); Assert.Equal(1000u, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Money);
        Assert.Equal(104, world.Officers[12].Loyalty); Assert.Equal(255, world.Officers[13].Loyalty);
        Assert.Equal(89, world.Officers[11].Loyalty); Assert.Equal(89, world.Officers[14].Loyalty);
        Assert.Equal(89, world.Officers[23].Loyalty); Assert.Equal(2, world.Officers[23].OwnerId);
        Assert.DoesNotContain(events.OfType<OfficerLoyaltyChangedEvent>(), e => e.OfficerId == 23);
        Assert.Empty(world.Simulation.RandomStreams); Assert.False(Guandu.Apply(world, 12, 23, Rules, events));
        world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.Stable });
        state.Commit(world);
        Assert.Equal(state.CreateSnapshot().StateHash, WorldState.Restore(state.CreateSnapshot()).CreateSnapshot().StateHash);
    }

    [Theory]
    [InlineData("loyalty")][InlineData("relation")][InlineData("soldiers")][InlineData("food")]
    [InlineData("commander")][InlineData("player")][InlineData("required-place")][InlineData("patron-region")]
    [InlineData("patron-away")][InlineData("alliance")][InlineData("consumed")][InlineData("work")]
    [InlineData("personnel-unit")][InlineData("recruit-force")][InlineData("opponent-dead")][InlineData("recruit-dead")]
    public void FailedQualificationDoesNotMutateOrConsumeRandom(string failure)
    {
        var state = State(); var world = state.BeginMutation();
        switch (failure)
        {
            case "loyalty": world.Replace(world.Officers[23].WithLoyalty(2, 90)); break;
            case "relation": ForceRelations.Set(world, 1, 2, 20, []); break;
            case "soldiers": world.Replace(world.Settlements[4] with { Soldiers = 9998 }); break;
            case "food": world.Replace(ResourceAccounts.Root(world, 2) with { Treasury = ResourceAccounts.Root(world, 2).Treasury with { Food = 0 } }); break;
            case "commander": world.Replace(world.Officers[23] with { IsCommander = true }); break;
            case "player": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
            case "required-place": world.Replace(world.Settlements[3] with { OwnerId = 2 }); break;
            case "patron-region": world.Replace(world.Officers[11] with { SettlementId = 2 }); break;
            case "patron-away":
                world.SetSimulation(world.Simulation with { Tasks = [new(Guid.NewGuid(), PersonnelMissions.RecruitKind, "player", 1, 11, 3,
                    world.Clock, world.Clock.NextDay(), 0, 0) { Journey = new(2, 23, 50) { Route = new(new(7, 2), new(37, 2), false) } }] }); break;
            case "alliance": world.SetSimulation(world.Simulation with { Alliance = new(3, 1, [1, 2], 10) }); break;
            case "consumed": world.SetSimulation(world.Simulation with { ConsumedEventKeys = [Definition.ConsumptionKey] }); break;
            case "work": world.Replace(world.Settlements[4] with { CanHostPersons = false }); break;
            case "personnel-unit": world.Replace(world.Units[20] with { IsPersonnel = true }); break;
            case "recruit-force": world.Replace(world.Officers[23] with { OwnerId = 3 }); break;
            case "opponent-dead": world.Replace(world.Officers[21] with { Status = OfficerStatus.Dead }); break;
            case "recruit-dead": world.Replace(world.Officers[23] with { Status = OfficerStatus.Dead }); break;
        }
        List<DomainEvent> events = [];
        Assert.False(Guandu.Eligible(world, 12, 23, Rules)); Assert.False(Guandu.Apply(world, 12, 23, Rules, events));
        Assert.Empty(events); Assert.Empty(world.Simulation.RandomStreams);
    }

    [Fact]
    public void NonMilitaryUnitsLoseSoldiersWithoutChangingTheirCommandAndRolesNeedNotBeRulers()
    {
        var state = State(); var world = state.BeginMutation();
        world.Replace(world.Forces[1] with { LeaderId = 13 }); world.Replace(ResourceAccounts.Root(world, 1) with { LeaderId = 13 });
        world.Replace(world.Officers[13] with { Status = OfficerStatus.Ruler });
        world.Replace(world.Officers[11] with { Status = OfficerStatus.Serving });
        world.Replace(world.Forces[2] with { LeaderId = 24 }); world.Replace(ResourceAccounts.Root(world, 2) with { LeaderId = 24 });
        world.Replace(world.Officers[24] with { Status = OfficerStatus.Ruler });
        world.Replace(world.Officers[21] with { Status = OfficerStatus.Serving });
        world.Replace(world.Units[20] with { OrderClass = UnitOrderClass.NonMilitary });
        var previousOrder = world.Units[20].Order;
        Assert.True(Guandu.Apply(world, 12, 23, Rules, []));
        Assert.Equal(2001u, world.Units[20].Soldiers); Assert.Equal(previousOrder, world.Units[20].Order);
        Assert.Equal(104, world.Officers[11].Loyalty);
    }

    [Fact]
    public void IndependentTreasuryDoesNotSatisfyOrPayTheRootFoodRequirement()
    {
        var world = State().BeginMutation();
        world.AddCorps(new(50, 2, 23, new(3000, 7777)));
        world.Replace(ResourceAccounts.Root(world, 2) with { Treasury = ResourceAccounts.Root(world, 2).Treasury with { Food = 0 } });
        Assert.False(Guandu.Eligible(world, 12, 23, Rules));
        world.Replace(ResourceAccounts.Root(world, 2) with { Treasury = ResourceAccounts.Root(world, 2).Treasury with { Food = 1 } });
        Assert.True(Guandu.Apply(world, 12, 23, Rules, []));
        Assert.Equal(1u, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Food);
        Assert.Equal(new CorpsTreasury(3000, 7777), world.Corps[50].Treasury);
    }

    [Fact]
    public void AuthorDefinedTotalLossUsesTheNormalUnitTerminationChain()
    {
        var state = State(); var world = state.BeginMutation();
        var rules = new SimulationRules { Guandu = Definition with { SoldierLossPercent = 100 }, Military = Military, Personnel = new(Places) };
        Assert.True(Guandu.Apply(world, 12, 23, rules, []));
        Assert.False(world.Units.ContainsKey(20));
        Assert.Equal(0u, world.Settlements[4].Soldiers);
        state.Commit(world);
    }

    [Fact]
    public void NormalRecruitmentCompletesAfterHistoricalEffectsAndInitializesRecruitLoyaltyOnce()
    {
        var state = State(); var world = state.BeginMutation(); var rules = Rules;
        var task = new PersistentTask(Guid.NewGuid(), PersonnelMissions.RecruitKind, "player", 1, 12, 1,
            world.Clock, world.Clock.NextDay().NextDay(), 2, 0) { Journey = new(2, 23, 99) { Route = new(new(2, 2), new(37, 2), false) } };
        List<DomainEvent> events = [];
        new PersonnelMissionRule(rules, PersonnelMissions.RecruitKind).Complete(world, task, events);
        Assert.Single(events.OfType<GuanduEvent>());
        var recruit = Assert.Single(events.OfType<RecruitmentResolvedEvent>());
        Assert.Equal((100, 99, true), (recruit.Threshold, recruit.Roll, recruit.Success));
        Assert.Equal(1, world.Officers[23].OwnerId); Assert.Equal(100, world.Officers[23].Loyalty);
        Assert.True(events.FindIndex(e => e is GuanduEvent) < events.FindIndex(e => e is RecruitmentResolvedEvent));
        Assert.DoesNotContain(events.OfType<OfficerLoyaltyChangedEvent>(), e => e.OfficerId == 23 && e.Reason == "guandu");
        Assert.Equal(2, world.Simulation.Tasks.Length);
    }
}
