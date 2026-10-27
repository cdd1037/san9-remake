using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class MilitaryCityNetworkTests
{
    private static MilitaryDefinition Map((int Id, int X, int Y)[] cities, int width = 40, int height = 25) =>
        new(width, height, Enumerable.Repeat(7, width * height).ToImmutableArray(), [new(7, 10, null)],
            [new(203, 60, 0, 100000, 5, new(5, 5), TravelMode.Land)
                { SoldierFootprints = [new(20000, new(7, 7))] }],
            cities.Select(c => new BattlefieldPlace(c.Id, MapCoordinates.Center(new(c.X, c.Y)), new(1, 1))).ToImmutableArray())
        { DefaultDeploymentFormationId = 203, Geography = new(5, []) };

    private static MilitaryCityNetwork Generate(MilitaryDefinition map, uint soldiers = 10000) =>
        MilitaryCityNetwork.Generate(new(map), map.Places.Select(p => p.Id), new(map.Formations[0].FootprintFor(soldiers), null));

    [Fact]
    public void DistantTwoCityMapDoesNotAcquireAnAbsoluteTimeCap()
    {
        var map = Map([(17, 2, 12), (9001, 37, 12)]) with { Terrains = [new(7, 100, null)] };
        var result = Generate(map);
        Assert.True(result.AreNeighbors(17, 9001));
        Assert.True(result.AreNeighbors(9001, 17));
        Assert.Equal(4, result.Diagnostics.DistanceFields);
        Assert.Equal(1, result.Diagnostics.BypassSearches);
    }

    [Fact]
    public void TerrainBarrierRejectsEvenAnOtherwiseNearestPair()
    {
        var map = Map([(17, 2, 12), (9001, 37, 12)]);
        map = map with { BlockedCells = Enumerable.Range(0, 25).Select(y => new MapCell(20, y)).ToImmutableArray() };
        var result = Generate(map);
        Assert.Empty(result.Connections);
        Assert.Equal(0, result.Diagnostics.BypassSearches);
    }

    [Fact]
    public void ReferenceFormationMustFitANarrowPassage()
    {
        var map = Map([(17, 2, 12), (9001, 37, 12)]);
        map = map with { BlockedCells = Enumerable.Range(0, 25).Where(y => y != 12).Select(y => new MapCell(20, y)).ToImmutableArray() };
        Assert.True(Generate(map).AreNeighbors(17, 9001));
        Assert.Empty(Generate(map, 20000).Connections);
    }

    [Fact]
    public void ReorderingAndRelabelingCitiesDoesNotChangeTheGeometry()
    {
        var map = Map([(11, 3, 3), (22, 35, 15), (900, 19, 9)]);
        var baseline = Generate(map);
        var ids = new Dictionary<int, int> { [11] = 80000, [22] = 3, [900] = 600 };
        var changed = map with { Places = map.Places.Reverse().Select(p => p with { Id = ids[p.Id] }).ToImmutableArray() };
        Assert.Equal(baseline.Connections.Select(c => (ids[c.FromCityId], ids[c.ToCityId])).Order(),
            Generate(changed).Connections.Select(c => (c.FromCityId, c.ToCityId)).Order());
    }

    [Fact]
    public void UniformCostScalingDoesNotChangeCalibratedFeatures()
    {
        var map = Map([(11, 3, 3), (22, 35, 15), (900, 19, 9)]);
        Assert.Equal(Generate(map).Connections.ToArray(), Generate(map with { Terrains = [new(7, 30, null)] }).Connections.ToArray());
    }

    [Fact]
    public void EquivalentShortestRoutesCanAvoidTheMiddleCity()
    {
        var result = Generate(Map([(11, 1, 1), (22, 7, 7), (900, 4, 4)], 9, 9));
        Assert.True(result.AreNeighbors(11, 22));
        Assert.True(result.Diagnostics.ClearanceSearches > 0);
    }

    [Fact]
    public void DefenseMasksSupportMoreThanSixtyFourCities()
    {
        var positions = new[] { (Id: 0, X: 2, Y: 2), (Id: 1, X: 12, Y: 2) }
            .Concat(Enumerable.Range(2, 64).Select(i => (Id: i, X: 20 + (i - 2) * 3, Y: 40))).ToArray();
        var map = Map(positions, 220, 45);
        Assert.True(Generate(map).AreNeighbors(0, 1));
        map = map with { Places = map.Places.Select(p => p.Id == 65 ? p with
            { Attack = new(100000, false), Combat = new(1, 1, 0, 1, 0) } : p).ToImmutableArray() };
        Assert.False(Generate(map).AreNeighbors(0, 1));
    }

    [Fact]
    public void TwoEndpointDefensesAreExemptFromTheirOwnLink()
    {
        var map = Map([(17, 2, 12), (9001, 37, 12)]);
        map = map with { Places = map.Places.Select(p => p with
            { Attack = new(100000, false), Combat = new(1, 1, 0, 1, 0) }).ToImmutableArray() };
        Assert.True(Generate(map).AreNeighbors(17, 9001));
    }

    [Fact]
    public void LinksAreDeterministicAndDoNotDependOnCityInputOrder()
    {
        var map = Map([(11, 3, 3), (22, 35, 15), (900, 19, 9)]);
        var rules = new MilitaryRulebook(map);
        var first = MilitaryCityNetwork.Generate(rules, [11, 22, 900]);
        var second = MilitaryCityNetwork.Generate(rules, [900, 11, 22]);
        Assert.Equal(first.Connections.ToArray(), second.Connections.ToArray());
        Assert.Equal(first.Diagnostics, second.Diagnostics);
        Assert.Throws<ArgumentException>(() => MilitaryCityNetwork.Generate(rules, [11, 11]));
    }

    [Fact]
    public void FineCoordinateCitiesCanShareAStrategicNavigationSite()
    {
        var map = Map([(17, 2, 12), (9001, 37, 12)]);
        map = map with { Places = map.Places.Add(map.Places[0] with { Id = 71, Position = new(13, 62) }) };
        var result = Generate(map);
        Assert.True(result.AreNeighbors(17, 71));
        Assert.True(result.AreNeighbors(17, 9001));
        Assert.True(result.AreNeighbors(71, 9001));
        Assert.Equal(4, result.Diagnostics.DistanceFields);
    }

    [Fact]
    public void DefaultDeploymentFormationDoesNotChangeTheReferenceModel()
    {
        var map = Map([(11, 3, 3), (22, 35, 15), (900, 19, 9)]);
        var first = MilitaryCityNetwork.Generate(new(map), map.Places.Select(p => p.Id));
        map = map with { Formations = map.Formations.Add(map.Formations[0] with { Id = 9876, MovementBudget = 100 }), DefaultDeploymentFormationId = 9876 };
        var second = MilitaryCityNetwork.Generate(new(map), map.Places.Select(p => p.Id));
        Assert.Equal(first.Connections.ToArray(), second.Connections.ToArray());
    }
}
