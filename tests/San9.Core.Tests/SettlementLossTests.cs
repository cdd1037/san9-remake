using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SettlementLossTests
{
    private static SettlementState Place(int id, int owner, int corps, uint soldiers = 100) =>
        new(id, owner, soldiers, 100, 100, 70, 500, 100, 1000, 100, 1000)
        { Kind = SettlementKind.City, CorpsId = corps, ConsumesFood = false };
    private static OfficerState Person(int id, int owner, int? home, OfficerStatus status = OfficerStatus.Serving) =>
        new(id, owner, home, new(70, 70, 70, 70), false) { Status = status };
    private static WorldState State(bool captive = false) => WorldState.Create(new(190, 1, 1), 17,
        [new(1, 100) { LeaderId = 101, BaseTrust = 100 }, new(2, 900) { LeaderId = 201, BaseTrust = 900 },
         new(3, 100) { LeaderId = 301 }],
        [Place(10, 1, 100, 0), Place(11, 1, 100), Place(20, 2, 200), Place(30, 3, 300)],
        [Person(101, 1, 11, OfficerStatus.Ruler), Person(102, 1, 10), Person(103, 1, 10),
         Person(201, 2, null, OfficerStatus.Ruler) with { UnitId = 900 }, Person(301, 3, 30, OfficerStatus.Ruler),
         .. captive ? new[] { Person(302, 3, 10, OfficerStatus.Captive) } : []],
        units: [new(900, 2, new(12, 2), 1000) { CorpsId = 200, ServicePlaceId = 20, FormationId = 1,
            Morale = 70, CommanderId = 201, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(201, BattleRank.Front),
            Order = new(MilitaryOrderKind.AttackSettlement, null, 10) }],
        corps: [new(100, 1, 101, new(1000, 1000)) { IsRoot = true }, new(200, 2, 201, new(1000, 1000)) { IsRoot = true },
            new(300, 3, 301, new(1000, 1000)) { IsRoot = true }]);
    private static SimulationRules Rules() => new()
    {
        Personnel = new([new(10, new(12, 2)), new(11, new(2, 2)), new(20, new(22, 2)), new(30, new(32, 2))],
            new Dictionary<int, int> { [10] = 11 }),
        Military = new(new(10, 1, Enumerable.Repeat(0, 10).ToImmutableArray(), [new(0, 1, null)],
            [new(1, 3, 0, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(10, new(12, 2), new(1, 1)) { CanDemolish = true }, new(11, new(2, 2), new(1, 1)),
             new(20, new(22, 2), new(1, 1)), new(30, new(32, 2), new(1, 1))]))
    };

    [Fact]
    public void RootWithoutDirectCityShouldMergeASurvivingChildIntoIt()
    {
        var world = State().BeginMutation();
        world.AddCorps(new(110, 1, 102, new(300, 400)));
        world.Replace(world.Settlements[10] with { OwnerId = 2, CorpsId = 200, Soldiers = 100 });
        world.Replace(world.Settlements[11] with { CorpsId = 110 });
        world.Replace(world.Officers[102] with { SettlementId = 11, Status = OfficerStatus.Governor });
        world.Replace(world.Officers[101] with { SettlementId = null, UnitId = 901 });
        world.Replace(world.Officers[103] with { SettlementId = 11 });
        world.AddUnit(new(901, 1, new(7, 2), 100) { CorpsId = 100, ServicePlaceId = 10 });
        GovernanceLifecycle.Reconcile(world, new HashSet<int> { 1 }, Rules(), [], false);
        Assert.Equal(100, Assert.Single(world.Corps.Values, c => c.OwnerId == 1).Id);
        Assert.Equal(11, world.Units[901].ServicePlaceId);
        Assert.Equal(100, world.Settlements[11].CorpsId);
        Assert.Equal(1300u, world.Corps[100].Treasury.Money);
        Assert.Equal(1400u, world.Corps[100].Treasury.Food);
        Assert.Equal(OfficerStatus.Serving, world.Officers[102].Status);
    }

    [Fact]
    public void BattleSuccessionMergesTheSuccessorsCorpsEvenWhenTheRootStillHasACity()
    {
        var world = State().BeginMutation();
        world.AddCorps(new(110, 1, 102, new(300, 400)));
        world.Replace(world.Settlements[10] with { CorpsId = 110, Soldiers = 100 });
        world.Replace(world.Officers[102] with { Status = OfficerStatus.Governor, SuccessionPriority = 100 });
        world.Replace(world.Officers[103] with { SettlementId = 11 });
        world.Replace(world.Officers[101] with { Status = OfficerStatus.Dead, OwnerId = null, SettlementId = null });
        List<DomainEvent> events = [];

        GovernanceLifecycle.Reconcile(world, new HashSet<int> { 1 }, Rules(), events, false);

        Assert.Equal(102, world.Forces[1].LeaderId);
        Assert.Equal(102, world.Corps[100].LeaderId);
        Assert.False(world.Corps.ContainsKey(110));
        Assert.Equal(100, world.Settlements[10].CorpsId);
        Assert.Equal(1300u, world.Corps[100].Treasury.Money);
        Assert.Equal(world.Clock.Year, world.Officers[102].StatusStartYear);
        Assert.Single(events.OfType<ForceLeaderChangedEvent>());
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void CityDefectionShouldRunSharedOwnershipPublicOrderFormula()
    {
        var world = State().BeginMutation();
        var rules = Rules();
        List<DomainEvent> events = [];
        SettlementAffiliation.Transfer(world, world.Settlements[10], 2, world.Officers[102],
            [world.Officers[102], world.Officers[103]], SettlementDefectionKind.Isolated, rules, events);
        Assert.Equal(800, world.Settlements[10].PublicOrder);
        Assert.Equal(800, Assert.Single(events.OfType<CityCapturePublicOrderEvent>()).Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedPlaceShouldReleaseItsExistingThirdPartyCaptiveToHisOwnForce(bool facility)
    {
        var world = State(captive: true).BeginMutation();
        if (facility) world.Replace(world.Settlements[10] with { Kind = SettlementKind.Facility });
        WorldInvariants.Validate(world);
        var rules = Rules();
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(900, false), rules, []);
        GovernanceLifecycle.Reconcile(world, new HashSet<int> { 1, 2 }, rules, [], false);
        Assert.Equal(OfficerStatus.Serving, world.Officers[302].Status);
        Assert.Equal(30, world.Officers[302].SettlementId);
        Assert.Equal(3, world.Officers[302].OwnerId);
        Assert.Single(world.TasksFor(302));
        WorldInvariants.Validate(world);
    }

    [Theory]
    [InlineData(false, "capture", false)]
    [InlineData(true, "capture", false)]
    [InlineData(false, "defection", false)]
    [InlineData(true, "defection", false)]
    [InlineData(true, "demolition", false)]
    [InlineData(false, "capture", true)]
    [InlineData(true, "capture", true)]
    [InlineData(false, "defection", true)]
    [InlineData(true, "defection", true)]
    [InlineData(true, "demolition", true)]
    public void LosingMissionOriginAloneShouldNotCancelTheStillValidOutboundTarget(bool facility, string cause, bool search)
    {
        var state = State();
        var world = state.BeginMutation();
        if (facility) world.Replace(world.Settlements[10] with { Kind = SettlementKind.Facility });
        Assert.Equal(CommandRejection.None, search
            ? PersonnelMissions.StartSearch(world, new("system"), new(Guid.NewGuid(), 1, 0, [103], 11), Rules().Personnel, null, [])
            : PersonnelMissions.StartMove(world, new("system"), new(Guid.NewGuid(), 1, 0, [103], 11), Rules().Personnel, null, []));
        world.SetSimulation(world.Simulation with { NextSequence = 1 });
        WorldInvariants.Validate(world);
        var before = Assert.Single(world.Simulation.Tasks);
        if (cause == "capture")
            BattleAftermath.ResolveSettlementDefeat(world, 10, new(900, false), Rules(), []);
        else if (cause == "defection")
            SettlementAffiliation.Transfer(world, world.Settlements[10], 2, world.Officers[102],
                [world.Officers[102]], SettlementDefectionKind.Isolated, Rules(), []);
        else
        {
            world.Replace(world.Officers[201] with { UnitId = null, SettlementId = 20 });
            world.RemoveUnit(900);
            Assert.Equal(CommandRejection.None, FacilityDemolition.Apply(world, new(Guid.NewGuid(), 1, 1, 10), Rules(), []));
        }
        Assert.Equal(before, Assert.Single(world.Simulation.Tasks, t => t.OfficerId == 103));
        Assert.Equal(11, world.Officers[103].SettlementId);
        Assert.True(ResolutionPipeline.TaskReferencesValid(world, before));
        if (search) Assert.Equal(new ResourceAccount(ResourceOwnerKind.Corps, 100), SearchEventResources.Account(world, before));
        WorldInvariants.Validate(world);
        state.Commit(world);
        var engine = new SimulationEngine(state, Rules());
        var completed = engine.AdvanceXun();
        Assert.DoesNotContain(completed.Events.OfType<TaskCancelledEvent>(), e => e.TaskId == before.Id);
        Assert.Contains(completed.Events.OfType<TaskCompletedEvent>(), e => e.TaskId == before.Id);
        if (search) Assert.Contains(completed.Events.OfType<SearchResolvedEvent>(), e => e.OfficerId == 103);
        else Assert.Contains(completed.Events.OfType<OfficerMovedEvent>(), e => e.OfficerId == 103);
        Assert.DoesNotContain(engine.Snapshot.Simulation.Tasks, t => t.OfficerId == 103);
        Assert.Equal(11, engine.Snapshot.Officers.Single(p => p.Id == 103).SettlementId);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecapturedFriendlyPrisonerShouldResumeInTheCapturedPlaceWhenItSurvives(bool facility)
    {
        var world = State(captive: true).BeginMutation();
        world.Replace(world.Officers[302] with { OwnerId = 2 });
        if (facility) world.Replace(world.Settlements[10] with { Kind = SettlementKind.Facility });
        WorldInvariants.Validate(world);
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(900, false), Rules(), []);
        Assert.Equal(OfficerStatus.Serving, world.Officers[302].Status);
        Assert.Equal(10, world.Officers[302].SettlementId);
        Assert.Empty(world.TasksFor(302));
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void LosingABarbarianCityShouldRemoveItsServingDefendersEvenWhenAnotherCityRemains()
    {
        var world = State().BeginMutation();
        world.Replace(world.Forces[1] with { IsBarbarian = true });
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(900, false), Rules(), []);
        Assert.Equal(OfficerStatus.Dead, world.Officers[102].Status);
        Assert.False(world.Forces[1].IsEliminated);
    }

    [Fact]
    public void BarbarianFacilityDefendersStillRetreatRatherThanUseTheCityDeathRule()
    {
        var world = State().BeginMutation();
        world.Replace(world.Forces[1] with { IsBarbarian = true });
        world.Replace(world.Settlements[10] with { Kind = SettlementKind.Facility });
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(900, false), Rules(), []);
        Assert.Equal(OfficerStatus.Serving, world.Officers[102].Status);
        Assert.Equal(11, world.Officers[102].SettlementId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARecoveredPrisonerDoesNotStayInAZeroDurabilityBuilding(bool facility)
    {
        var world = State(captive: true).BeginMutation();
        world.Replace(world.Officers[302] with { OwnerId = 2 });
        world.Replace(world.Settlements[10] with { Durability = 0,
            Kind = facility ? SettlementKind.Facility : SettlementKind.City });
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(900, false), Rules(), []);
        Assert.Equal(OfficerStatus.Serving, world.Officers[302].Status);
        Assert.Equal(20, world.Officers[302].SettlementId);
        Assert.Single(world.TasksFor(302));
        WorldInvariants.Validate(world);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedRulerStaysOnlyWhenRecoveredByHisRootCorps(bool child)
    {
        var world = State(captive: true).BeginMutation();
        world.Replace(world.Officers[201] with { UnitId = null, SettlementId = 10, Status = OfficerStatus.Captive });
        world.Replace(world.Officers[302] with { OwnerId = 2, Status = OfficerStatus.Serving, UnitId = 900, SettlementId = null });
        world.Replace(world.Units[900] with { CommanderId = 302, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(302, BattleRank.Front) });
        if (child)
        {
            world.AddCorps(new(210, 2, 302, new(100, 100)));
            world.Replace(world.Units[900] with { CorpsId = 210 });
            world.Replace(world.Officers[302] with { Status = OfficerStatus.Governor });
        }
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(900, false), Rules(), []);
        Assert.Equal(OfficerStatus.Ruler, world.Officers[201].Status);
        Assert.Equal(child ? 20 : 10, world.Officers[201].SettlementId);
        Assert.Equal(child ? 1 : 0, world.TasksFor(201).Count());
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void AThirdPartyCaptiveWithoutAnyHomeIsReleasedInsteadOfInherited()
    {
        var world = State(captive: true).BeginMutation();
        world.Replace(world.Settlements[30] with { OwnerId = 2, CorpsId = 200 });
        world.Replace(world.Officers[301] with { SettlementId = null, RegionId = 30 });
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(900, false), Rules(), []);
        Assert.Equal(OfficerStatus.Free, world.Officers[302].Status);
        Assert.Null(world.Officers[302].OwnerId);
        Assert.Null(world.Officers[302].UnitId);
        Assert.Empty(world.TasksFor(302));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LosingTheActualJourneyDestinationStillStartsAReturn(bool returning)
    {
        var world = State().BeginMutation();
        if (returning)
            PersonnelReturns.Schedule(world, new("system", 1, 0), 103, 10, 11, 2, false, [],
                new(new(12, 2), new(2, 2), false));
        else
            Assert.Equal(CommandRejection.None, PersonnelMissions.StartMove(world, new("system"),
                new(Guid.NewGuid(), 1, 0, [103], 11), Rules().Personnel, null, []));
        world.SetSimulation(world.Simulation with { NextSequence = 1 });
        world.Replace(world.Settlements[10] with { Soldiers = 100 });
        world.Replace(world.Settlements[11] with { Soldiers = 0 });
        world.Replace(world.Officers[101] with { SettlementId = 10 });
        world.Replace(world.Units[900] with { Order = new(MilitaryOrderKind.AttackSettlement, null, 11) });
        List<DomainEvent> events = [];
        BattleAftermath.ResolveSettlementDefeat(world, 11, new(900, false), Rules(), events);
        Assert.Equal(PersonnelReturns.Kind, Assert.Single(world.TasksFor(103)).Kind);
        Assert.Equal(returning ? 0 : 1, events.OfType<PersonnelJourneyInterruptedEvent>().Count());
        Assert.Equal(10, world.Officers[103].SettlementId);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void RecruitmentAfterOriginLossUsesTheCurrentFriendlyHome()
    {
        var state = State(captive: true);
        var world = state.BeginMutation();
        world.Replace((world.Officers[302] with { OwnerId = null, Status = OfficerStatus.Free, SettlementId = 30 }).WithLoyalty(1, 100));
        Assert.Equal(CommandRejection.None, PersonnelMissions.StartRecruit(world, new("system"),
            new(Guid.NewGuid(), 1, 0, 103, 302), Rules(), []));
        var task = Assert.Single(world.Simulation.Tasks);
        world.SetSimulation(world.Simulation with { NextSequence = 1, Tasks = [task with
            { Journey = task.Journey! with { SuccessRoll = 0 } }] });
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(900, false), Rules(), []);
        WorldInvariants.Validate(world);
        state.Commit(world);
        var engine = new SimulationEngine(state, Rules());
        var result = engine.AdvanceXun();
        Assert.Contains(result.Events.OfType<TaskCompletedEvent>(), e => e.TaskId == task.Id);
        Assert.True(Assert.Single(result.Events.OfType<RecruitmentResolvedEvent>()).Success);
        var recruited = engine.Snapshot.Officers.Single(p => p.Id == 302);
        Assert.Equal(1, recruited.OwnerId);
        Assert.Equal(11, recruited.SettlementId);
    }

    [Theory]
    [InlineData(false, "troops")]
    [InlineData(false, "morale")]
    [InlineData(false, "durability")]
    [InlineData(true, "troops")]
    [InlineData(true, "morale")]
    [InlineData(true, "durability")]
    public void CityAndPortUseTheSharedThreeStateDefeatGate(bool facility, string cause)
    {
        var world = State().BeginMutation();
        world.Replace(world.Settlements[10] with
        {
            Kind = facility ? SettlementKind.Facility : SettlementKind.City,
            Soldiers = cause == "troops" ? 0u : 100u,
            Morale = cause == "morale" ? 0 : 70,
            Durability = cause == "durability" ? 0 : 100
        });
        List<DomainEvent> events = [];
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(900, false), Rules(), events);
        Assert.Equal(2, world.Settlements[10].OwnerId);
        Assert.True(world.Settlements[10].Durability >= 25);
        Assert.Single(events.OfType<SettlementCapturedEvent>());
        Assert.False(world.Forces[1].IsEliminated);
    }

    [Fact]
    public void AChildCorpsWithoutACityStillMergesBackIntoTheRoot()
    {
        var world = State().BeginMutation();
        world.AddCorps(new(110, 1, 102, new(300, 400)));
        world.Replace(world.Settlements[10] with { Kind = SettlementKind.Facility, CorpsId = 110, Soldiers = 100 });
        world.Replace(world.Officers[102] with { Status = OfficerStatus.Governor });
        world.Replace(world.Officers[103] with { SettlementId = 11 });
        GovernanceLifecycle.Reconcile(world, new HashSet<int> { 1 }, Rules(), [], false);
        Assert.False(world.Corps.ContainsKey(110));
        Assert.Equal(1300u, world.Corps[100].Treasury.Money);
        Assert.Equal(100, world.Settlements[10].CorpsId);
        Assert.Equal(OfficerStatus.Serving, world.Officers[102].Status);
    }

    [Fact]
    public void LastCityLossStillClearsTheRemainingPortAndArmyInTheSameTransaction()
    {
        var world = State().BeginMutation();
        world.Replace(world.Settlements[11] with { Kind = SettlementKind.Facility });
        world.AddUnit(new(901, 1, new(7, 2), 100) { CorpsId = 100, ServicePlaceId = 11, FormationId = 1, Morale = 70 });
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(900, false), Rules(), []);
        Assert.True(world.Forces[1].IsEliminated);
        Assert.Null(world.Settlements[11].OwnerId);
        Assert.Equal(0u, world.Settlements[11].Soldiers);
        Assert.DoesNotContain(world.Units.Values, u => u.OwnerId == 1);
        Assert.DoesNotContain(world.Corps.Values, c => c.OwnerId == 1);
    }

}
