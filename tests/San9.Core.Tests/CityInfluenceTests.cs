using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class CityInfluenceTests
{
    private static SettlementState City(int? owner = 1) => new(9000, owner, 0, 100, 100, 0, 0, 0, 100, 0, 100);
    private static MilitaryRulebook Rules(IEnumerable<MapCell>? blocked = null, BattlefieldPlace[]? extra = null,
        FacilityRule[]? facilities = null, int radius = 8) => new(new(30, 30, [.. Enumerable.Repeat(0, 900)],
        [new(0, 1, 1)], [], [new(9000, MapCoordinates.Center(new(10, 10)), new(5, 5))
            { FacilityTypeId = facilities is null ? null : 9400 }, .. extra ?? []])
    {
        CityInfluence = new(radius, 10), Geography = new(5, [.. blocked ?? []]), Facilities = facilities is null ? [] :
            [new(9400, 0, 100, new(5, 5), 0, false, true, true, 10000, null, null, null, []), .. facilities]
    });

    [Theory]
    [InlineData(12, 12, true)] // Full 5x5 urban footprint, including the outer corner.
    [InlineData(20, 10, true)]
    [InlineData(21, 10, false)]
    [InlineData(16, 16, true)]
    [InlineData(17, 16, false)]
    public void RangeStartsAtFullFootprintAndCountsFourNeighborSteps(int x, int y, bool expected)
    {
        Assert.Equal(expected, Rules().HasCityInfluence(1, MapCoordinates.Center(new(x, y)), [City()]));
        Assert.Equal(expected, Rules().CityInfluenceCells(City(), [City()]).Contains(new(x, y)));
    }

    [Fact]
    public void BarriersCountDetoursAndSeedCellsRemainInside()
    {
        var wall = Enumerable.Range(0, 30).Select(y => new MapCell(13, y)).ToArray();
        var closed = Rules(wall.Append(new(12, 12)));
        Assert.False(closed.HasCityInfluence(1, MapCoordinates.Center(new(14, 10)), [City()]));
        Assert.True(closed.HasCityInfluence(1, MapCoordinates.Center(new(12, 12)), [City()]));
        var gate = Rules(wall.Where(c => c.Y != 15));
        Assert.True(gate.HasCityInfluence(1, MapCoordinates.Center(new(14, 12)), [City()])); // Detour 8.
        Assert.False(gate.HasCityInfluence(1, MapCoordinates.Center(new(14, 11)), [City()])); // Detour 9.
    }

    [Fact]
    public void OwnershipAndDestroyedStateAreReadOnEveryQuery()
    {
        var rules = Rules(); var position = MapCoordinates.Center(new(16, 10));
        Assert.True(rules.HasCityInfluence(1, position, [City()]));
        Assert.False(rules.HasCityInfluence(1, position, [City(2)]));
        Assert.True(rules.HasCityInfluence(2, position, [City(2)]));
        Assert.False(rules.HasCityInfluence(1, position, [City(null)]));
        Assert.False(rules.HasCityInfluence(1, position, [City() with { IsDestroyed = true }]));
        Assert.False(rules.HasCityInfluence(1, position, [City() with { Kind = SettlementKind.Facility }]));
    }

    [Fact]
    public void CurrentFacilitiesBlockUntilDestroyedAndUnfinishedConstructionDoesNotBlock()
    {
        var type = new FacilityRule(9500, 0, 100, new(1, 30), 0, true, false, false, 1, null, null, null, []);
        var rules = Rules(facilities: [type]);
        var barrier = new SettlementState(9001, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)
            { Kind = SettlementKind.Facility, Facility = new(9500, MapCoordinates.Center(new(13, 15)), null) };
        var position = MapCoordinates.Center(new(14, 10));
        Assert.False(rules.HasCityInfluence(1, position, [City(), barrier]));
        Assert.True(rules.HasCityInfluence(1, position, [City(), barrier with { IsDestroyed = true }]));
        Assert.True(rules.HasCityInfluence(1, position, [City(), barrier with { Facility = barrier.Facility with { BuilderUnitId = 1 } }]));
    }

    [Fact]
    public void NullProfileAndZeroRadiusHaveExplicitMeaning()
    {
        Assert.True(Rules(radius: 0).HasCityInfluence(1, MapCoordinates.Center(new(12, 12)), [City()]));
        Assert.False(Rules(radius: 0).HasCityInfluence(1, MapCoordinates.Center(new(13, 12)), [City()]));
        var rules = new MilitaryRulebook(new(1, 1, [0], [new(0, 1, null)], [], []));
        Assert.False(rules.HasCityInfluence(1, new(2, 2), []));
        Assert.Throws<ArgumentException>(() => Rules(radius: -1));
        Assert.Throws<ArgumentException>(() => Rules([new(30, 0)]));
    }

    [Fact]
    public void SourceRangeAgreesWithCombatQueryAcrossDetoursButDoesNotIncludeAnotherCity()
    {
        var city = City();
        var second = city with { Id = 9001 };
        var rules = Rules(Enumerable.Range(0, 30).Where(y => y != 15).Select(y => new MapCell(13, y)),
            [new(9001, MapCoordinates.Center(new(25, 25)), new(1, 1))]);
        var range = rules.CityInfluenceCells(city, [city, second]);
        Assert.DoesNotContain(new MapCell(25, 25), range);
        Assert.True(rules.HasCityInfluence(1, MapCoordinates.Center(new(25, 25)), [city, second]));
        for (int y = 0; y < 30; y++)
        for (int x = 0; x < 30; x++)
            Assert.Equal(rules.HasCityInfluence(1, MapCoordinates.Center(new(x, y)), [city, second with { OwnerId = 2 }]), range.Contains(new(x, y)));
        Assert.Empty(rules.CityInfluenceCells(city with { Kind = SettlementKind.Facility }, [city]));
    }
}
