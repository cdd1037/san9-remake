using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class TerritoryConnectionTests
{
    private static MilitaryDefinition Definition(int width, params int?[] regions) => new(width, regions.Length / width,
        regions.Select(_ => 0).ToImmutableArray(), [new(0, 1, null), new(1, null, 1) { IsWater = true }], [], [])
    {
        RegionIds = regions.ToImmutableArray(),
        TerritoryExits = regions.Select(_ => TerritoryExits.All).ToImmutableArray()
    };

    [Fact]
    public void ConnectionsAreDirectedAndUseRegionsInsteadOfCitiesOrRecursiveReachability()
    {
        var definition = Definition(3, 101, 702, 9901) with
        { TerritoryExits = [TerritoryExits.None, TerritoryExits.All, TerritoryExits.All] };
        Assert.Equal(new TerritoryConnection[] { new(702, 101), new(702, 9901), new(9901, 702) },
            new MilitaryRulebook(definition).TerritoryConnections([]));
    }

    [Fact]
    public void CardinalEdgesDoNotWrapRowsOrCrossBlockedOrUnassignedCells()
    {
        var definition = Definition(2, 1, 2, 3, null) with
        { Geography = new(1, [new(0, 0)]) };
        Assert.Empty(new MilitaryRulebook(definition).TerritoryConnections([]));
        definition = Definition(2, 1, 1, 2, 2);
        Assert.Equal(new TerritoryConnection[] { new(1, 2), new(2, 1) }, new MilitaryRulebook(definition).TerritoryConnections([]));
    }

    [Fact]
    public void WaterBordersReactToCurrentHarborsWithoutRebuildingTheRulebook()
    {
        var definition = Definition(2, 1, 2) with
        {
            TerrainIds = [0, 1],
            Places = [new(2002, MapCoordinates.Center(new(1, 0)), new(1, 1)) { FacilityTypeId = 92 }],
            Facilities = [new(92, 0, 100, new(1, 1), 0, false, true, true, 1000, null, null, null, []) { IsHarbor = true }]
        };
        var rules = new MilitaryRulebook(definition);
        var harbor = new SettlementState(2002, 9, 0, 100, 100, 50, 0, 0, 0, 0, 0) { Kind = SettlementKind.Facility };
        Assert.Empty(rules.TerritoryConnections([]));
        Assert.Equal(2, rules.TerritoryConnections([harbor]).Length);
        Assert.Empty(rules.TerritoryConnections([harbor with { IsDestroyed = true }]));
        Assert.Equal(2, rules.TerritoryConnections([harbor with { OwnerId = 18 }]).Length);
    }

    [Fact]
    public void AlternativeLandBorderDoesNotAcquireAnUnnecessaryHarborRequirement()
    {
        var definition = Definition(2, 1, 2, 1, 2) with { TerrainIds = [0, 1, 0, 0] };
        Assert.Equal(new TerritoryConnection[] { new(1, 2), new(2, 1) }, new MilitaryRulebook(definition).TerritoryConnections([]));
    }

    [Fact]
    public void InvalidTopologyIsRejectedAndAbsentTopologyDoesNotInventConnections()
    {
        var definition = Definition(2, 1, 2);
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { TerritoryExits = [(TerritoryExits)16, TerritoryExits.All] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { TerritoryExits = [TerritoryExits.All] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { TerritoryExits = default }));
        Assert.Empty(new MilitaryRulebook(definition with { TerritoryExits = [] }).TerritoryConnections([]));
    }
}
