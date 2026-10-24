using San9.Application.Presentation;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class FacilityCommandAvailabilityTests
{
    private static readonly string[] Kinds = ["patrol", "commerce", "cultivate", "repair", "recruit", "train", "buy", "sell"];

    private static void Check(WorldSnapshot world, string kind, string? reason)
    {
        var view = new StrategySnapshot(G01ScenarioTests.Fixture(), world);
        string? key = reason is null ? null : "ui.command-reason." + reason;
        var entry = MapCommandMenu.Settlement(view, 1).Single(g => g.Id == "facilities").Children.Single(c => c.Id == kind);
        Assert.Equal(key, entry.UnavailableReasonKey);
        Assert.Equal(reason is null, entry.Enabled);
        Assert.Equal(key, kind == "patrol" ? PatrolDraft.UnavailableReasonKey(view, 1) : FacilityCommandDraft.UnavailableReasonKey(view, 1, kind));
    }

    private static WorldSnapshot City(WorldSnapshot world, Func<SettlementState, SettlementState> update) =>
        world with { Settlements = world.Settlements.Select(s => s.Id == 1 ? update(s) : s).ToArray() };
    private static WorldSnapshot Funds(WorldSnapshot world, uint money, uint food = 2000) =>
        world with { Corps = world.Corps.Select(c => c with { Treasury = new(money, food) }).ToArray() };

    [Fact]
    public void CommonReasonsPrecedeActivityLimitsForEveryFacilityCommand()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 7);
        var world = City(Funds(session.Snapshot, 0, 0), s => s with { ConfusionDaysRemaining = 1,
            PatrolUsed = true, CommerceUsed = true, CultivationUsed = true, RepairUsed = true,
            RecruitUsed = true, TrainUsed = true, FoodTradeUsed = true });
        world = world with { Officers = world.Officers.Select(o => o with { Acted = true }).ToArray() };
        foreach (string kind in Kinds) Check(world, kind, "confused");
        world = City(world, s => s with { ConfusionDaysRemaining = 0 });
        foreach (string kind in Kinds) Check(world, kind, "no-officer");
    }

    [Theory]
    [InlineData("patrol", "public-order-max")]
    [InlineData("commerce", "commerce-max")]
    [InlineData("cultivate", "cultivation-max")]
    [InlineData("repair", "durability-max")]
    public void PaidDevelopmentChecksFundsBeforeUsedAndMaximum(string kind, string maximum)
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 7);
        var world = City(Funds(session.Snapshot, 49), s => s with { PatrolUsed = true, CommerceUsed = true,
            CultivationUsed = true, RepairUsed = true, PublicOrder = 1000, Commerce = s.MaxCommerce,
            Cultivation = s.MaxCultivation, Durability = s.MaxDurability });
        Check(world, kind, "funds");
        world = Funds(world, 50);
        Check(world, kind, "already-used");
        world = City(world, s => s with { PatrolUsed = false, CommerceUsed = false, CultivationUsed = false, RepairUsed = false });
        Check(world, kind, maximum);
        world = City(world, s => s with { PublicOrder = 999, Commerce = s.MaxCommerce - 1,
            Cultivation = s.MaxCultivation - 1, Durability = s.MaxDurability - 1 });
        Check(world, kind, null);
    }

    [Fact]
    public void RecruitmentUsesCapacityThenItsOwnUsedPopulationAndFundsReasons()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 7);
        var world = City(Funds(session.Snapshot, 0), s => s with { Soldiers = s.SoldierCapacity, RecruitUsed = true, DraftPopulation = 0 });
        Check(world, "recruit", "soldiers-max");
        world = City(world, s => s with { Soldiers = s.SoldierCapacity - 1 });
        Check(world, "recruit", "recruit-used");
        world = City(world, s => s with { RecruitUsed = false });
        Check(world, "recruit", "no-recruits");
        world = City(world, s => s with { DraftPopulation = 1 });
        Check(world, "recruit", "recruit-no-funds");
        Check(Funds(world, 1), "recruit", null);
    }

    [Fact]
    public void TrainingChecksSoldiersBeforeUsedAndMoraleWithoutChargingFunds()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 7);
        var world = City(Funds(session.Snapshot, 0), s => s with { Soldiers = 0, TrainUsed = true, Morale = 100 });
        Check(world, "train", "no-soldiers");
        world = City(world, s => s with { Soldiers = 1 });
        Check(world, "train", "already-used");
        world = City(world, s => s with { TrainUsed = false });
        Check(world, "train", "morale-max");
        Check(City(world, s => s with { Morale = 99 }), "train", null);
    }

    [Fact]
    public void BuyingChecksMoneyAndStorageBeforeMerchantAndSharedTradeUsage()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 7);
        var world = City(Funds(session.Snapshot, 0, TreasuryRules.Capacity), s => s with { MerchantPresent = false, FoodTradeUsed = true });
        Check(world, "buy", "no-funds");
        world = Funds(world, 1, TreasuryRules.Capacity);
        Check(world, "buy", "food-max");
        world = Funds(world, 1, TreasuryRules.Capacity - 1);
        Check(world, "buy", "merchant-absent");
        world = City(world, s => s with { MerchantPresent = true });
        Check(world, "buy", "trade-used");
        Check(City(world, s => s with { FoodTradeUsed = false }), "buy", null);
    }

    [Fact]
    public void SellingDistinguishesEmptyFoodFromLessThanOneUnitOfProceeds()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 7);
        var world = City(Funds(session.Snapshot, TreasuryRules.Capacity, 0), s => s with { MerchantPresent = false, FoodTradeUsed = true });
        world = world with { Simulation = world.Simulation with { FoodExchangeRate = 7 } };
        Check(world, "sell", "no-food");
        world = Funds(world, TreasuryRules.Capacity, 6);
        Check(world, "sell", "insufficient-food");
        world = Funds(world, TreasuryRules.Capacity, 7);
        Check(world, "sell", "money-max");
        world = Funds(world, TreasuryRules.Capacity - 1, 7);
        Check(world, "sell", "merchant-absent");
        world = City(world, s => s with { MerchantPresent = true });
        Check(world, "sell", "trade-used");
        Check(City(world, s => s with { FoodTradeUsed = false }), "sell", null);
    }
}
