using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Core.Tests;

public sealed class NewGameInitializationTests
{
    private static WorldState Create(NewGameSettings settings, ulong seed = 19, uint reserve = 900001) => WorldState.Create(new(190, 1, 1), seed,
        [new(1, 100), new(2, 100), new(3, 100) { IsBarbarian = true }, new(4, 100) { IsDormant = true }],
        [Place(1, 1), Place(2, 2), Place(3, 3), Place(4, 4) with { IsDormant = true },
            Place(5, null), Place(6, 2) with { Kind = SettlementKind.Facility }, Place(7, null) with { Kind = SettlementKind.Facility }],
        [new OfficerState(9001, 1, 1, new(80, 70, 60, 90), false) { Status = OfficerStatus.Serving, ParentId = 88, BloodlineId = 77 },
            new OfficerState(9002, null, 2, new(10, 20, 30, 40), false) { Status = OfficerStatus.Free },
            new OfficerState(9004, 2, 2, new(40, 30, 20, 10), false),
            new OfficerState(9003, null, 2, new(10, 20, 30, 40), false) { Status = OfficerStatus.NotAppeared }],
        [new(10, 2, new(0, 0), 100) { Morale = 95 }],
        corps: [new(1, 1, null, new(10001, 900001)) { IsRoot = true, }, new(2, 2, null, new(10001, reserve)) { IsRoot = true },
            new(3, 3, null, new(10001, 900001)) { IsRoot = true }, new(4, 4, null, new(10001, 900001)) { IsRoot = true }, new(20, 2, 9004, new(reserve, 100))],
        controls: [new("player", 1, ControllerKind.Player), new("ai:2", 2, ControllerKind.Automatic), new("ai:3", 3, ControllerKind.Automatic)],
        newGame: settings, officerStartingPlaces: [1, 2]);

    private static SettlementState Place(int id, int? owner) => new(id, owner, 15000, 50, 100, 95, 950, 90, 100, 90, 100)
        { SoldierCapacity = 30000, DraftPopulation = 1234 };

