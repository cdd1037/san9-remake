using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;
public sealed class EconomicOutlookTests
{
    [Theory]
    [InlineData(1,1,9,180)]
    [InlineData(3,21,1,100)]
    [InlineData(7,1,9,360)]
    [InlineData(12,21,1,190)]
    public void EstimatesUseNextIncomeBoundaryAndDoNotChargeTheRuler(int month,int day,int salaryCount,int foodDays)
    {
        var clock=new GameClock(190,month,day);
        var world=new WorldSnapshot(clock,new(clock,1))
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
