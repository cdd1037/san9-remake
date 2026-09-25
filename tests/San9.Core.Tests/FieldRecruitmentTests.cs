using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class FieldRecruitmentTests
{
    private static readonly SimulationRules Rules = new()
    {
        Personnel = new([new(1, new(2, 2)), new(2, new(902, 2))], new Dictionary<int, int> { [1] = 1, [2] = 2 }, [new(1, 2), new(2, 1)]),
        Military = new(new(200, 2, Enumerable.Repeat(0, 400).ToImmutableArray(), [new(0, 1, null)],
            [new(1, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 2), new(1, 1)), new(2, new(902, 2), new(1, 1))])
            { RegionIds = Enumerable.Repeat<int?>(2, 400).ToImmutableArray() })
    };

    private static OfficerState Person(int id, int owner, int? home, int loyalty = 0) =>
        new OfficerState(id, owner, home, new(id == 20 ? 100 : 50, 50, 100, 50), false)
        { Status = id is 1 or 2 ? OfficerStatus.Ruler : OfficerStatus.Serving, UnitId = home is null ? 100 : null }
        .WithLoyalty(owner, loyalty).WithLoyalty(1, owner == 2 ? 255 : 100);

    internal static WorldState World() => WorldState.Create(new(190, 1, 1), 17,
        [new(1, 1000) { LeaderId = 1, BaseTrust = 1000 }, new(2, 0) { LeaderId = 2 }],
        [new(1, 1, 1000, 100, 100, 70, 0, 0, 0, 0, 0) { CorpsId = 1, ConsumesFood = false },
         new(2, 2, 1000, 100, 100, 70, 0, 0, 0, 0, 0) { CorpsId = 2, ConsumesFood = false }],
        [Person(1, 1, 1), Person(2, 2, 2), Person(11, 1, 1), Person(20, 2, null), Person(21, 2, null, 100), Person(22, 2, null),
         Person(30, 1, 1) with { OwnerId = null, Status = OfficerStatus.Undiscovered }],
        units: [new(100, 2, new(332, 2), 5000) { CorpsId = 2, ServicePlaceId = 2, FormationId = 1, CommanderId = 20,
            Morale = 70, Wounded = 123, CargoMoney = 345, CargoFood = 678,
            MemberRanks = new Dictionary<int, BattleRank> { [20] = BattleRank.Front, [21] = BattleRank.Middle, [22] = BattleRank.Rear }.ToImmutableDictionary() }],
        corps: [new(1, 1, 1, new(10000, 10000)) { IsRoot = true }, new(2, 2, 2, new(10000, 10000)) { IsRoot = true }], relations: [new(1, 2, 50)],
        controls: [new("player", 1, ControllerKind.Player)]);

    private static (WorldMutation World, PersistentTask Task) Submit(int target = 20)
    {
        var state = World(); var engine = new SimulationEngine(state, Rules);
        Assert.True(engine.Execute(new("player"), new RecruitOfficerCommand(Guid.NewGuid(), 1, 0, 11, target)).Submission.Accepted);
        return (state.BeginMutation(Rules.Abilities), Assert.Single(engine.Snapshot.Simulation.Tasks));
    }

    private static List<DomainEvent> Complete(WorldMutation world, PersistentTask task)
    {
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Remove(task) });
        List<DomainEvent> events = [];
        PersonnelDefection.Recruit(world, task, Rules, events);
        WorldInvariants.Validate(world);
        return events;
    }

    [Fact]
    public void CommanderBringsArmyButNotHomeAndLoyalMembersReturnFromField()
    {
        var (world, task) = Submit();
        var before = world.Units[100]; var home = world.Settlements[2];
        Assert.False(world.Officers[20].IsCommander); // Field commanders have a separate authoritative identity.
        var events = Complete(world, task);
        var army = world.Units[100];
        Assert.Equal((1, 1, 1), (army.OwnerId, army.CorpsId, army.ServicePlaceId));
        Assert.Equal((before.Position, before.Soldiers, before.Wounded, before.Morale, before.CargoMoney, before.CargoFood),
            (army.Position, army.Soldiers, army.Wounded, army.Morale, army.CargoMoney, army.CargoFood));
        Assert.Equal(home, world.Settlements[2]);
        Assert.Equal(new[] { 20, 22 }, world.Officers.Values.Where(p => p.UnitId == 100).OrderBy(p => p.Id).Select(p => p.Id));
        Assert.Equal(45, world.Officers[20].LoyaltyTo(2));
        Assert.Equal(1, world.Officers[22].OwnerId);
        Assert.Equal((2, 2, (int?)null), (world.Officers[21].OwnerId, world.Officers[21].SettlementId, world.Officers[21].UnitId));
        var returning = Assert.Single(world.Simulation.Tasks);
        Assert.Equal(21, returning.OfficerId);
        Assert.True(returning.Journey!.ReturnLeg!.FieldTravel);
        Assert.Equal(before.Position, returning.Journey.Route.Origin);
        Assert.Single(events.OfType<UnitTransferredEvent>());
        Assert.Contains(events.OfType<ResourceChangedEvent>(), e => e.Reason == "personnel-recruited");
    }

    [Fact]
    public void OrdinaryMemberLeavesWithoutTakingTheArmy()
    {
        var (world, task) = Submit(22);
        var before = world.Units[100];
        Assert.Empty(Complete(world, task).OfType<UnitTransferredEvent>());
        Assert.Equal(2, world.Units[100].OwnerId);
        Assert.Equal(before.Soldiers, world.Units[100].Soldiers);
        Assert.Equal(20, world.Units[100].CommanderId);
        Assert.Null(world.Officers[22].UnitId);
        Assert.Equal(1, world.Officers[22].OwnerId);
    }

    [Fact]
    public void UnitMovementKeepsTargetButLeavingUnitInvalidatesIt()
    {
        var (world, task) = Submit(); var rule = Rules.FindTask(task.Kind)!;
        Assert.True(rule.HasValidPayload(task));
        world.Replace(world.Units[100] with { Position = new(502, 2) });
        Assert.True(rule.Validate(world, task));
        world.Replace(world.Officers[20] with { UnitId = null, SettlementId = 2 });
        Assert.False(rule.Validate(world, task));
    }

    [Fact]
    public void LiveCommanderChangePreventsTakingArmyThroughFormerCommander()
    {
        var (world, task) = Submit();
        world.Replace(world.Officers[20] with { BaseAbilities = new(50, 50, 100, 40) });
        world.Replace(world.Officers[22] with { BaseAbilities = new(50, 50, 100, 90) });
        world.MaintainLeadership([]);
        Assert.Equal(22, world.Units[100].CommanderId);
        Assert.Empty(Complete(world, task).OfType<UnitTransferredEvent>());
        Assert.Equal(2, world.Units[100].OwnerId);
    }

    [Fact]
    public void RulerAndCommanderSharingRulerUnitAreProtected()
    {
        var (world, _) = Submit();
        world.Replace(world.Officers[2] with { SettlementId = null, UnitId = 100 });
        Assert.False(PersonnelMissions.Recruitable(world, 1, world.Officers[2]));
        Assert.False(PersonnelDefection.CanTransfer(world, world.Officers[20]));
    }

    [Fact]
    public void CaptiveRecoversAndOrdersAgainstNewFriendlyArmyAreCleared()
    {
        var (world, task) = Submit();
        world.Replace(Person(30, 1, null) with { Status = OfficerStatus.Captive });
        world.AddUnit(new UnitState(101, 1, new(202, 2), 500) { FormationId = 1, ServicePlaceId = 1,
            Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 100 } });
        world.SetSimulation(world.Simulation with { NextUnitId = 102 });
        var events = Complete(world, task);
        Assert.Equal(OfficerStatus.Serving, world.Officers[30].Status);
        Assert.Null(world.Officers[30].UnitId);
        Assert.Null(world.Units[101].Order);
        Assert.Single(events.OfType<PrisonerDisplacedEvent>());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PublicResolutionUsesCurrentPositionOrCancelsIfOfficerLeft(bool leaves)
    {
        var state = World(); var engine = new SimulationEngine(state, Rules);
        Assert.True(engine.Execute(new("player"), new RecruitOfficerCommand(Guid.NewGuid(), 1, 0, 11, 20)).Submission.Accepted);
        var mutation = state.BeginMutation(Rules.Abilities);
        mutation.Replace(mutation.Units[100] with { Position = new(502, 2) });
        if (leaves) mutation.Replace(mutation.Officers[20] with { UnitId = null, SettlementId = 2 });
        mutation.MaintainLeadership([]);
        state.Commit(mutation);
        var events = new List<DomainEvent>();
        for (int i = 0; i < 2; i++)
        {
            engine.EndControl(new("player")); events.AddRange(engine.AdvanceXun().Events);
        }
        Assert.Equal(leaves ? 2 : 1, engine.Snapshot.Units.Single(u => u.Id == 100).OwnerId);
        if (leaves)
        {
            Assert.Empty(events.OfType<RecruitmentResolvedEvent>());
            Assert.Contains(events.OfType<TaskCancelledEvent>(), e => e.Reason == "invalid-target-or-executor");
        }
        else
        {
            Assert.True(Assert.Single(events.OfType<RecruitmentResolvedEvent>()).Success);
            var returns = events.OfType<TaskStartedEvent>().Where(e => e.Task.Kind == PersonnelReturns.Kind).ToArray();
            var envoy = Assert.Single(returns, e => e.Task.OfficerId == 11).Task;
            Assert.Equal(new WorldPosition(502, 2), envoy.Journey!.Route.Origin);
            Assert.Equal(17, envoy.Journey.ReturnLeg!.CountdownDays);
            Assert.Contains(returns, e => e.Task.OfficerId == 21 && e.Task.Journey!.Route.Origin == new WorldPosition(502, 2));
        }
        _ = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), Rules);
    }

    [Fact]
    public void FieldCorpsWithoutRemainingMembersDissolvesAfterFollowersLeave()
    {
        var (world, task) = Submit();
        world.AddCorps(new(3, 2, 20, new(123, 456)));
        world.Replace(world.Units[100] with { CorpsId = 3 });
        world.Replace(world.Officers[20] with { Status = OfficerStatus.Governor });
        var events = Complete(world, task);
        Assert.DoesNotContain(3, world.Corps.Keys);
        Assert.Equal(OfficerStatus.Serving, world.Officers[20].Status);
        Assert.Single(events.OfType<CorpsDissolvedEvent>());
        Assert.DoesNotContain(world.Simulation.Tasks, t => t.Journey?.ReturnLeg?.CorpsId == 3);
    }

}
