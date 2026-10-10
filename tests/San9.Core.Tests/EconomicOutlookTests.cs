using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;
public sealed class EconomicOutlookTests
{
    [Fact]
    public void DormantOfficerDoesNotConsumeSalaryAndBarbarianSupplyReplacesInventory()
    {
        var clock = new GameClock(190, 6, 21);
        var snapshot = new WorldSnapshot(clock, new(new(190, 1, 1), 1))
        {
            Forces = [new(5, 100) { IsBarbarian = true }],
            Corps = [new(10, 5, null, new(90000, 300000)) { IsRoot = true }, new(11, 5, null, new(40000, 200000))],
            Settlements = [new(15, 5, 1000, 100, 100, 50, 800, 100, 100, 100, 100) { CorpsId = 10, Wounded = 1000 }],
            Officers = [new(1, 5, 15, new(50, 50, 50, 50), false) { IsDormant = true }]
        };
        string before = snapshot.StateHash;
        var supplied = EconomicOutlook.Read(snapshot, new Dictionary<int, int>(), 5, 10);
        Assert.Equal(0, supplied.Money.Expenditure);
        Assert.Equal(30000, supplied.Money.Forecast);
        Assert.Equal(-60000, supplied.Money.Income);
        Assert.Equal(100000, supplied.Food.Forecast);
        var withoutCity = EconomicOutlook.Read(snapshot, new Dictionary<int, int>(), 5, 11);
        Assert.Equal(40000, withoutCity.Money.Forecast);
        Assert.Equal(200000, withoutCity.Food.Forecast);
        Assert.Equal(before, snapshot.StateHash);
    }

    [Theory]
    [InlineData(1,1,9,180)]
    [InlineData(3,21,1,100)]
    [InlineData(7,1,9,360)]
    [InlineData(12,21,1,190)]
    public void EstimatesUseNextIncomeBoundaryAndDoNotChargeTheRuler(int month,int day,int salaryCount,int foodDays)
    {
        var clock=new GameClock(190,month,day);
        var world=new WorldSnapshot(clock,new(new(189,1,1),1))
        {
            Forces=[new(5,100)], Corps=[new(10,5,1,new(1000, 1000)) { IsRoot = true }],
            Settlements=[new(15,5,500,1000,1000,50,100,0,1000,0,1000) { CorpsId=10,Kind=SettlementKind.City }],
            Officers=[new(1,5,15,new(50,50,50,50),false) { Status=OfficerStatus.Ruler },new(2,5,15,new(50,50,50,50),false) { Status=OfficerStatus.Serving }]
        };
        string before=world.StateHash;
        var forecast=EconomicOutlook.Read(world,new Dictionary<int,int>(),5);
        Assert.Equal(salaryCount,forecast.Money.Expenditure);
        Assert.Equal(foodDays,forecast.Food.Expenditure);
        Assert.Equal(1000-salaryCount,forecast.Money.Forecast);
        Assert.Equal(1000-foodDays,forecast.Food.Forecast);
        Assert.Equal(before,world.StateHash);
    }
    [Fact]
    public void NewlyStartedScenarioSkipsIncomeBoundariesBeforeThirtyDays()
    {
        var started = new GameClock(190, 6, 21);
        var world = new WorldSnapshot(started, new(started, 1))
        {
            Forces = [new(5, 100)], Corps = [new(10, 5, null, new(1000, 1000)) { IsRoot = true }],
            Settlements = [new(15, 5, 500, 100, 100, 50, 800, 100, 100, 100, 100) { CorpsId = 10 }],
            Officers = [new(1, 5, 15, new(50, 50, 50, 50), false)]
        };
        var result = EconomicOutlook.Read(world, new Dictionary<int, int>(), 5);
        Assert.Equal(10, result.Money.Expenditure);
        Assert.Equal(370, result.Food.Expenditure);
        var engineWorld = WorldState.Create(started, 1, world.Forces, world.Settlements, world.Officers, corps: world.Corps);
        var mutation = engineWorld.BeginMutation();
        mutation.SetClock(new(190, 7, 1));
        List<DomainEvent> events = [];
        EconomicCycle.Apply(ResolutionStage.QuarterUpdate, mutation, EconomyRulebook.Empty, events);
        Assert.Empty(events);
        Assert.Equal(1000u, mutation.Corps[10].Treasury.Money);
        Assert.Equal(1000u, mutation.Corps[10].Treasury.Food);
    }

    [Fact]
    public void ForecastCapsEachAccountBeforeSummingTheForce()
    {
        var clock=new GameClock(190,1,1);
        var world=new WorldSnapshot(clock,new(clock,1))
        {
            Forces=[new(5,100)], Corps=[new(10,5,1,new(999999, 0)) { IsRoot = true },new(11,5,2,new(999999,0))],
            Settlements=[new(15,5,0,1000,1000,50,100,100,1000,0,1000) { CorpsId=10,Kind=SettlementKind.City },new(16,5,0,1000,1000,50,100,100,1000,0,1000) { CorpsId=11,Kind=SettlementKind.City }]
        };
        Assert.Equal(2000000,EconomicOutlook.Read(world,new Dictionary<int,int>(),5).Money.Forecast);
        Assert.Equal(1000000,EconomicOutlook.Read(world,new Dictionary<int,int>(),5,11).Money.Forecast);
    }
}
