using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class TrapGeographyTests
{
    private static MilitaryDefinition Definition() => new(7, 7,
        Enumerable.Repeat(100, 49).ToImmutableArray(), [new(100, 1, null), new(200, 1, null)], [], [])
    {
        Elevations = Enumerable.Repeat(0, 49).ToImmutableArray(),
        TrapTerrains = [new(100, 700, false), new(200, null, true)],
        HigherGroundTrap = new(800, 3),
        Tactics = [new(600, 7, TacticEffect.TrapSelection, 0, 0, 0, true, false, []),
            new(700, 7, TacticEffect.Trap, 10, 9, 0, false, false, []),
            new(800, 7, TacticEffect.Trap, 10, 9, 0, false, false, [])]
    };

    [Theory]
    [InlineData(3, 2, 800)]
    [InlineData(3, 3, 700)]
    [InlineData(4, 0, 700)]
    public void NeighborEditsChangeSelectionWithoutEditingTarget(int x, int y, int expected)
    {
        var original = Definition();
        var target = MapCoordinates.Center(new(0, 0));
        int neighbor = y * original.Width + x;
        var rock = original with { TerrainIds = original.TerrainIds.SetItem(neighbor, 200) };
        Assert.Equal(700, new MilitaryRulebook(rock).TrapAt(target)!.Id);
        var raised = rock with { Elevations = rock.Elevations.SetItem(neighbor, 1) };
        Assert.Equal(expected, new MilitaryRulebook(raised).TrapAt(target)!.Id);
        Assert.Equal(700, new MilitaryRulebook(raised with { TerrainIds = original.TerrainIds }).TrapAt(target)!.Id);
        Assert.Equal(700, new MilitaryRulebook(original).TrapAt(target)!.Id);
    }

    [Fact]
    public void RulesAreIndependentOfSourceIdsAndSupportEmptyTerrainAndCustomRadius()
    {
        var definition = Definition() with { HigherGroundTrap = new(800, 1) };
        definition = definition with { TerrainIds = definition.TerrainIds.SetItem(0, 200).SetItem(2, 200),
            Elevations = definition.Elevations.SetItem(2, 5) };
        Assert.Null(new MilitaryRulebook(definition).TrapAt(MapCoordinates.Center(new(0, 0))));
        Assert.Equal(800, new MilitaryRulebook(definition with { HigherGroundTrap = new(800, 2) })
            .TrapAt(MapCoordinates.Center(new(0, 0)))!.Id);
        Assert.Equal(700, new MilitaryRulebook(definition).TrapAt(MapCoordinates.Center(new(6, 6)))!.Id);
    }

    [Fact]
    public void RejectsIncompleteGeographyAndInvalidReferences()
    {
        var definition = Definition();
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { Elevations = [0] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { TrapTerrains = [] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { TrapTerrains = [new(100, 600, false), new(200, null, true)] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { HigherGroundTrap = new(999, 3) }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { HigherGroundTrap = new(800, -1) }));
    }
}
