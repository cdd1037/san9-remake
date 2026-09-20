using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MerchantInvestmentTests
{
    private static MerchantInvestmentDefinition Definition => new([
        new("cash", [1, 3], ResourceKind.Money, new("cash.banned")), new("food", [2], ResourceKind.Food, new("food.banned"))],
        12, 50, 1000, 100, 5, 20, 3, 2, 4, 5) { Id = "merchant", Consumption = EventConsumption.None };
    private static SimulationRules Rules => new() { Personnel = new([new(1, new(0, 0)), new(2, new(0, 0)), new(3, new(0, 0))], searchEvents: [Definition]) };
    private static ulong Seed(int roll) => Enumerable.Range(1, 10000).Select(i => (ulong)i).First(s => new StableRandom(s).NextUInt64() % 100 == (ulong)roll);
    private static (WorldMutation World, PersistentTask Task) Create(int eligibilityRoll = 0, int investmentRoll = 0)
    {
        var world = WorldState.Create(new(190, 2, 1), 1,
            [new(1, 5000, 100, 51) { BaseTrust = 51, LeaderId = 1 }, new(2, 5000, 100, 51) { BaseTrust = 51 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(0, 0, 0, 0), false) { Status = OfficerStatus.Ruler }, new(3, 1, 1, new(0, 0, 99, 0), false)],
            controls: [new("player", 1, ControllerKind.Player), new("other", 2, ControllerKind.Player)], foodExchangeRate: 7).BeginMutation();
        world.SetSimulation(world.Simulation with { RandomStreams = [new("event-search-eligibility", Seed(eligibilityRoll)), new("event-merchant-investment", Seed(investmentRoll))] });
        return (world, new(Guid.NewGuid(), "personnel-search", "player", 1, 3, 1, world.Clock, world.Clock, 0, 0) { Journey = new(1, null, 0) });
    }

    [Theory]
    [InlineData("valid", true, true)]
    [InlineData("foreign", true, true)]
    [InlineData("automatic", false, false)]
    [InlineData("chance", false, true)]
    [InlineData("trust50", false, true)]
    [InlineData("money1000", false, true)]
    [InlineData("full-money", false, true)]
    [InlineData("full-food", false, true)]
    [InlineData("site", false, true)]
    [InlineData("banned", false, true)]
    [InlineData("world-key", true, true)]
    [InlineData("other-force", true, true)]
    public void QualificationPreservesPoliticsRandomBeforeAllResourceSiteAndHistoryGates(string variant, bool expected, bool draws)
    {
        var (world, task) = Create(variant == "chance" ? 8 : 7);
        switch (variant)
        {
            case "foreign": world.Replace(world.Settlements[1] with { OwnerId = 2 }); break;
            case "automatic": world.SetSimulation(world.Simulation with { Controls = [] }); break;
            case "trust50": world.Replace(world.Forces[1] with { BaseTrust = 50 }); break;
            case "money1000": world.Replace(world.Forces[1] with { Money = 1000 }); break;
            case "full-money": world.Replace(world.Forces[1] with { Money = 1000000 }); break;
            case "full-food": world.Replace(world.Forces[1] with { Food = 1000000 }); break;
            case "site": task = task with { Journey = new(999, null, 0) }; break;
            case "banned": world.Replace(world.Forces[1] with { EventHistory = [new("cash.banned")] }); break;
            case "world-key": world.SetSimulation(world.Simulation with { ConsumedEventKeys = [new("cash.banned")] }); break;
            case "other-force": world.Replace(world.Forces[2] with { EventHistory = [new("cash.banned")] }); break;
        }
        var rng = new StableRandom(Seed(variant == "chance" ? 8 : 7)); if (draws) rng.NextUInt64();
        Assert.Equal(expected, MerchantInvestment.Prepare(world, task, Definition));
        Assert.Equal(rng.State, world.Simulation.RandomStreams.Single(s => s.Name == "event-search-eligibility").State);
    }

    [Theory]
    [InlineData(0, 19, true)]
    [InlineData(0, 20, false)]
    [InlineData(99, 98, true)]
    [InlineData(99, 99, false)]
    [InlineData(100, 99, true)]
    public void InvestmentUsesEffectivePoliticsAndOneStrictRoll(int politics, int roll, bool wins)
    {
        var (world, task) = Create(investmentRoll: roll); world.Replace(world.Officers[3] with { BaseAbilities = new(500, 500, politics, 500) });
        List<DomainEvent> events = [];
        MerchantInvestment.Resolve(world, task, Rules, new("merchant"), new(MerchantInvestmentChoice.Invest, 101), events);
        var result = Assert.Single(events.OfType<MerchantInvestmentResolvedEvent>());
        long gross = wins ? 151 + 4L * Math.Max(1, politics) : 0;
        Assert.Equal(wins, result.Succeeded); Assert.Equal(gross, result.GrossReturn);
        Assert.Equal((uint)(5000 - 101 + gross), world.Forces[1].Money); Assert.Equal(51, result.BaseTrust);
        Assert.Empty(world.Forces[1].EventHistory); Assert.Empty(world.Simulation.ConsumedEventKeys);
        Assert.Equal(politics, world.Officers[3].BaseAbilities.Politics);
    }

    [Fact]
    public void FoodInvestmentUsesCurrentRateAndIndependentCorpsForBothDebitAndReturn()
    {
        var (world, task) = Create(); task = task with { Journey = new(2, null, 0) };
        world.AddCorps(new(7, 1, 3, new(5000, 999000)) { ControlMode = CorpsControlMode.Direct });
        world.Replace(world.Settlements[1] with { CorpsId = 7 }); List<DomainEvent> events = [];
        Assert.True(MerchantInvestment.Prepare(world, task, Definition));
        MerchantInvestment.Resolve(world, task, Rules, new("merchant"), new(MerchantInvestmentChoice.Invest, 101), events);
        var result = Assert.Single(events.OfType<MerchantInvestmentResolvedEvent>());
        Assert.Equal(3829, result.GrossReturn); Assert.Equal(1000000u, world.Corps[7].Treasury!.Food);
        Assert.Equal(4899u, world.Corps[7].Treasury!.Money); Assert.Equal(5000u, world.Forces[1].Money); Assert.Equal(100u, world.Forces[1].Food);
        Assert.Equal(2829, events.OfType<ResourceChangedEvent>().Single(e => e.Resource == ResourceKind.Food).SaturationLoss);
    }

    [Fact]
    public void ExecutionBansOnlyThisForceAndCategoryWhileRefusalAllowsRetryWithoutDrawing()
    {
        var (world, task) = Create(); var random = world.Simulation.RandomStreams.ToArray(); List<DomainEvent> events = [];
        MerchantInvestment.Resolve(world, task, Rules, new("merchant"), new(MerchantInvestmentChoice.Decline, 0), events);
        Assert.True(MerchantInvestment.CanResolve(world, task, Rules, new("merchant")));
        MerchantInvestment.Resolve(world, task, Rules, new("merchant"), new(MerchantInvestmentChoice.Execute, 0), events);
        Assert.Equal(46, world.Forces[1].BaseTrust); Assert.Equal(new PersistentEventKey("cash.banned"), Assert.Single(world.Forces[1].EventHistory));
        world.Replace(world.Forces[1] with { BaseTrust = 51 });
        Assert.False(MerchantInvestment.CanResolve(world, task with { Journey = new(3, null, 0) }, Rules, new("merchant")));
        Assert.True(MerchantInvestment.CanResolve(world, task with { Journey = new(2, null, 0) }, Rules, new("merchant")));
        Assert.True(MerchantInvestment.CanResolve(world, task with { ForceId = 2 }, Rules, new("merchant")));
        Assert.Equal(random, world.Simulation.RandomStreams.ToArray()); Assert.DoesNotContain(events, e => e is ResourceChangedEvent);
        Assert.Equal(5000u, world.Forces[1].Money);
    }

    [Theory]
    [InlineData(MerchantInvestmentChoice.Invest, 99)]
    [InlineData(MerchantInvestmentChoice.Invest, 1001)]
    [InlineData(MerchantInvestmentChoice.Decline, 100)]
    [InlineData(MerchantInvestmentChoice.Execute, 100)]
    [InlineData((MerchantInvestmentChoice)99, 0)]
    public void InvalidInputHasNoPartialMutation(MerchantInvestmentChoice choice, uint amount)
    {
        var (world, task) = Create(); var force = world.Forces[1]; var random = world.Simulation.RandomStreams.ToArray(); List<DomainEvent> events = [];
        Assert.Throws<ArgumentException>(() => MerchantInvestment.Resolve(world, task, Rules, new("merchant"), new(choice, amount), events));
        Assert.Equal(force, world.Forces[1]); Assert.Equal(random, world.Simulation.RandomStreams.ToArray()); Assert.Empty(events);
    }

    [Fact]
    public void InvalidatedMerchantCancelsWithoutInvestmentRandomOrPayment()
    {
        var (world, task) = Create(); Assert.True(MerchantInvestment.Prepare(world, task, Definition));
        world.Replace(world.Forces[1] with { Food = 1000000 }); var random = world.Simulation.RandomStreams.ToArray(); List<DomainEvent> events = [];
        Assert.Throws<InvalidOperationException>(() => MerchantInvestment.Resolve(world, task, Rules, new("merchant"), new(MerchantInvestmentChoice.Invest, 100), events));
        Assert.Equal(5000u, world.Forces[1].Money); Assert.Equal(random, world.Simulation.RandomStreams.ToArray()); Assert.Empty(events);
    }

    [Fact]
    public void LargeAuthoredReturnSaturatesAccountWithoutOverflow()
    {
        var (world, task) = Create();
        world.SetSimulation(world.Simulation with { FoodExchangeRate = int.MaxValue });
        task = task with { Journey = new(2, null, 0) };
        var d = Definition with { ReturnNumerator = int.MaxValue, ReturnDenominator = 1 };
        var rules = new SimulationRules { Personnel = new([new(1, new(0, 0)), new(2, new(0, 0)), new(3, new(0, 0))], searchEvents: [d]) };
        List<DomainEvent> events = [];
        MerchantInvestment.Resolve(world, task, rules, new("merchant"), new(MerchantInvestmentChoice.Invest, 1000), events);
        Assert.Equal(1000000u, world.Forces[1].Food);
        Assert.Equal(4000u, world.Forces[1].Money);
        var reward = events.OfType<ResourceChangedEvent>().Single(e => e.Resource == ResourceKind.Food);
        Assert.Equal(long.MaxValue, reward.Requested); Assert.Equal(999900, reward.Actual);
    }

    [Fact]
    public void MonthlyMarketPreservesDuplicateWeightsAndSkipsUnconfiguredMarket()
    {
        var (world, _) = Create(); List<DomainEvent> events = [];
        FoodMarket.Refresh(world, null, events); Assert.Equal(7, world.Simulation.FoodExchangeRate); Assert.Empty(events);
        var pools = Enumerable.Range(1, 12).Select(m => ImmutableArray.Create(m, m, m + 1)).ToImmutableArray();
        var market = new FoodMarketDefinition(pools); market.Validate();
        var rng = new StableRandom(StableRandom.DeriveSeed(world.Simulation.RunSeed, "food-market"));
        int expected = pools[1][(int)(rng.NextUInt64() % 3)]; FoodMarket.Refresh(world, market, events);
        Assert.Equal(expected, world.Simulation.FoodExchangeRate); Assert.Equal(rng.State, world.Simulation.RandomStreams.Single(s => s.Name == "food-market").State);
        Assert.Equal(expected, Assert.Single(events.OfType<FoodExchangeRateChangedEvent>()).Current);
    }
}
