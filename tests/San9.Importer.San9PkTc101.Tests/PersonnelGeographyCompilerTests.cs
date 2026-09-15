using San9.Core.Domain;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class PersonnelGeographyCompilerTests
{
    [Theory]
    [InlineData(0, TerritoryExits.All)]
    [InlineData(2, TerritoryExits.East | TerritoryExits.South | TerritoryExits.West)]
    [InlineData(8, TerritoryExits.North | TerritoryExits.South | TerritoryExits.West)]
    [InlineData(32, TerritoryExits.North | TerritoryExits.East | TerritoryExits.West)]
    [InlineData(128, TerritoryExits.North | TerritoryExits.East | TerritoryExits.South)]
    [InlineData(0x1FE, TerritoryExits.None)]
    [InlineData(0x401, TerritoryExits.All)]
    public void TerritoryExitConversionPreservesCardinalDirectionsOnly(ushort flags, TerritoryExits expected) =>
        Assert.Equal(expected, PersonnelGeographyCompiler.TerritoryExits(flags));

    private static San9ScenarioSquareRecord Square(int id, ushort region, ushort flags = 0, byte terrain = 0) =>
        new(id, (ushort)id, flags, region, 0, 0, 0, terrain, 0, 0, 0, 0);

    [Theory]
    [InlineData(8, false)]
    [InlineData(2, true)]
    [InlineData(0x1FE, false)]
    public void HorizontalConnectionsRespectSourceDirectionRatherThanAnyBlockingBit(ushort flags, bool forward)
    {
        var connections = PersonnelGeographyCompiler.CityConnections([Square(0, 1, flags), Square(1, 2)], 2, 1,
            new Dictionary<int, int> { [1] = 1, [2] = 2 }, new HashSet<int>());
        Assert.Equal(forward, connections.Contains(new(1, 2)));
        Assert.Equal(flags != 0x1FE, connections.Contains(new(2, 1)));
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(15, false)]
    [InlineData(16, false)]
    [InlineData(9, true)]
    [InlineData(12, true)]
    public void WaterFamilyNeedsAPortRegionAtEitherEnd(byte terrain, bool withoutPort)
    {
        var squares = new[] { Square(0, 1), Square(1, 2, terrain: terrain) };
        var regions = new Dictionary<int, int> { [1] = 1, [2] = 2 };
        Assert.Equal(withoutPort ? 2 : 0, PersonnelGeographyCompiler.CityConnections(squares, 2, 1, regions, new HashSet<int>()).Length);
        Assert.Equal(2, PersonnelGeographyCompiler.CityConnections(squares, 2, 1, regions, new HashSet<int> { 2 }).Length);
    }

    [Fact]
    public void AggregationDeduplicatesCityEdgesAndDoesNotJoinAcrossRows()
    {
        var squares = new[] { Square(0, 1), Square(1, 11), Square(2, 2), Square(3, 2) };
        var result = PersonnelGeographyCompiler.CityConnections(squares, 2, 2,
            new Dictionary<int, int> { [1] = 1, [11] = 1, [2] = 2 }, new HashSet<int>());
        Assert.Equal(new[] { new CityConnection(1, 2), new(2, 1) }, result.ToArray());
        squares = [Square(0, 1, 0x1FE), Square(1, 11, 32), Square(2, 2, 2), Square(3, 2, 0x1FE)];
        Assert.Empty(PersonnelGeographyCompiler.CityConnections(squares, 2, 2,
            new Dictionary<int, int> { [1] = 1, [11] = 1, [2] = 2 }, new HashSet<int>()));
    }
}
