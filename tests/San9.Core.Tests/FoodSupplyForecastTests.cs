using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class FoodSupplyForecastTests
{
    [Theory]
    [InlineData(1, 1, 1, 1, 360)]
    [InlineData(6, 21, 1, 1, 20)]
    [InlineData(6, 21, 6, 21, 740)]
    public void SupplyWindowIncludesOnlyReliableHarvest(int month, int day, int startMonth, int startDay, long required)
    {
        var forecast = FoodSupplyForecast.Evaluate(new(new(190, month, day), new(190, startMonth, startDay), 100, 2, 500, null));
        Assert.Equal(required, forecast.Expenditure);
        Assert.Equal(required <= 100, forecast.Sufficient);
        Assert.Equal(Math.Max(0, 100 - required) + 500, forecast.Forecast);
    }
}