    [Theory]
    [InlineData(0u, 0u, 5000u, 0u, 0, 100, 50)]
    [InlineData(0u, 8000u, 13000u, 0u, 95, 100, 50)]
    [InlineData(50000u, 8000u, 18000u, 45000u, 0, 400, 60)]
    [InlineData(90000u, 8000u, 20000u, 81000u, 95, 100, 50)]
    [InlineData(50000u, 499000u, 500000u, 45000u, 95, 100, 50)]
    [InlineData(50000u, 500000u, 500000u, 45000u, 95, 400, 60)]
    public void FoundedCityRecruitmentUsesDraftPopulationCapacityAndForceMorale(uint draftPopulation, uint soldiers,
        uint expectedSoldiers, uint expectedDraft, int morale, int trust, int expectedMorale)
    {
        var city = Place(1, 1) with { Kind = SettlementKind.City, SoldierCapacity = 500000,
            DraftPopulation = draftPopulation, Soldiers = soldiers, Morale = morale, CorpsId = 1 };
        var world = WorldState.Create(new(190, 1, 1), 19,
            [new(1, trust) { LeaderId = 10 }], [city],
            [new(10, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
            corps: [new(1, 1, 10, new(0, 0)) { IsRoot = true }], foundedForceIds: new HashSet<int> { 1 }).CreateSnapshot();
        var actual = Assert.Single(world.Settlements);
        Assert.Equal(expectedSoldiers, actual.Soldiers);
        Assert.Equal(expectedDraft, actual.DraftPopulation);
        Assert.Equal(expectedMorale, actual.Morale);
        Assert.Equal(soldiers, city.Soldiers);
    }

    [Theory]
    [InlineData(0, 95)]
    [InlineData(1, 95)]
    [InlineData(20, 85)]
    [InlineData(49, 71)]
    [InlineData(50, 70)]
    [InlineData(51, 70)]
    [InlineData(75, 70)]
    [InlineData(149, 95)]
    public void CreatedServingLoyaltyUsesCircularAffinityAndFivePointVariation(int affinity, int minimum)
    {
        var values = new HashSet<int>();
        for (ulong seed = 0; seed < 32; seed++)
        foreach (var mode in Enum.GetValues<GameMode>())
        {
            var world = WorldState.Create(new(190, 1, 1), seed,
                [new(1, 100) { LeaderId = 9001 }], [Place(1, 1)],
                [new(9001, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
                    new(2, 1, 1, new(50, 50, 50, 50), false) { Affinity = affinity }],
                corps: [new(1, 1, 9001, new(0, 0)) { IsRoot = true }],
                newGame: new() { Mode = mode },
                createdOfficerIds: new HashSet<int> { 2 }).CreateSnapshot();
            int loyalty = world.Officers.Single(p => p.Id == 2).Loyalty;
            Assert.InRange(loyalty, minimum, minimum + 4);
            values.Add(loyalty);
        }
        Assert.Equal(Enumerable.Range(minimum, 5), values.Order());
    }

    [Fact]
    public void FictionalSetupExcludesCreatedPeopleRegardlessOfStatusOrIdentityOrder()
    {
        OfficerState[] people =
        [
            new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, Affinity = 20 },
            new(2, 1, 1, new(50, 50, 50, 50), false) { Affinity = 40 },
            new(3, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Free },
            new(4, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.NotAppeared },
            new(5, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered },
            new(6, null, null, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered, IsDormant = true },
            new(9001, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }
        ];
        var createdIds = Enumerable.Range(1, 6).ToHashSet();
        WorldSnapshot Start(GameMode mode) => WorldState.Create(new(190, 1, 1), 19,
            [new(1, 100) { LeaderId = 1 }, new(2, 100) { LeaderId = 9001 }], [Place(1, 1), Place(2, 2)], people,
            corps: [new(1, 1, 1, new(0, 0)) { IsRoot = true }, new(2, 2, 9001, new(0, 0)) { IsRoot = true }],
            newGame: new() { Mode = mode }, officerStartingPlaces: [1, 2], createdOfficerIds: createdIds).CreateSnapshot();
        var historical = Start(GameMode.Historical);
        var fictional = Start(GameMode.Fictional);
        foreach (var expected in historical.Officers.Where(p => createdIds.Contains(p.Id)))
        {
            var actual = fictional.Officers.Single(p => p.Id == expected.Id);
            Assert.Equal(expected with { ForceLoyalties = actual.ForceLoyalties }, actual);
            Assert.Equal(expected.ForceLoyalties.OrderBy(p => p.Key), actual.ForceLoyalties.OrderBy(p => p.Key));
        }
        Assert.NotEqual(historical.Officers.Single(p => p.Id == 9001), fictional.Officers.Single(p => p.Id == 9001));
        Assert.InRange(fictional.Officers.Single(p => p.Id == 2).Loyalty, 85, 89);
    }

    [Theory]
    [InlineData(GameDifficulty.Beginner, 12001u, 16000u)]
    [InlineData(GameDifficulty.Advanced, 14001u, 26000u)]
    public void DifficultyAndManualBonusesHaveIndependentScopesAndCaps(GameDifficulty difficulty, uint money, uint soldiers)
    {
        var world = Create(new() { Difficulty = difficulty, MoneyBonus = 0, FoodBonus = 50,
            DomesticBonus = 100, SoldierBonus = 1000, MoraleBonus = 20 }).CreateSnapshot();
        Assert.Equal(money, world.Corps.Single(c => c.Id == 2).Treasury.Money);
        Assert.Equal(difficulty == GameDifficulty.Beginner ? 1080051u : 1260051u, world.Corps.Single(c => c.Id == 2).Treasury.Food);
        Assert.Equal(difficulty == GameDifficulty.Beginner ? 1080001u : 1260001u, world.Corps.Single(c => c.Id == 20).Treasury.Money);
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

    [Theory]
    [InlineData(GameDifficulty.Beginner)]
    [InlineData(GameDifficulty.Advanced)]
    public void ComputerStartingBonusesSaturateAtTenMillion(GameDifficulty difficulty)
    {
        var world = Create(new() { Difficulty = difficulty, MoneyBonus = 50000, FoodBonus = 500000 },
            reserve: 9_000_001).CreateSnapshot();
        Assert.Equal(10_000_000u, world.Corps.Single(c => c.Id == 2).Treasury.Food);
        Assert.Equal(10_000_000u, world.Corps.Single(c => c.Id == 20).Treasury.Money);
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
