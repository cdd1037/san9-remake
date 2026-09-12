using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MapCoordinatesTests
{
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
