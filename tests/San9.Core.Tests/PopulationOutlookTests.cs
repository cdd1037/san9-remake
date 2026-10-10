using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PopulationOutlookTests
{
    [Fact]
    public void ForecastMatchesQuarterlyGrowthAndDormantDefinitionsDoNotChangeIt()
    {
        var world = WorldState.Create(new(190, 1, 1), 1, [],
            [new(1, null, 0, 100, 100, 50, 800, 0, 100, 0, 100) { Population = 200399 },
             new(2, null, 500000, 100, 100, 50, 1000, 0, 100, 0, 100) { IsDormant = true, Population = 900000 }], []);
        var snapshot = world.CreateSnapshot();
        var city = snapshot.Settlements.Single(s => s.Id == 1);
        long predicted = PopulationOutlook.DraftGrowth(snapshot, city);
        Assert.Equal(600, predicted);
        Assert.Equal(predicted, PopulationOutlook.DraftGrowth(snapshot with { Settlements = [city] }, city));
        var mutation = world.BeginMutation();
        mutation.SetClock(new(190, 4, 1));
        EconomicCycle.Apply(ResolutionStage.QuarterUpdate, mutation, EconomyRulebook.Empty, []);
        Assert.Equal(predicted, (long)mutation.Settlements[1].DraftPopulation - city.DraftPopulation);
        Assert.Equal(snapshot.Settlements.Single(s => s.Id == 2), mutation.AllSettlements[2]);
        Assert.Equal(0, PopulationOutlook.DraftGrowth(snapshot, snapshot.Settlements.Single(s => s.Id == 2)));
    }
}
