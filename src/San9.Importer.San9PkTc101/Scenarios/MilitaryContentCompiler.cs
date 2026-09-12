using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

/// <summary>TC101 identities are interpreted here; domain movement consumes explicit passage and budgets.</summary>
public static class MilitaryContentCompiler
{
    public static MilitaryDefinition Compile(IReadOnlyList<San9ScenarioSquareRecord> squares,
        IReadOnlyList<San9ScenarioTerrainRecord> terrains, IReadOnlyList<San9ScenarioFormationRecord> formations,
        IReadOnlyList<SettlementInitialState> settlements, IReadOnlyList<San9ScenarioZoneRecord> zones,
        IReadOnlyList<San9ScenarioFixedBuildingRecord> buildings)
    {
        if (squares.Count != San9ScenarioSquareArchive.RecordCount ||
            !squares.Select(s => s.Id).SequenceEqual(Enumerable.Range(0, squares.Count)) ||
            !terrains.Select(t => t.Id).SequenceEqual(Enumerable.Range(0, San9ScenarioTerrainArchive.RecordCount)) ||
            !formations.Select(f => f.Id).SequenceEqual(Enumerable.Range(0, San9ScenarioFormationArchive.RecordCount)))
            throw new InvalidDataException("Incomplete military source catalogs.");
        var places = settlements.Select(s =>
        {
            Footprint shape;
            if (s.Kind == SettlementKind.City) shape = new(5, 5);
            else
            {
                var zone = zones.Single(z => z.Id + 50 == s.Id);
                var type = buildings.Single(b => b.Id == zone.FixedBuildingId);
                shape = new(type.FootprintWidth, type.FootprintHeight);
            }
            return new BattlefieldPlace(s.Id, s.Position, shape);
        }).ToImmutableArray();
        var definition = new MilitaryDefinition(San9ScenarioSquareArchive.MapWidth, San9ScenarioSquareArchive.MapHeight,
            squares.Select(s => s.TerrainId ?? throw new InvalidDataException("Missing battlefield terrain.")).ToImmutableArray(),
            terrains.Select(t => Passage(t.Id)).ToImmutableArray(),
            formations.Select(f => new FormationRule(f.Id, checked(f.Mobility * 25), f.DeploymentCost,
                100000, 5, new(3, 3), Mode(f.Id))).ToImmutableArray(), places)
        {
            BlockedCells = squares.Where(s => s.IsPathBlocked).Select(s => new MapCell(s.GridX, s.GridY)).ToImmutableArray()
        };
        _ = new MilitaryRulebook(definition);
        return definition;
    }

    private static TravelMode Mode(int sourceId) => sourceId switch
    {
        >= 0 and <= 19 or 27 => TravelMode.Land,
        >= 20 and <= 26 => TravelMode.Water,
        _ => throw new InvalidDataException("Unknown TC101 formation movement mapping.")
    };

    private static TerrainPassage Passage(int sourceId) => sourceId switch
    {
        0 or 1 or 2 or 3 or 4 or 5 or 11 or 13 or 14 => new(sourceId, 10, null),
        6 or 7 or 8 => new(sourceId, 12, null),
        9 or 12 => new(sourceId, 12, 12),
        10 or 15 or 16 => new(sourceId, null, 10),
        _ => throw new InvalidDataException("Unknown TC101 terrain movement mapping.")
    };
}
