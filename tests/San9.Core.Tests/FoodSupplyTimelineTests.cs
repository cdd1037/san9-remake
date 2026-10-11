using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class FoodSupplyTimelineTests
{
    [Fact]
    public void FutureFoodCannotPayForTheDaysBeforeItsDelivery()
    {
        Assert.Equal(100, FoodSupplyForecast.RequiredFood(10, 30, [new(10, 0, 1000)]));
        Assert.Equal(110, FoodSupplyForecast.RequiredFood(10, 30, [new(11, 0, 1000)]));
    }

    [Fact]
    public void SameAccountTroopTransferIncludesRoundingAfterArrival()
    {
        long amount = FoodSupplyForecast.TransferExpenditure(12000, 1000, 5342, 12, 180);
        long before = FoodSupplyForecast.GarrisonConsumption(12000) + FoodSupplyForecast.GarrisonConsumption(1000);
        long moving = FoodSupplyForecast.GarrisonConsumption(6658) + FoodSupplyForecast.GarrisonConsumption(1000) + FoodSupplyForecast.FieldConsumption(5342);
        long arrived = FoodSupplyForecast.GarrisonConsumption(6658) + FoodSupplyForecast.GarrisonConsumption(6342);
        Assert.Equal(moving * 12 + arrived * 168 - before * 180, amount);
    }

    [Fact]
    public void RecruitmentStopsAtTheSupplyLimitIncludingWoundedRounding()
    {
        uint count = FoodSupplyForecast.SustainableRecruitment(1000, 499, 5000, 180, 180);
        Assert.Equal(501u, count);
        long before = FoodSupplyForecast.GarrisonConsumption(1000, 499);
        Assert.True((FoodSupplyForecast.GarrisonConsumption(1000 + count, 499) - before) * 180 <= 180);
        Assert.True((FoodSupplyForecast.GarrisonConsumption(1001 + count, 499) - before) * 180 > 180);
    }
}
