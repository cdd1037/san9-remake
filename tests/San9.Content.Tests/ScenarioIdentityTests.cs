using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Content.Tests;

public sealed class ScenarioIdentityTests
{
    private static ScenarioInitialState Fixture() => new(22, "expanded", 190, 1, 1, 9000,
        [new(9000, "force", 0, 100, 100, 0)],
        Enumerable.Range(0, 75).Select(i => new SettlementInitialState(1000 + i, $"city.{i}", 9000,
            i, 0, 0, 100, 100, 0, ["merchant"]) { Kind = SettlementKind.City }).ToArray(),
        new() { [2000] = "region" }, new() { [2000] = 1074 });

    [Fact]
    public void CityIdentityAndCountAreIndependentOfOriginalSlots()
    {
        Fixture().Validate();
    }

    [Fact]
    public void RegionMustReferenceAnExistingCityRatherThanANameEntry()
    {
        var scenario = Fixture();
        Assert.Throws<InvalidDataException>(() => (scenario with { RegionCityIds = new() { [2000] = 2000 } }).Validate());
        var settlements = scenario.Settlements.ToArray();
        settlements[^1] = settlements[^1] with { Kind = SettlementKind.Facility, Icons = [] };
        Assert.Throws<InvalidDataException>(() => (scenario with { Settlements = settlements }).Validate());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public void FacilityCannotAcquireCityIconsThroughItsId(int id)
    {
        var scenario = Fixture();
        var settlements = scenario.Settlements.ToArray();
        settlements[0] = settlements[0] with { Id = id, Kind = SettlementKind.Facility };
        Assert.Throws<InvalidDataException>(() => (scenario with { Settlements = settlements }).Validate());
    }
}
