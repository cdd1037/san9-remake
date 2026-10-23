using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Core.Tests;

public sealed class NewGameInitializationTests
{
    private static WorldState Create(NewGameSettings settings, ulong seed = 19) => WorldState.Create(new(190, 1, 1), seed,
        [new(1, 100), new(2, 100), new(3, 100) { IsBarbarian = true }, new(4, 100) { IsDormant = true }],
        [Place(1, 1), Place(2, 2), Place(3, 3), Place(4, 4) with { IsDormant = true },
            Place(5, null), Place(6, 2) with { Kind = SettlementKind.Facility }, Place(7, null) with { Kind = SettlementKind.Facility }],
        [new OfficerState(9001, 1, 1, new(80, 70, 60, 90), false) { Status = OfficerStatus.Serving, ParentId = 88, BloodlineId = 77 },
            new OfficerState(9002, null, 2, new(10, 20, 30, 40), false) { Status = OfficerStatus.Free },
            new OfficerState(9004, 2, 2, new(40, 30, 20, 10), false),
            new OfficerState(9003, null, 2, new(10, 20, 30, 40), false) { Status = OfficerStatus.NotAppeared }],
        [new(10, 2, new(0, 0), 100) { Morale = 95 }],
        corps: [new(1, 1, null, new(10001, 900001)) { IsRoot = true, ControlMode = CorpsControlMode.Automatic }, new(2, 2, null, new(10001, 900001)) { IsRoot = true },
            new(3, 3, null, new(10001, 900001)) { IsRoot = true }, new(4, 4, null, new(10001, 900001)) { IsRoot = true }, new(20, 2, 9004, new(900001, 100))],
        controls: [new("player", 1, ControllerKind.Player), new("ai:2", 2, ControllerKind.Automatic), new("ai:3", 3, ControllerKind.Automatic)],
        newGame: settings, officerStartingPlaces: [1, 2]);

    private static SettlementState Place(int id, int? owner) => new(id, owner, 15000, 50, 100, 95, 950, 90, 100, 90, 100)
        { SoldierCapacity = 30000, DraftPopulation = 1234 };

    [Theory]
    [InlineData(GameDifficulty.Beginner, 12001u, 16000u)]
    [InlineData(GameDifficulty.Advanced, 14001u, 26000u)]
    public void DifficultyAndManualBonusesHaveIndependentScopesAndCaps(GameDifficulty difficulty, uint money, uint soldiers)
    {
        var world = Create(new() { Difficulty = difficulty, MoneyBonus = 0, FoodBonus = 50,
            DomesticBonus = 100, SoldierBonus = 1000, MoraleBonus = 20 }).CreateSnapshot();
        Assert.Equal(money, world.Corps.Single(c => c.Id == 2).Treasury.Money);
        Assert.Equal(1000000u, world.Corps.Single(c => c.Id == 2).Treasury.Food);
        Assert.Equal(1000000u, world.Corps.Single(c => c.Id == 20).Treasury.Money);
        Assert.Equal(difficulty == GameDifficulty.Beginner ? 170u : 190u, world.Corps.Single(c => c.Id == 20).Treasury.Food);
        Assert.Equal(soldiers, world.Settlements.Single(s => s.Id == 2).Soldiers);
        Assert.Equal(16000u, world.Settlements.Single(s => s.Id == 3).Soldiers);
        Assert.All(world.Settlements.Where(s => s.Id is 1 or 4 or 5 or 6), s => Assert.Equal(15000u, s.Soldiers));
        Assert.All(world.Corps.Where(c => c.Id is 1 or 3 or 4), c => Assert.Equal(new CorpsTreasury(10001, 900001), c.Treasury));
        var city = world.Settlements.Single(s => s.Id == 2);
        Assert.Equal((1000, 100, 100, 100, 100), (city.PublicOrder, city.Commerce, city.Cultivation, city.Durability, city.Morale));
        Assert.Equal(100, world.Settlements.Single(s => s.Id == 6).Morale);
        Assert.Equal(100, world.Settlements.Single(s => s.Id == 7).Morale);
        Assert.Equal(95, world.Settlements.Single(s => s.Id == 5).Morale);
        Assert.Equal(100, Assert.Single(world.Units).Morale);
        Assert.All(world.Settlements, s => Assert.Equal(1234u, s.DraftPopulation));
    }

    [Fact]
    public void FictionalSetupPreservesAbilitiesAndServiceWhileDeterministicallyReplacingPersonalRelations()
    {
        var options = new NewGameSettings { Mode = GameMode.Fictional };
        var first = Create(options).CreateSnapshot();
        StateAssert.Equal(first, Create(options).CreateSnapshot());
        StateAssert.NotEqual(first, Create(options, 20).CreateSnapshot());
        var serving = first.Officers.Single(p => p.Id == 9001);
        Assert.Equal((1, 1, OfficerStatus.Serving, new OfficerAbilities(80, 70, 60, 90)),
            (serving.OwnerId, serving.SettlementId, serving.Status, serving.BaseAbilities));
        Assert.All(first.Officers, p =>
        {
            Assert.Null(p.ParentId); Assert.Equal(p.Id, p.BloodlineId);
            Assert.InRange(p.Righteousness, 0, 15); Assert.InRange(p.Ambition, 0, 15);
            Assert.InRange(p.Affinity, 0, 149); Assert.InRange(p.LifespanTier, 0, 7);
        });
        Assert.InRange(serving.Loyalty, 80, 100);
        Assert.Equal(50, serving.LoyaltyTo(2));
        Assert.All(first.Relations, r => Assert.Equal(50, r.Value));
        Assert.Equal(3, first.Relations.Count);
    }
}
