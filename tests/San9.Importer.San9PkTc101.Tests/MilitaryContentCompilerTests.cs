using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class MilitaryContentCompilerTests
{
    [Fact]
    public void KeepsEveryTerrainAndFormationAndSeparatesHardBlockingFromTerrain()
    {
        var squares = Enumerable.Range(0, 40000).Select(i =>
            new San9ScenarioSquareRecord(i, (ushort)i, 0, 0, 0, 0, 0, 3, 0, 0, 0, 0)).ToArray();
        squares[3] = squares[3] with { SquareFlags = 2, TerrainCode = 2 };
        squares[4] = squares[4] with { SquareFlags = 1024, TerrainCode = 15 };
        var terrains = Enumerable.Range(0, 17).Select(i => new San9ScenarioTerrainRecord(i, (ushort)i, 999, 99)).ToArray();
        var formations = Enumerable.Range(0, 28).Select(i => new San9ScenarioFormationRecord(i, (ushort)i, 0, 0, 0,
            1, 1, 1, 1, 12, 1, 1, 100, 0, new byte[] { 1, 3, 4, 5, 7 }, 0)).ToArray();
        SettlementInitialState[] places = [new(5000, "city", 1, 10, 10, 100, 100, 100, 50, [])];
        var definition = MilitaryContentCompiler.Compile(squares, terrains, formations, places, [], []);
        Assert.Equal(Enumerable.Range(0, 17), definition.Terrains.Select(t => t.Id));
        Assert.Equal(Enumerable.Range(0, 28), definition.Formations.Select(f => f.Id));
        Assert.Equal(new MapCell(3, 0), Assert.Single(definition.BlockedCells));
        Assert.Equal(2, definition.TerrainIds[3]);
        Assert.Equal(15, definition.TerrainIds[4]);
        Assert.All(definition.Formations, f => Assert.Equal(300, f.MovementBudget));
        var rules = new MilitaryRulebook(definition);
        Assert.Null(rules.Map.PassageCost(MapCoordinates.Center(new(3, 0)), new(1, 1), TravelMode.Land, []));
        Assert.Equal(10, rules.Map.PassageCost(MapCoordinates.Center(new(4, 0)), new(1, 1), TravelMode.Water, []));
        Assert.Null(rules.Map.PassageCost(MapCoordinates.Center(new(4, 0)), new(1, 1), TravelMode.Land, []));
        Assert.Equal(5000, Assert.Single(definition.Places).Id);
        Assert.Throws<InvalidDataException>(() => MilitaryContentCompiler.Compile(squares, terrains[..16], formations, places, [], []));
        Assert.Throws<InvalidDataException>(() => MilitaryContentCompiler.Compile(squares, terrains, formations[..27], places, [], []));
    }
}
