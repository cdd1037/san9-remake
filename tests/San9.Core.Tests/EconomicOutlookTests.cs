using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;
using System.Collections.Immutable;

namespace San9.Core.Tests;
public sealed class EconomicOutlookTests
{
    [Fact]
    public void ConfiguredPeriodSalaryIsSharedBySettlementAndForecast()
    {
        var grades = PersonalPeeragePolicy.Default.Grades.Values.OrderBy(r => r.Grade)
            .Select(r => r.Grade == PersonalPeerageGrade.CountyMarquis ? r with { Salary = 12 } : r).ToImmutableArray();
        var period = new PersonalPeeragePeriod(180, 220, ImmutableDictionary<PersonalPeerageGrade, ImmutableArray<int>>.Empty, []) { GradeRules = grades };
        var economy = new EconomyRulebook([], []) { PersonalPeerageRegimes = ImmutableDictionary<string, PersonalPeerageRegime>.Empty.Add("han", new([period])) };
        var force = new ForceState(5, 100) { LeaderId = 1, PersonalPeerageRegimeId = "han" };
        var world = WorldState.Create(new(190, 6, 21), 1, [force],
            [new SettlementState(15, 5, 0, 100, 100, 0, 100, 0, 100, 0, 100)],
            [new OfficerState(1, 5, 15, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new OfficerState(2, 5, 15, new(50, 50, 50, 50), false) { PersonalPeerage = new(PersonalPeerageGrade.CountyMarquis, null) }],
            corps: [new CorpsState(10, 5, 1, new(1000, 1000)) { IsRoot = true }]);
        Assert.Equal(130, EconomicOutlook.Read(world.CreateSnapshot(), new Dictionary<int, int>(), 5, CompanionSettings.Default,
            peerages: economy.PeeragePolicy(force, 190)).Money.Expenditure);
        var mutation = world.BeginMutation();
        EconomicCycle.Apply(ResolutionStage.XunUpdate, mutation, economy, []);
        Assert.Equal(987u, mutation.Corps[10].Treasury.Money);
    }

    [Fact]
    public void PersonalPeeragePayUsesServingOfficersCorpsAndIncludesRuler()
    {
        var clock = new GameClock(190, 6, 21);
        var world = WorldState.Create(clock, 1,
            [new ForceState(5, 100) { LeaderId = 1 }],
            [new SettlementState(15, 5, 0, 100, 100, 0, 100, 0, 100, 0, 100) { CorpsId = 10 },
             new SettlementState(16, 5, 0, 100, 100, 0, 100, 0, 100, 0, 100) { CorpsId = 11 }],
            [new OfficerState(1, 5, 15, new(50, 50, 50, 50), false)
                { Status = OfficerStatus.Ruler, PersonalPeerage = new(PersonalPeerageGrade.ImperialMarquis, null) },
             new OfficerState(2, 5, 16, new(50, 50, 50, 50), false)
                { PersonalPeerage = new(PersonalPeerageGrade.CountyMarquis, null) }],
            corps: [new CorpsState(10, 5, 1, new(1000, 1000)) { IsRoot = true },
                new CorpsState(11, 5, 2, new(1000, 1000))]);
        var snapshot = world.CreateSnapshot();
        Assert.Equal(10, EconomicOutlook.Read(snapshot, new Dictionary<int, int>(), 5, CompanionSettings.Default, 10).Money.Expenditure);
        Assert.Equal(70, EconomicOutlook.Read(snapshot, new Dictionary<int, int>(), 5, CompanionSettings.Default, 11).Money.Expenditure);
        var withCaptive = snapshot with { Officers = [.. snapshot.Officers,
            new OfficerState(3, 5, 16, new(50, 50, 50, 50), false)
            { Status = OfficerStatus.Captive, PersonalPeerage = new(PersonalPeerageGrade.King, null) }] };
        Assert.Equal(70, EconomicOutlook.Read(withCaptive, new Dictionary<int, int>(), 5, CompanionSettings.Default, 11).Money.Expenditure);
        var mutation = world.BeginMutation();
        List<DomainEvent> events = [];
        EconomicCycle.Apply(ResolutionStage.XunUpdate, mutation, EconomyRulebook.Empty, events);
        Assert.Equal(999u, mutation.Corps[10].Treasury.Money);
        Assert.Equal(993u, mutation.Corps[11].Treasury.Money);
    }

    [Fact]
    public void InsufficientPersonalPeeragePayIsReportedWithoutPartialPayment()
    {
        var world = WorldState.Create(new(190, 6, 21), 1,
            [new ForceState(5, 100) { LeaderId = 1 }],
            [new SettlementState(15, 5, 0, 100, 100, 0, 100, 0, 100, 0, 100)],
            [new OfficerState(1, 5, 15, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new OfficerState(2, 5, 15, new(50, 50, 50, 50), false)
                { PersonalPeerage = new(PersonalPeerageGrade.CountyMarquis, null) }],
            corps: [new CorpsState(10, 5, 1, new(3, 1000)) { IsRoot = true }]);
        var mutation = world.BeginMutation();
        List<DomainEvent> events = [];
        EconomicCycle.Apply(ResolutionStage.XunUpdate, mutation, EconomyRulebook.Empty, events);
        var payment = Assert.Single(events.OfType<ResourceChangedEvent>(), e => e.Reason == "peerage-salary");
        Assert.Equal(0, payment.Actual);
        Assert.Equal(6, payment.Shortfall);
        Assert.Equal(2u, mutation.Corps[10].Treasury.Money);
    }

    [Fact]
    public void AccountGroupingKeepsFieldPersonnelAndTransportCostsWithTheirCurrentCorps()
    {
        var clock = new GameClock(190, 6, 21);
        var world = new WorldSnapshot(clock, new(new(190, 1, 1), 1)
        {
            Tasks = [new(Guid.NewGuid(), "personnel-return", "player", 5, 3, 16, clock, clock.NextDay(), 0, 0)
                { Reinforcements = new(1000, 50, 16) }]
        })
        {
            Forces = [new(5, 100), new(6, 100)],
            Corps = [new(10, 5, null, new(1000, 1000)) { IsRoot = true }, new(11, 5, null, new(2000, 2000))],
            Settlements = [new(15, 5, 500, 100, 100, 50, 800, 0, 100, 0, 100),
                new(16, 5, 1000, 100, 100, 50, 800, 0, 100, 0, 100) { CorpsId = 11 }],
            Units = [new(20, 5, new(0, 0), 1000), new(21, 6, new(0, 0), 90000)],
            Officers = [new(1, 5, null, new(50, 50, 50, 50), false) { UnitId = 20, OfficeId = 1 },
                new(2, 5, 16, new(50, 50, 50, 50), false) { OfficeId = 2 },
                new(3, 5, 16, new(50, 50, 50, 50), false)]
        };
        var salaries = new Dictionary<int, int> { [1] = 7, [2] = 3 };
        var root = EconomicOutlook.Read(world, salaries, 5, CompanionSettings.Default, 10);
        var branch = EconomicOutlook.Read(world, salaries, 5, CompanionSettings.Default, 11);
        var force = EconomicOutlook.Read(world, salaries, 5, CompanionSettings.Default);
        Assert.Equal(7, root.Money.Expenditure);
        Assert.Equal(4, branch.Money.Expenditure); // Includes the returning transport's officer.
        Assert.Equal(40, root.Food.Expenditure);
        Assert.Equal(50, branch.Food.Expenditure);
        Assert.Equal(root.Money.Forecast + branch.Money.Forecast, force.Money.Forecast);
        Assert.Equal(root.Food.Forecast + branch.Food.Forecast, force.Food.Forecast);
    }

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
        var before = snapshot;
        var supplied = EconomicOutlook.Read(snapshot, new Dictionary<int, int>(), 5, CompanionSettings.Default, 10);
        Assert.Equal(0, supplied.Money.Expenditure);
        Assert.Equal(30000, supplied.Money.Forecast);
        Assert.Equal(-60000, supplied.Money.Income);
        Assert.Equal(100000, supplied.Food.Forecast);
        var withoutCity = EconomicOutlook.Read(snapshot, new Dictionary<int, int>(), 5, CompanionSettings.Default, 11);
        Assert.Equal(40000, withoutCity.Money.Forecast);
        Assert.Equal(200000, withoutCity.Food.Forecast);
        StateAssert.Equal(before, snapshot);
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
        var before=world;
        var forecast=EconomicOutlook.Read(world,new Dictionary<int,int>(), 5,CompanionSettings.Default);
        Assert.Equal(salaryCount,forecast.Money.Expenditure);
        Assert.Equal(foodDays,forecast.Food.Expenditure);
        Assert.Equal(1000-salaryCount,forecast.Money.Forecast);
        Assert.Equal(1000-foodDays,forecast.Food.Forecast);
        StateAssert.Equal(before, world);
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
        var result = EconomicOutlook.Read(world, new Dictionary<int, int>(), 5, CompanionSettings.Default);
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

    [Theory]
    [InlineData(9_999_999u, 10_000_000L)]
    [InlineData(10_000_000u, 10_000_000L)]
    [InlineData(11_000_000u, 11_000_000L)]
    public void ForecastCapsEachAccountBeforeSummingTheForceAndMatchesSettlement(uint balance, long expected)
    {
        var clock=new GameClock(190,6,21);
        var world=new WorldSnapshot(clock,new(new(190,1,1),1))
        {
            Forces=[new(5,100)], Corps=[new(10,5,null,new(balance, balance)) { IsRoot = true },new(11,5,2,new(balance,balance))],
            Officers=[new(2,5,16,new(50,50,50,50),false) { OfficeId=7 }],
            Settlements=[new(15,5,0,1000,1000,0,100,100,1000,100,1000) { CorpsId=10,Kind=SettlementKind.City },new(16,5,0,1000,1000,0,100,100,1000,100,1000) { CorpsId=11,Kind=SettlementKind.City }]
        };
        var salaries = new Dictionary<int,int> { [7] = 0 };
        var forecast = EconomicOutlook.Read(world, salaries, 5, CompanionSettings.Default);
        Assert.Equal(2 * expected, forecast.Money.Forecast);
        Assert.Equal(2 * expected, forecast.Food.Forecast);
        var branch = EconomicOutlook.Read(world, salaries, 5, CompanionSettings.Default, 11);
        Assert.Equal(expected, branch.Money.Forecast);
        Assert.Equal(expected, branch.Food.Forecast);
        var mutation = WorldState.Create(world.Simulation.Started, 1, world.Forces, world.Settlements,
            world.Officers, corps: world.Corps).BeginMutation();
        mutation.SetClock(new(190,7,1));
        EconomicCycle.Apply(ResolutionStage.QuarterUpdate, mutation, EconomyRulebook.Empty, []);
        Assert.Equal(branch.Money.Forecast, mutation.Corps[11].Treasury.Money);
        Assert.Equal(branch.Food.Forecast, mutation.Corps[11].Treasury.Food);
    }
}
