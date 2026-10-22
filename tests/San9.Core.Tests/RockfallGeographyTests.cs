using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class RockfallGeographyTests
{
    private static SettlementState Source() => new(9000, 1, 1000, 100, 100, 100, 0, 0, 100, 0, 100);
    private static MilitaryRulebook Rules(int width = 9, int height = 1, int[]? terrain = null,
        int[]? elevations = null, MapCell[]? blocked = null, int footprint = 1,
        bool rockfall = true, int unitWidth = 1) => new(new(width, height,
        [.. terrain ?? new int[width * height]],
        [new(0, 1, null), new(1, 1, null) { StopsRockfall = true }, new(2, null, 1) { IsWater = true }],
        [new(7000, 1, 0, 10000, 1, new(unitWidth, 1), TravelMode.Land) { Combat = new(10, 10, 0, 10, 50) }],
        [new(9000, MapCoordinates.Center(new(1, height / 2)), new(footprint, 1))
            { Combat = new(10, 10, 0, 10, 50), Attack = new(10, false) { ElevationBonusPerLevel = 5, MaximumElevationBonus = 10 }, FacilityTypeId = 8000 }])
    {
        Geography = new(5, [.. blocked ?? []]), Rockfall = new(5, 25), Elevations = [.. elevations ?? new int[width * height]],
        Facilities = [new(8000, 0, 100, new(1, 1), 0, true, true, true, 100000, null, null, null, [])
            { Combat = new(10, 10, 0, 10, 50), Attack = new(10, false), InnateAttack = new(rockfall, 0) }]
    });

    [Theory]
    [InlineData(1, false)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public void FiveWavesExcludeSourceAndIncludeDiagonalNeighbors(int x, bool expected)
    {
        var rules = Rules(9, 13);
        Assert.Equal(expected, rules.RockfallCovers(Source(), MapCoordinates.Center(new(x, 6 + x - 1)), [Source()]));
    }

    [Fact]
    public void ReturnedPathDetoursAroundBarriersAndOnlyContainsCoveredCells()
    {
        var rules = Rules(7, 5, blocked: [new(3, 2)]);
        var target = MapCoordinates.Center(new(5, 2));
        var path = rules.RockfallPath(Source(), target, [Source()]);
        Assert.NotEmpty(path);
        Assert.Equal(new MapCell(5, 2), path[^1]);
        Assert.DoesNotContain(new MapCell(3, 2), path);
        foreach (var cell in path.Skip(1)) Assert.True(rules.RockfallCovers(Source(), MapCoordinates.Center(cell), [Source()]));
        for (int i = 1; i < path.Count; i++)
            Assert.InRange(Math.Max(Math.Abs(path[i].X - path[i - 1].X), Math.Abs(path[i].Y - path[i - 1].Y)), 1, 1);
    }

    [Fact]
    public void FullGeographicFootprintSeedsThePropagation()
    {
        var rules = Rules(10, footprint: 3);
        Assert.True(rules.RockfallCovers(Source(), MapCoordinates.Center(new(7, 0)), [Source()]));
        Assert.False(rules.RockfallCovers(Source(), MapCoordinates.Center(new(8, 0)), [Source()]));
    }

    [Fact]
    public void UphillWaterAndBarriersBlockWhileStopTerrainIsCoveredOnlyAsALeaf()
    {
        var target = MapCoordinates.Center(new(4, 0));
        Assert.False(Rules(elevations: [0, 0, 0, 1, 0, 0, 0, 0, 0]).RockfallCovers(Source(), target, [Source()]));
        Assert.True(Rules(elevations: [3, 3, 2, 1, 0, 0, 0, 0, 0]).RockfallCovers(Source(), target, [Source()]));
        foreach (int terrain in new[] { 1, 2 })
        {
            var rules = Rules(terrain: [0, 0, 0, terrain, 0, 0, 0, 0, 0]);
            Assert.False(rules.RockfallCovers(Source(), target, [Source()]));
            Assert.Equal(terrain == 1, rules.RockfallCovers(Source(), MapCoordinates.Center(new(3, 0)), [Source()]));
        }
        Assert.False(Rules(blocked: [new(3, 0)]).RockfallCovers(Source(), target, [Source()]));
        Assert.True(Rules(terrain: [0, 1, 0, 0, 0, 0, 0, 0, 0]).RockfallCovers(Source(), target, [Source()]));
    }

    [Fact]
    public void ConstructionBlocksImmediatelyAndDestructionInvalidatesCoverageWithoutACache()
    {
        var rules = Rules();
        var building = Source() with { Id = 9001, Facility = new(8000, MapCoordinates.Center(new(3, 0)), 77) };
        var target = MapCoordinates.Center(new(4, 0));
        Assert.False(rules.RockfallCovers(Source(), target, [Source(), building]));
        Assert.True(rules.RockfallCovers(Source(), target, [Source(), building with { IsDestroyed = true }]));
        Assert.False(rules.RockfallCovers(Source() with { IsDestroyed = true }, target, []));
        Assert.False(rules.RockfallCovers(building, target, []));
    }

    [Fact]
    public void SpecialRangeReplacesElevationRangeAndFallsBackOutsideCoverage()
    {
        var rules = Rules(elevations: [2, 2, 0, 0, 0, 0, 0, 0, 0]);
        Assert.Equal(25, rules.FacilityAttackRange(Source(), MapCoordinates.Center(new(6, 0)), [Source()]));
        Assert.Equal(20, rules.FacilityAttackRange(Source(), MapCoordinates.Center(new(7, 0)), [Source()]));
    }

    [Theory]
    [InlineData(true, 6, 1, true)]
    [InlineData(true, 7, 1, false)]
    [InlineData(false, 5, 1, true)]
    [InlineData(false, 6, 1, false)]
    [InlineData(false, 6, 11, true)]
    public void FacilitySelectionPreservesSpecialRangeElevationFootprintsAndIdentityTies(
        bool rockfall, int targetCell, int unitWidth, bool reachable)
    {
        var rules = Rules(elevations: [2, 2, 0, 0, 0, 0, 0, 0, 0], rockfall: rockfall, unitWidth: unitWidth);
        var target = new UnitState(3, 2, MapCoordinates.Center(new(targetCell, 0)), 1000) { FormationId = 7000 };
        var world = WorldState.Create(new(190, 1, 1), 42, [new(1, 100), new(2, 100)], [Source()], [],
            units: [target, target with { Id = 2 }],
            corps: [new(1, 1, null, new(0, 0)) { IsRoot = true }, new(2, 2, null, new(0, 0)) { IsRoot = true }]);
        var selected = MilitaryEngagements.FacilityTarget(world.BeginMutation(), Source(), rules);
        Assert.Equal(reachable ? 2 : (int?)null, selected?.Id);
    }
}
