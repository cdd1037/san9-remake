using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class FoodTradeTests
{
    private static WorldMutation World(uint money = 10000, uint food = 10000) => WorldState.Create(new(190, 2, 1), 17,
        [new(1, 100)],
        [new(1, 1, 0, 100, 100, 50, 0, 0, 100, 0, 100) { MerchantPresenceChance = 100, MerchantPresent = true }],
        Enumerable.Range(1, 6).Select(id => new OfficerState(id, 1, 1, new(80, 80, 80, 80), false)),
        controls: [new("player", 1, ControllerKind.Player)], foodExchangeRate: 7, corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(money, food)) { IsRoot = true }]).BeginMutation();
    private static FoodTradeCommand Command(FoodTradeDirection direction, uint amount, params int[] people) =>
        new(Guid.NewGuid(), 1, 0, 1, people.ToImmutableArray(), direction, amount);

    [Theory]
    [InlineData(FoodTradeDirection.Buy, 1, 1)]
    [InlineData(FoodTradeDirection.Buy, 7, 1)]
    [InlineData(FoodTradeDirection.Buy, 8, 2)]
    [InlineData(FoodTradeDirection.Sell, 1, 0)]
    [InlineData(FoodTradeDirection.Sell, 7, 1)]
    [InlineData(FoodTradeDirection.Sell, 8, 1)]
    public void IntegerTradesConserveBothResourcesAndConsumeSharedCityUse(FoodTradeDirection direction, uint amount, uint money)
    {
        var world = World(); List<DomainEvent> events = [];
        Assert.True(FoodTrade.Apply(world, Command(direction, amount, 1, 2), events));
        var result = Assert.Single(events.OfType<FoodTradeCompletedEvent>());
        Assert.Equal(money, result.MoneyAmount);
        Assert.Equal(direction == FoodTradeDirection.Buy ? 10000 - money : 10000 + money, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Money);
        Assert.Equal(direction == FoodTradeDirection.Buy ? 10000 + amount : 10000 - amount, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Food);
        Assert.True(world.Officers[1].Acted); Assert.True(world.Officers[2].Acted); Assert.False(world.Officers[3].Acted);
        Assert.Equal(FoodTradeRejection.AlreadyUsed, FoodTrade.Quote(world, 1, 1, [3], FoodTradeDirection.Buy).Rejection);
        Assert.Equal(FoodTradeRejection.AlreadyUsed, FoodTrade.Quote(world, 1, 1, [3], FoodTradeDirection.Sell).Rejection);
        Assert.Empty(world.Simulation.RandomStreams);
        XunRecovery.Settlements(world);
        XunRecovery.Personnel(world);
        Assert.True(FoodTrade.Quote(world, 1, 1, [1], direction).Available);
    }

    [Theory]
    [InlineData(60, 4000)]
    [InlineData(80, 6000)]
    [InlineData(100, 10000)]
    public void LimitsUseExecutedPoliticsNotRawPolitics(int politics, uint expected)
    {
        var world = World(100000, 100000);
        world.Replace(world.Officers[1] with { BaseAbilities = new(80, 80, politics, 80) });
        Assert.Equal(expected, FoodTrade.Quote(world, 1, 1, [1], FoodTradeDirection.Buy).MaximumFoodAmount);
        Assert.Equal(expected + 6000, FoodTrade.Quote(world, 1, 1, [1, 2], FoodTradeDirection.Sell).MaximumFoodAmount);
    }

    [Theory]
    [InlineData(FoodTradeDirection.Buy, 3, 10000, 21)]
    [InlineData(FoodTradeDirection.Buy, 10000, 999997, 3)]
    [InlineData(FoodTradeDirection.Sell, 999997, 10000, 21)]
    [InlineData(FoodTradeDirection.Sell, 10000, 9, 9)]
    [InlineData(FoodTradeDirection.Sell, 10000, 6, 0)]
    [InlineData(FoodTradeDirection.Buy, 0, 0, 0)]
    [InlineData(FoodTradeDirection.Buy, 10000, 1000000, 0)]
    [InlineData(FoodTradeDirection.Sell, 1000000, 10000, 0)]
    public void LimitsRespectCurrentBalancesAndCapacities(FoodTradeDirection direction, uint money, uint food, uint expected)
    {
        var world = World(money, food);
        Assert.Equal(expected, FoodTrade.Quote(world, 1, 1, [1], direction).MaximumFoodAmount);
        List<DomainEvent> events = [];
        Assert.False(FoodTrade.Apply(world, Command(direction, expected + 1, 1), events));
        Assert.Equal(money, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Money); Assert.Equal(food, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Food);
        Assert.False(world.Officers[1].Acted); Assert.False(world.Settlements[1].FoodTradeUsed); Assert.Empty(events);
    }

    [Theory]
    [InlineData("none")][InlineData("duplicate")][InlineData("six")][InlineData("missing")]
    [InlineData("acted")][InlineData("free")][InlineData("away")][InlineData("foreign")]
    [InlineData("merchant")][InlineData("facility")][InlineData("used")][InlineData("direction")]
    [InlineData("zero")][InlineData("task")]
    public void InvalidTradesHaveNoEffects(string invalid)
    {
        var world = World(); var command = Command(FoodTradeDirection.Buy, 7, 1);
        switch (invalid)
        {
            case "none": command = command with { OfficerIds = [] }; break;
            case "duplicate": command = command with { OfficerIds = [1, 1] }; break;
            case "six": command = command with { OfficerIds = [1, 2, 3, 4, 5, 6] }; break;
            case "missing": command = command with { OfficerIds = [99] }; break;
            case "acted": world.Replace(world.Officers[1] with { Acted = true }); break;
            case "free": world.Replace(world.Officers[1] with { Status = OfficerStatus.Free }); break;
            case "away": world.Replace(world.Officers[1] with { SettlementId = null }); break;
            case "foreign": world.Replace(world.Settlements[1] with { OwnerId = null }); break;
            case "merchant": world.Replace(world.Settlements[1] with { MerchantPresent = false }); break;
            case "facility": world.Replace(world.Settlements[1] with { Kind = SettlementKind.Facility }); break;
            case "used": world.Replace(world.Settlements[1] with { FoodTradeUsed = true }); break;
            case "direction": command = command with { Direction = (FoodTradeDirection)99 }; break;
            case "zero": command = command with { FoodAmount = 0 }; break;
            case "task": world.SetSimulation(world.Simulation with { Tasks = [new(Guid.NewGuid(), "personnel-search", "player", 1, 1, 1, world.Clock, world.Clock, 0, 0)] }); break;
        }
        var city = world.Settlements[1]; var person = world.Officers[1]; List<DomainEvent> events = [];
        Assert.False(FoodTrade.Apply(world, command, events));
        Assert.Equal(10000u, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Money); Assert.Equal(10000u, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Food);
        Assert.Equal(city, world.Settlements[1]); Assert.Equal(person, world.Officers[1]); Assert.Empty(events); Assert.Empty(world.Simulation.RandomStreams);
    }

    [Fact]
    public void QuoteDoesNotReserveFundsAndExecutionRechecksCurrentState()
    {
        var world = World(); Assert.Equal(6000u, FoodTrade.Quote(world, 1, 1, [1], FoodTradeDirection.Buy).MaximumFoodAmount);
        world.Replace(ResourceAccounts.Root(world, 1) with { Treasury = ResourceAccounts.Root(world, 1).Treasury with { Money = 1 } });
        List<DomainEvent> events = []; Assert.False(FoodTrade.Apply(world, Command(FoodTradeDirection.Buy, 8, 1), events));
        Assert.Equal(1u, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Money); Assert.Empty(events);
    }

    [Fact]
    public void MerchantRefreshUsesStrictChanceAndDoesNotRollForPermanentOrDisabledCities()
    {
        var world = World();
        world.Replace(world.Settlements[1] with { MerchantPresenceChance = 20, OwnerId = null });
        world.AddSettlement(world.Settlements[1] with { Id = 2, MerchantPresenceChance = 100, MerchantPresent = false });
        world.AddSettlement(world.Settlements[1] with { Id = 3, MerchantPresenceChance = 0, MerchantPresent = true });
        world.AddSettlement(world.Settlements[1] with { Id = 4, Kind = SettlementKind.Facility, MerchantPresenceChance = 0, MerchantPresent = false });
        foreach (int roll in new[] { 19, 20 })
        {
            ulong seed = Enumerable.Range(1, 10000).Select(i => (ulong)i).First(s => new StableRandom(s).NextUInt64() % 100 == (ulong)roll);
            world.SetSimulation(world.Simulation with { RandomStreams = [new("city-merchants", seed)] });
            CityMerchants.Refresh(world, []);
            Assert.Equal(roll == 19, world.Settlements[1].MerchantPresent);
            Assert.True(world.Settlements[2].MerchantPresent); Assert.False(world.Settlements[3].MerchantPresent); Assert.False(world.Settlements[4].MerchantPresent);
            var expected = new StableRandom(seed); expected.NextUInt64(); Assert.Equal(expected.State, world.Simulation.RandomStreams.Single().State);
        }
    }
}
