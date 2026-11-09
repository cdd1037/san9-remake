using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Core.Tests;

public sealed class ComputerIncomeTests
{
    private static readonly CompanionSettings Rates = new() { ComputerRevenuePercent = 200, ComputerHarvestPercent = 150 };

    private static SettlementState City(int id = 1) => new(id, 5, 0, 100, 100, 0, 0, 101, 1000, 101, 1000)
        { Kind = SettlementKind.City, Population = 40000, CorpsId = id };

    private static WorldState World(bool player = false, bool barbarian = false, uint balance = 1000) =>
        WorldState.Create(new(190, 1, 1), 190, [new(5, 100) { IsBarbarian = barbarian, LeaderId = 1 }],
            [City(), City(2)], [new(1, 5, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
                new(2, 5, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Governor }],
            corps: [new(1, 5, 1, new(balance, balance)) { IsRoot = true },
                new(2, 5, 2, new(balance, balance))],
            controls: [new("controller", 5, player ? ControllerKind.Player : ControllerKind.Automatic)]);

    [Theory]
    [InlineData(0, 0L, 0L)]
    [InlineData(50, 176L, 1525L)]
    [InlineData(100, 354L, 3051L)]
    [InlineData(125, 443L, 3814L)]
    [InlineData(200, 709L, 6103L)]
    [InlineData(int.MaxValue, 7617677171L, 65539960734L)]
    public void PercentagesApplyInsideEachFormulaWithExplicitIntegerRounding(int percent, long revenue, long harvest)
    {
        // R=10, commerce=cultivation=101. At 200%, final-result doubling would incorrectly give 708/6102.
        var city = City() with { Population = 100 };
        Assert.Equal(revenue, EconomyMath.Revenue(city, percent));
        Assert.Equal(harvest, EconomyMath.Harvest(city, percent));
    }

    [Theory]
    [InlineData(false, false, GameDifficulty.Beginner, 1929L, 8383L)]
    [InlineData(false, false, GameDifficulty.Advanced, 1929L, 8383L)]
    [InlineData(true, false, GameDifficulty.Advanced, 1464L, 5922L)]
    [InlineData(false, true, GameDifficulty.Advanced, 30000L, 100000L)]
    public void PeriodicSettlementAndForecastShareRatesAndKeepEachAccountIndependent(bool player, bool barbarian,
        GameDifficulty difficulty, long money, long food)
    {
        var world = World(player, barbarian);
        var mutation = world.BeginMutation(companion: Rates);
        mutation.SetSimulation(mutation.Simulation with { NewGame = new() { Difficulty = difficulty } });
        mutation.SetClock(new(190, 6, 21));
        world.Commit(mutation);
        var before = world.CreateSnapshot();
        var outlook = EconomicOutlook.Read(before, new Dictionary<int, int>(), 5, Rates);
        Assert.Equal(2 * money, outlook.Money.Forecast + (barbarian ? 0 : outlook.Money.Expenditure));
        Assert.Equal(2 * food, outlook.Food.Forecast);
        StateAssert.Equal(before, world.CreateSnapshot());

        mutation = world.BeginMutation(companion: Rates);
        mutation.SetClock(new(190, 7, 1));
        EconomicCycle.Apply(ResolutionStage.QuarterUpdate, mutation, EconomyRulebook.Empty, []);
        foreach (var account in mutation.Corps.Values)
        {
            Assert.Equal(money, account.Treasury.Money);
            Assert.Equal(food, account.Treasury.Food);
        }
    }

    [Theory]
    [InlineData(9999999u, 10000000u)]
    [InlineData(11000000u, 11000000u)]
    public void LargeRatesRespectCapacityAndNeverReduceExistingExcess(uint balance, uint expected)
    {
        var settings = Rates with { ComputerRevenuePercent = int.MaxValue, ComputerHarvestPercent = int.MaxValue };
        var world = World(balance: balance);
        var mutation = world.BeginMutation(companion: settings);
        mutation.SetClock(new(190, 7, 1));
        EconomicCycle.Apply(ResolutionStage.QuarterUpdate, mutation, EconomyRulebook.Empty, []);
        Assert.All(mutation.Corps.Values, c => Assert.Equal(new CorpsTreasury(expected, expected), c.Treasury));
    }

    [Fact]
    public void ForkKeepsCapturedRatesWhenTheHostChangesConfiguration()
    {
        var engine = new SimulationEngine(World());
        engine.ConfigureCompanion(Rates);
        var branch = engine.Fork();
        engine.ConfigureCompanion(CompanionSettings.Default);
        Assert.Equal(1464, engine.GetAccountOutlook(5, 1).Money.Forecast);
        Assert.Equal(1929, branch.GetAccountOutlook(5, 1).Money.Forecast);
        Assert.Equal(8383, branch.GetAccountOutlook(5, 1).Food.Forecast);
    }
}
