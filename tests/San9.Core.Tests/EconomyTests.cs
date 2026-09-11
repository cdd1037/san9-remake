using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class EconomyTests
{
    private static WorldState World(uint money = 1000, uint food = 1000, GameClock? clock = null, ulong seed = 190) => WorldState.Create(clock ?? new(190, 1, 1), seed,
        [new(1, 1000, 1000, 100) { BaseTrust = 100, LeaderId = 0 }],
        [new(1, 1, 990, 900, 1000, 50, 900, 900, 1000, 900, 1000) { CorpsId = 1, Population = 60000, DraftPopulation = 1000, SoldierCapacity = 1000 },
         new(2, 1, 990, 900, 1000, 50, 900, 900, 1000, 900, 1000) { CorpsId = 2, Population = 60000, DraftPopulation = 1000, SoldierCapacity = 1000 }],
        new[] { new OfficerState(0, 1, 1, new(100,100,100,100), false) { Status = OfficerStatus.Ruler } }
            .Concat(Enumerable.Range(1,5).Select(id => new OfficerState(id,1,2,new(100,100,100,100),false) { Affinity = id * 10, Loyalty = 90, MasteredCategories = [0,5] })),
        corps: [new(1,1,0,null),new(2,1,1,new(money,food))], controls: [new("player",1,ControllerKind.Player)]);

    private static CommandExecutionResult Run(SimulationEngine engine, DomesticActivity activity, params int[] ids) =>
        engine.Execute(new("player"), new DevelopCommand(new Guid((int)engine.Snapshot.Simulation.NextSequence + 1,0,0,new byte[8]),
            1,engine.Snapshot.Simulation.NextSequence,new(1,2,ids.ToImmutableArray(),activity)));

    [Theory]
    [InlineData(DomesticActivity.Patrol,4)]
    [InlineData(DomesticActivity.Commerce,7)]
    [InlineData(DomesticActivity.Cultivation,7)]
    [InlineData(DomesticActivity.Repair,7)]
    public void FourProjectsUseTheirAccountAndRecover(DomesticActivity activity, int expected)
    {
        var engine = new SimulationEngine(World());
        var before = engine.Snapshot;
        var result = Run(engine,activity,1);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(expected,result.Development!.Value.Increment);
        Assert.Equal(1000u,engine.Snapshot.Forces.Single().Money);
        Assert.Equal(945u,engine.Snapshot.Corps.Single(c=>c.Id==2).Treasury!.Money);
        var debit = Assert.Single(result.Events.OfType<ResourceChangedEvent>());
        Assert.Equal(new ResourceAccount(ResourceOwnerKind.Corps,2),debit.Account);
        Assert.Equal(-50,debit.Actual);
        if (activity == DomesticActivity.Patrol) Assert.Equal(114, engine.Snapshot.Forces.Single().Trust);
        Assert.False(Run(engine,activity,2).Submission.Accepted);
        Assert.False(before.Officers.Single(p=>p.Id==1).Acted);
        if(activity==DomesticActivity.Repair)
        {
            Assert.Equal(3,engine.Snapshot.Officers.Single(p=>p.Id==1).TacticProficiencies[5]);
            Assert.Equal(1,engine.Snapshot.Officers.Single(p=>p.Id==1).TacticProficiencies[6]);
        }
        SimulationEngineTests.Advance(engine);
        Assert.True(Run(engine,activity,1).Submission.Accepted);
    }

    [Fact]
    public void DuplicateParticipantsAndCompetingOrdersHaveNoSideEffects()
    {
        var engine = new SimulationEngine(World());
        var before = engine.Snapshot;
        Assert.Equal(DevelopmentRejection.InvalidParticipants,Run(engine,DomesticActivity.Commerce,1,1).Development!.Value.Rejection);
        Assert.Same(before,engine.Snapshot);
        Assert.True(Run(engine,DomesticActivity.Commerce,1,2,3,4,5).Submission.Accepted);
        var after = engine.Snapshot;
        Assert.All(after.Officers.Where(p=>p.Id>0),p=>Assert.True(p.Acted));
        Assert.False(Run(engine,DomesticActivity.Cultivation,5).Submission.Accepted);
        Assert.Same(after,engine.Snapshot);
        Assert.Equal(745u,after.Corps.Single(c=>c.Id==2).Treasury!.Money);
        Assert.Single(after.Simulation.RandomStreams);
    }

    [Fact]
    public void RecruitmentClampsBeforeChargingAndConservesMilitaryPopulation()
    {
        var engine = new SimulationEngine(World());
        var before = engine.Snapshot.Settlements.Single(s=>s.Id==2);
        var result = Run(engine,DomesticActivity.Recruit,1,2,3,4,5);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(new DevelopmentOutcome(DevelopmentRejection.None,10,1),result.Development);
        var after = engine.Snapshot.Settlements.Single(s=>s.Id==2);
        Assert.Equal(1000u,after.Soldiers);
        Assert.Equal(before.Soldiers+before.DraftPopulation,after.Soldiers+after.DraftPopulation);
        Assert.Equal(994u,engine.Snapshot.Corps.Single(c=>c.Id==2).Treasury!.Money);
        Assert.Equal(0,result.Events.OfType<ResourceChangedEvent>().Where(e=>e.Resource is ResourceKind.DraftPopulation or ResourceKind.Soldiers).Sum(e=>e.Actual));
        Assert.All(engine.Snapshot.Officers,p=>Assert.All(p.TacticProficiencies,v=>Assert.Equal(0,v)));
    }

    [Fact]
    public void TrainingIsFreeAndOnlyChangesMoraleAndProficiency()
    {
        var engine = new SimulationEngine(World(0));
        var before = engine.Snapshot.Settlements.Single(s=>s.Id==2);
        var result = Run(engine,DomesticActivity.Train,1);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(new DevelopmentOutcome(DevelopmentRejection.None,10,0),result.Development);
        var after = engine.Snapshot.Settlements.Single(s=>s.Id==2);
        Assert.Equal(60,after.Morale);
        Assert.Equal(before.Soldiers,after.Soldiers);
        Assert.Empty(result.Events.OfType<ResourceChangedEvent>());
        Assert.Equal(new[]{3,1,1,1,1,0,0,0,0},engine.Snapshot.Officers.Single(p=>p.Id==1).TacticProficiencies);
    }

    [Fact]
    public void InsufficientMoneyDoesNotConsumeCombinationRandom()
    {
        var engine = new SimulationEngine(World(54));
        var before = engine.Snapshot;
        Assert.Equal(DevelopmentRejection.InsufficientFunds,Run(engine,DomesticActivity.Commerce,1).Development!.Value.Rejection);
        Assert.Same(before,engine.Snapshot);
        Assert.Empty(before.Simulation.RandomStreams);
    }

    [Fact]
    public void FoodShortfallAndSalaryAreExplicitAndDoNotBorrowFromRoot()
    {
        var engine = new SimulationEngine(World(2,1));
        Assert.Equal(3,engine.InitialEvents.OfType<ResourceChangedEvent>().Count(e=>e.Reason=="salary" && e.Shortfall==1));
        Assert.Equal(1000u,engine.Snapshot.Forces.Single().Money);
        Assert.Equal(0u,engine.Snapshot.Corps.Single(c=>c.Id==2).Treasury!.Money);
        var result = SimulationEngineTests.Advance(engine);
        var food = result.Events.OfType<ResourceChangedEvent>().Where(e=>e.Resource==ResourceKind.Food && e.Account.Id==2).ToArray();
        Assert.Equal(10,food.Length);
        Assert.Equal(-20,food.Sum(e=>e.Requested));
        Assert.Equal(-1,food.Sum(e=>e.Actual));
        Assert.Equal(19,food.Sum(e=>e.Shortfall));
        Assert.True(result.Events.ToList().IndexOf(food[0]) < result.Events.ToList().FindIndex(e=>e is StageCompletedEvent { Stage: ResolutionStage.DailyCommands }));
    }

    [Theory]
    [InlineData(3,1,4,1,true,false)]
    [InlineData(3,11,4,1,false,false)]
    [InlineData(6,1,7,1,true,true)]
    [InlineData(12,1,1,1,true,false)]
    public void PopulationAndRevenueUseQuarterAndElapsedGates(int month,int day,int nextMonth,int nextDay,bool grows,bool harvest)
    {
        var engine = new SimulationEngine(World(clock:new(190,month,day)));
        Assert.DoesNotContain(engine.InitialEvents.OfType<ResourceChangedEvent>(),e=>e.Reason.StartsWith("quarterly"));
        AdvanceXunResult result;
        do { result=SimulationEngineTests.Advance(engine); } while(engine.Snapshot.Clock.Month!=nextMonth || engine.Snapshot.Clock.Day!=nextDay);
        var city=engine.Snapshot.Settlements[0];
        Assert.Equal(grows?1120u:1000u,city.DraftPopulation);
        Assert.Equal(60000u,city.Population);
        Assert.Equal(grows?2:0,result.Events.OfType<ResourceChangedEvent>().Count(e=>e.Reason=="quarterly-revenue"));
        Assert.Equal(harvest?2:0,result.Events.OfType<ResourceChangedEvent>().Count(e=>e.Reason=="annual-harvest"));
        Assert.Equal(engine.Snapshot.StateHash,new SimulationEngine(WorldState.Restore(engine.Snapshot)).Snapshot.StateHash);
    }

    [Fact]
    public void RevenueAndHarvestPreserveIntegerTruncation()
    {
        var city=new SettlementState(1,1,0,1,1,0,500,300,500,200,500){Population=90000,DraftPopulation=10000};
        Assert.Equal(1450,EconomyMath.Revenue(city));
        Assert.Equal(11704,EconomyMath.Harvest(city));
        Assert.Equal(40,EconomyMath.ExecutedAbility(60));
        Assert.Equal(60,EconomyMath.ExecutedAbility(80));
        Assert.Equal(100,EconomyMath.ExecutedAbility(100));
        Assert.Equal(2,EconomyMath.AffinityDistance(149,1));
    }

    [Fact]
    public void TreasurySaturationAndMissingCorpsAreNotSilentlyAccepted()
    {
        var world=World(999999);
        var mutation=world.BeginMutation();
        List<DomainEvent> events=[];
        ResourceAccounts.Change(mutation,new(ResourceOwnerKind.Corps,2),ResourceKind.Money,10,"test-income",2,events);
        var change=Assert.Single(events.OfType<ResourceChangedEvent>());
        Assert.Equal(1,change.Actual);Assert.Equal(9,change.SaturationLoss);
        mutation.Replace(mutation.Settlements[2] with { CorpsId=999 });
        Assert.Throws<ArgumentException>(()=>world.Commit(mutation));
    }

    [Fact]
    public void TwentyNineDaysDoNotGrowAndThirtyDaysDo()
    {
        var mutation=World(clock:new(190,3,1)).BeginMutation();
        List<DomainEvent> events=[];
        mutation.SetClock(new(190,3,30));
        EconomicCycle.Apply(ResolutionStage.QuarterUpdate,mutation,EconomyRulebook.Empty,events);
        Assert.Empty(events);
        mutation.SetClock(new(190,4,1));
        EconomicCycle.Apply(ResolutionStage.QuarterUpdate,mutation,EconomyRulebook.Empty,events);
        Assert.Equal(1120u,mutation.Settlements[2].DraftPopulation);
    }

    [Fact]
    public void NamedOfficeSalaryHasStablePriorityAndCannotPartiallyPay()
    {
        var world=World(20);
        var mutation=world.BeginMutation();
        mutation.Replace(mutation.Officers[1] with { OfficeId=7 });
        mutation.Replace(mutation.Officers[2] with { OfficeId=8 });
        world.Commit(mutation);
        var engine=new SimulationEngine(world,new EconomyRulebook([new(7,15),new(8,10)],[]));
        var salaries=engine.InitialEvents.OfType<ResourceChangedEvent>().Where(e=>e.Reason=="salary").ToArray();
        Assert.Equal(new int?[]{1,2,3,4,5},salaries.Select(e=>e.SourceId));
        Assert.Equal(new long[]{-15,0,-1,-1,-1},salaries.Select(e=>e.Actual));
        Assert.Equal(10,salaries[1].Shortfall);
        Assert.Equal(89,engine.Snapshot.Officers.Single(p=>p.Id==2).Loyalty);
        Assert.Equal(2u,engine.Snapshot.Corps.Single(c=>c.Id==2).Treasury!.Money);
        Assert.Empty(new SimulationEngine(WorldState.Restore(engine.Snapshot),new EconomyRulebook([new(7,15),new(8,10)],[])).InitialEvents);
    }

    [Fact]
    public void PopulationAndProficiencySaturationRemainObservable()
    {
        var world=World();
        var mutation=world.BeginMutation();
        mutation.Replace(mutation.Settlements[1] with { Population=1000000 });
        mutation.Replace(mutation.Settlements[2] with { Population=1000000,DraftPopulation=499999,PublicOrder=1000 });
        mutation.SetClock(new(190,4,1));
        List<DomainEvent> events=[];
        EconomicCycle.Apply(ResolutionStage.QuarterUpdate,mutation,EconomyRulebook.Empty,events);
        Assert.Equal(1000000u,mutation.Settlements[2].Population);
        Assert.Equal(500000u,mutation.Settlements[2].DraftPopulation);
        Assert.Contains(events.OfType<ResourceChangedEvent>(),e=>e.Resource==ResourceKind.Population && e.SaturationLoss>0);
        Assert.Contains(events.OfType<ResourceChangedEvent>(),e=>e.Resource==ResourceKind.DraftPopulation && e.SaturationLoss>0);
        mutation.Replace(mutation.Officers[1] with { TacticProficiencies=[998,0,0,0,0,0,0,0,0] });
        world.Commit(mutation);
        var engine=new SimulationEngine(world);
        Assert.True(Run(engine,DomesticActivity.Train,1).Submission.Accepted);
        Assert.Equal(1000,engine.Snapshot.Officers.Single(p=>p.Id==1).TacticProficiencies[0]);
    }

    [Theory]
    [InlineData(false,false,22)]
    [InlineData(true,false,11)]
    [InlineData(false,true,11)]
    public void SpecialCombinationChecksStatusAndLeaderAffinityAfterItsRoll(bool requiresRuler,bool distant,int expected)
    {
        // Independently evaluated stream for seed7 starts with rolls8,74: pair succeeds,
        // but an ineligible pair must fall through to the failed affinity roll.
        var world=World(seed:7);
        var mutation=world.BeginMutation();
        foreach(int id in new[]{1,2})mutation.Replace(mutation.Officers[id] with { Affinity=distant?20:0 });
        world.Commit(mutation);
        var rules=new EconomyRulebook([],[new(DomesticActivity.Commerce,1,2,requiresRuler,false)]);
        var engine=new SimulationEngine(world,rules);
        var result=Run(engine,DomesticActivity.Commerce,2,1);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(expected,result.Development!.Value.Increment);
        var restored=new SimulationEngine(WorldState.Restore(engine.Snapshot),rules);
        SimulationEngineTests.Advance(engine);SimulationEngineTests.Advance(restored);
        Assert.Equal(Run(engine,DomesticActivity.Commerce,1,2).Snapshot.StateHash,Run(restored,DomesticActivity.Commerce,1,2).Snapshot.StateHash);
    }
}
