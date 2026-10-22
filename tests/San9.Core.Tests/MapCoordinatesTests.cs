using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MapCoordinatesTests
{
    [Theory]
    [InlineData(0, -100, WorldDirection.North)]
    [InlineData(100, -100, WorldDirection.NorthEast)]
    [InlineData(100, 0, WorldDirection.East)]
    [InlineData(100, 41, WorldDirection.East)]
    [InlineData(100, 42, WorldDirection.SouthEast)]
    [InlineData(100, 100, WorldDirection.SouthEast)]
    [InlineData(41, 100, WorldDirection.South)]
    [InlineData(42, 100, WorldDirection.SouthEast)]
    [InlineData(0, 100, WorldDirection.South)]
    [InlineData(-100, 100, WorldDirection.SouthWest)]
    [InlineData(-100, 0, WorldDirection.West)]
    [InlineData(-100, -100, WorldDirection.NorthWest)]
    public void FacingUsesEightSectorsOfTheNetDisplacement(int dx, int dy, WorldDirection expected)
    {
        Assert.Equal(expected, WorldDirections.Between(new(100, 100), new(100 + dx, 100 + dy)));
        Assert.Equal((WorldDirection)(((int)expected + 4) % 8),
            WorldDirections.Between(new(100 + dx, 100 + dy), new(100, 100)));
    }

    [Fact]
    public void FacingHandlesWideCoordinatesAndHasNoDirectionForZeroDisplacement()
    {
        Assert.Null(WorldDirections.Between(new(10, 20), new(10, 20)));
        Assert.Equal(WorldDirection.NorthWest, WorldDirections.Between(new(int.MaxValue, int.MaxValue), new(0, 0)));
        Assert.Equal(WorldDirection.East, WorldDirections.Between(new(int.MinValue, 0), new(int.MaxValue, 1)));
    }

    [Theory]
    [InlineData(0, 0, 2, 2)]
    [InlineData(184, 26, 922, 132)]
    [InlineData(199, 199, 997, 997)]
    [InlineData(1200, 300, 6002, 1502)]
    public void CellCentersHaveExplicitFineUnitsAndNoOriginalMapSizeLimit(int x, int y, int fineX, int fineY)
    {
        var cell = new MapCell(x, y);
        var position = MapCoordinates.Center(cell);
        Assert.Equal(new WorldPosition(fineX, fineY), position);
        Assert.Equal(cell, MapCoordinates.Cell(position));
        Assert.Equal(new MapCell(x, y), MapCoordinates.Cell(new(fineX + 2, fineY - 2)));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 10)]
    [InlineData(0, 0, 30, 0, 1, 10)]
    [InlineData(0, 0, 30, 1, 1, 10)]
    [InlineData(0, 0, 31, 0, 2, 10)]
    [InlineData(2, 2, 182, 242, 10, 10)]
    [InlineData(2, 2, 182, 243, 10, 10)]
    [InlineData(2, 2, 182, 244, 11, 20)]
    public void TravelUsesIntegerEuclideanFineDistanceAndSearchRoundsDays(int x, int y, int toX, int toY, int days, int searchDays)
    {
        var rules = new PersonnelRulebook([new(1, new(x, y)), new(2, new(toX, toY))]);
        Assert.Equal(days, rules.JourneyDays(1, 2, PersonnelJourneyKind.Recruit));
        Assert.Equal(days, rules.JourneyDays(1, 2, PersonnelJourneyKind.Move));
        Assert.Equal(searchDays, rules.JourneyDays(1, 2, PersonnelJourneyKind.Search));
        Assert.Equal(days, rules.JourneyDays(2, 1, PersonnelJourneyKind.Move));
    }

    [Fact]
    public void DistanceUsesWideIntegerArithmeticAndFinePositionsSurvivePersistence()
    {
        Assert.Equal(3037000498L, MapCoordinates.Distance(new(0, 0), new(int.MaxValue, int.MaxValue)));
        var unit = new UnitState(1, 1, new(1003, 2001), 100);
        Assert.True(unit.IsValid);
        var json = CanonicalJson.Text(unit);
        var restored = System.Text.Json.JsonSerializer.Deserialize<UnitState>(json, CanonicalJson.Options)!;
        Assert.Equal(unit, restored);
        Assert.Equal(new MapCell(200, 400), restored.Cell);
    }
}
