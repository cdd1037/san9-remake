using San9.Content.Maps;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101;

public static class MapImporter
{
    internal static MapInstance Build(ScenarioSource scenario)
    {
        var header = scenario.Read(San9ScenarioArchive.ReadHeader);
        if (header.MapWidth != 200 || header.MapHeight != 200)
            throw new InvalidDataException("Unsupported TC101 map dimensions.");
        var squares = scenario.Read(San9ScenarioSquareArchive.ReadKnownRecords);
        var cities = scenario.Read(San9ScenarioCityArchive.ReadKnownRecords);
        var zones = scenario.Read(San9ScenarioZoneArchive.ReadKnownRecords);
        var types = scenario.Read(San9ScenarioFixedBuildingArchive.ReadKnownRecords);
        San9ScenarioSquareArchive.ValidateKnownTopology(squares, cities, zones);
        var cells = squares.Select(Cell).ToArray();
        List<MapBuildingPlacement> buildings = [];
        foreach (var (city, index) in cities.Select((c, i) => (c, i)))
            if (city.Durability != ushort.MaxValue && city.MaxDurability != ushort.MaxValue)
                buildings.Add(new(index, city.X, city.Y, 0));
        foreach (var zone in zones)
            if (zone.FixedBuildingId is int type && zone.Durability != ushort.MaxValue)
                buildings.Add(new(50 + zone.Id, zone.X, zone.Y, type));
        var definition = new MapDefinition(MapDefinition.CurrentSchemaVersion, San9PkTc101Detector.SourceBuildId,
            header.MapWidth, header.MapHeight, cells, types.Select(t => new MapBuildingType(t.Id, t.MaxDurability)).ToArray())
        { BlockingDefaults = new([1, 9, 12], [12], 2) };
        definition.Validate();
        return new(definition, buildings.OrderBy(b => b.X + b.Y).ThenBy(b => b.X).ToArray());
    }

    public static MapCellDefinition Cell(San9ScenarioSquareRecord square) =>
        new(square.TerrainCode, square.OffsetCode, square.OpaqueField0B, square.RegionCode)
        {
            HideRegionName = square.IsExcludedFromRegionSquareCount,
            BridgeOrientation = square.TerrainCode == 11 && square.HasSquareFlagBit(9) ? square.OpaqueField0C : null,
            MovementBlocked = square.IsPathBlocked,
            InfluenceBlocked = square.PathBlockingBits == 0x01fe,
            Construction = (square.IsFacilityTemplateSquare ? ConstructionSurface.General : 0) |
                (square.AllowsFacilityShape3By1 ? ConstructionSurface.Horizontal : 0) |
                (square.AllowsFacilityShape1By3 ? ConstructionSurface.Vertical : 0)
        };
}
