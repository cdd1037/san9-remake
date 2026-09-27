using San9.Importer.San9PkTc101.Images;
using San9.Content.Packages;
using San9.Content.Images;
using San9.Content.Maps;

namespace San9.Importer.San9PkTc101;

public static class MapImporter
{
    internal static MapPackage Build(byte[] scenario, MapResources resources, IReadOnlyList<ContentSource> sources)
    {
        T Scenario<T>(Func<Stream, T> read) { using var stream = new MemoryStream(scenario); return read(stream); }
        var header = Scenario(San9ScenarioArchive.ReadHeader);
        if (header.ScenarioId != 1 || header.StartDate != new San9ScenarioDate(190, 1, 1) || header.MapWidth != 200 || header.MapHeight != 200)
            throw new InvalidDataException("This MVP importer supports the registered 190-year scenario only.");
        var rawSquares = Scenario(San9ScenarioSquareArchive.ReadKnownRecords);
        var cities = Scenario(San9ScenarioCityArchive.ReadKnownRecords);
        var zones = Scenario(San9ScenarioZoneArchive.ReadKnownRecords);
        var fixedBuildings = Scenario(San9ScenarioFixedBuildingArchive.ReadKnownRecords);
        San9ScenarioSquareArchive.ValidateKnownTopology(rawSquares, cities, zones);
        var squares = rawSquares.Select(s => new MapSquare(s.TerrainCode, s.OffsetCode, s.ElevationTransitionMask, s.RegionCode ?? -1) { HideRegionName = s.IsExcludedFromRegionSquareCount }).ToArray();
        var assets = resources.Assets;
        var compositions = resources.Compositions;
        var terrain = MapTerrainCompiler.Compile(rawSquares, resources.MaskPositions, resources.MaskHeightAdjustments);
        var boundaries = MapBoundaryCompiler.Compile(rawSquares);
        List<MapSprite> bridges = [];
        foreach (var s in rawSquares.Where(s => s.TerrainCode == 11 && s.HasSquareFlagBit(9)))
        {
            if (s.OpaqueField0C > 1) throw new InvalidDataException("Invalid bridge orientation.");
            var c = MapTerrainCompiler.Center(s.GridX, s.GridY); bridges.Add(new($"bridge-{s.OpaqueField0C}", c.X - 96, c.Y - 16));
        }
        string BuildingImage(int id, bool alpha) => $"building-{id}-{(alpha ? "alpha" : "key")}";
        int[] bases = [0, 15, 19, 24, 29, 34, 39, 43, 48, 53, 58, 62, 66, 71, 76, 81, 86]; List<MapBuilding> buildings = [];
        void Building(int id, int x, int y, int type, int durability, int appearance, int development)
        {
            int max = fixedBuildings[type].MaxDurability;
            if (max <= 0 || x >= 200 || y >= 200) throw new InvalidDataException("Invalid building dimensions or durability.");
            bool four = type is 0 or 2 or 3 or 4 or 5 or 7 or 9 or 12 or 13 or 16;
            int frame = durability * 100 < max * 20 ? 0 : four && durability * 100 < max * 40 ? 1 : durability * 100 < max * 60 ? (four ? 2 : 1) : (four ? 3 : 2);
            int style = type != 0 ? 0 : appearance switch { 0 => 1, 1 => 2, 2 => 0, _ => throw new InvalidDataException("Invalid city appearance.") };
            int composition = bases[type] + 1 + frame + (type == 0 ? style * 5 : 0);
            var definition = compositions.SingleOrDefault(c => c.Id == composition) ?? throw new InvalidDataException($"Unconfirmed composition {composition}.");
            var center = MapTerrainCompiler.Center(x, y); int height = squares[y * 200 + x].Elevation;
            var sprites = definition.Parts.Select(p => new MapSprite(BuildingImage(p.ResourceId + (type == 0 && p.DevelopmentVariant ? (development <= 300 ? 0 : development <= 600 ? 1 : 2) : 0), p.PaletteAlpha), center.X + p.X, center.Y - 10 * (height - 1) + p.Y)).ToArray();
            buildings.Add(new(id, x, y, type, composition, sprites));
        }
        for (int i = 0; i < cities.Count; i++)
        {
            var c = cities[i]; if (c.Durability == ushort.MaxValue || c.MaxDurability == ushort.MaxValue) continue;
            Building(i, c.X, c.Y, 0, c.Durability, c.TerrainCode, Math.Max(c.Income, c.Harvest));
        }
        foreach (var z in zones)
            if (z.FixedBuildingId is int type && z.Durability != ushort.MaxValue) Building(50 + z.Id, z.X, z.Y, type, z.Durability, z.CityId is int c ? cities[c].TerrainCode : 0, 0);
        var orderedBuildings = buildings.OrderBy(b => b.X + b.Y).ThenBy(b => b.X).ToArray();
        var cloudPlacements = resources.CloudPlacements.Select(c => (c.Side,c.A,c.B,c.C)).ToArray();
        var edges = MapCloudCompiler.Edges(squares);
        var clouds = MapCloudCompiler.Foreground(squares, orderedBuildings, assets.ToDictionary(a => a.Id), cloudPlacements);
        var animations = new MapAnimations(resources.Clips, MapAnimationCompiler.PlaceWater(rawSquares));
        string[] names = ["spring", "summer", "autumn", "winter"];
        return new(13, San9PkTc101Detector.SourceBuildId, header.ScenarioId.ToString(System.Globalization.CultureInfo.InvariantCulture), names[(header.StartDate.Month - 1) / 3], 200, 200, 13056, 6656, squares, resources.Seasons, assets, resources.Masks, terrain, bridges.ToArray(), orderedBuildings.Select(b => new MapBuildingPlacement(b.Id,b.X,b.Y,b.FixedType)).ToArray(), compositions, clouds, edges.Left, edges.Right, sources.OrderBy(s => s.Name, StringComparer.Ordinal).ToArray(), animations, boundaries) { Units = resources.Units, MinimapColors = resources.MinimapColors, BuildingTypes = fixedBuildings.Select((b,id) => new MapBuildingType(id, b.MaxDurability)).ToArray() };
    }
}
