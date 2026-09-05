using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using San9.Content.Maps;

namespace San9.Importer.San9PkTc101;

public static class MapImporter
{
    public static async Task<MapPackage> ImportAsync(string sourceExe, string dataDirectory, string scenarioPath, string output, Action<string>? progress = null, CancellationToken token = default)
    {
        var identity = await San9PkTc101Detector.DetectAsync(sourceExe, token);
        if (!identity.IsMatch) throw new InvalidDataException(identity.Reason);
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists; choose a new package directory.");
        string staging = output + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            var map = Build(sourceExe, dataDirectory, scenarioPath, staging, progress, token);
            MapPackageFiles.Validate(map);
            await File.WriteAllTextAsync(Path.Combine(staging, "map.json"), JsonSerializer.Serialize(map, MapPackageFiles.JsonOptions), token);
            await MapPackageFiles.LoadAsync(staging, token);
            Directory.Move(staging, output);
            return map;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    private static MapPackage Build(string exePath, string dataDir, string scenarioPath, string output, Action<string>? progress, CancellationToken token)
    {
        List<MapSource> sources = []; List<MapAsset> assets = [];
        byte[] Read(string path)
        {
            token.ThrowIfCancellationRequested(); var bytes = File.ReadAllBytes(path);
            sources.Add(new(Path.GetFileName(path), Convert.ToHexString(SHA256.HashData(bytes)))); return bytes;
        }
        byte[] exe = Read(exePath), scenario = Read(scenarioPath);
        T Scenario<T>(Func<Stream, T> read) { using var stream = new MemoryStream(scenario); return read(stream); }
        var header = Scenario(San9ScenarioArchive.ReadHeader);
        if (header.ScenarioId != 1 || header.StartDate != new San9ScenarioDate(190, 1, 1) || header.MapWidth != 200 || header.MapHeight != 200)
            throw new InvalidDataException("This MVP importer supports the registered 190-year scenario only.");
        var rawSquares = Scenario(San9ScenarioSquareArchive.ReadKnownRecords);
        var cities = Scenario(San9ScenarioCityArchive.ReadKnownRecords);
        var zones = Scenario(San9ScenarioZoneArchive.ReadKnownRecords);
        var fixedBuildings = Scenario(San9ScenarioFixedBuildingArchive.ReadKnownRecords);
        San9ScenarioSquareArchive.ValidateKnownTopology(rawSquares, cities, zones);
        var squares = rawSquares.Select(s => new MapSquare(s.TerrainCode, s.OffsetCode, s.ElevationTransitionMask, s.RegionCode ?? -1)).ToArray();
        using var locationsStream = typeof(MapImporter).Assembly.GetManifestResourceStream("San9.Importer.San9PkTc101.MapSourceLocations.json")!;
        using var locations = JsonDocument.Parse(locationsStream); var root = locations.RootElement;
        int Int(int offset) => offset == -1 ? 0 : BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(offset, 4));
        Dictionary<int, int> paletteBindings = [];
        for (int i = 0; i < 0x42e; i++)
        {
            int id = Int(0x25cfd0 + i * 8), slot = Int(0x25cfd0 + i * 8 + 4);
            if (id is >= 10006 and <= 10776) paletteBindings.Add(id, slot - 91);
        }
        if (paletteBindings.Count != 771 || paletteBindings.Any(p => MapTerrainCompiler.Palette(p.Key) != p.Value))
            throw new InvalidDataException("Source land registry does not match confirmed bindings.");
        var positions = Enumerable.Range(0, 288).Select(i => Int(root.GetProperty("maskPositionOffset").GetInt32() + 4 * i)).ToArray();
        var adjustments = Enumerable.Range(0, 16).Select(i => Int(root.GetProperty("maskHeightOffset").GetInt32() + 4 * i)).ToArray();
        var terrain = MapTerrainCompiler.Compile(rawSquares, positions, adjustments);
        progress?.Invoke($"Validated {squares.Length} squares; compiled {terrain.Length} terrain stamps.");

        IndexedMapImage[] Images(string name, int count) => MapImageArchive.ReadImages(Read(Path.Combine(dataDir, name + ".s9")), count);
        byte[][] Palettes(string name, int count) => MapImageArchive.ReadPalettes(Read(Path.Combine(dataDir, name + ".s9")), count);
        MapAsset Save(string id, string path, PngImage image)
        {
            token.ThrowIfCancellationRequested(); string target = MapPackageFiles.Resolve(output, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            byte[] bytes = image.Encode(); File.WriteAllBytes(target, bytes);
            return new(id, path, Convert.ToHexString(SHA256.HashData(bytes)), image.Width, image.Height);
        }
        var land = Images("G_LLand", 771); List<MapSeason> seasons = [];
        string[] names = ["spring", "summer", "autumn", "winter"], suffixes = ["S", "N", "A", "W"];
        for (int s = 0; s < 4; s++)
        {
            var palettes = Palettes("P_Land_" + suffixes[s], 13); List<MapAsset> textures = [];
            for (int i = 0; i < land.Length; i++)
            {
                int id = 10006 + i; int p = paletteBindings[id];
                textures.Add(Save(id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"terrain/{names[s]}/{id}.png", MapImageArchive.Decode(land[i], palettes[p], false, false)));
            }
            seasons.Add(new(names[s], textures.ToArray())); progress?.Invoke($"Converted {names[s]}: {textures.Count} RGB textures.");
        }
        var masks = Images("G_MASK", 162); var maskPalette = Palettes("P_MASK", 1)[0]; List<MapMask> maskRecords = [];
        for (int i = 0; i < masks.Length; i++)
        {
            int id = 10806 + i; var image = masks[i]; string weight = $"mask-{id}", coverage = $"coverage-{id}";
            assets.Add(Save(weight, $"masks/{id}.png", new(image.Width, image.Height, 1, image.Pixels.Select(p => maskPalette[p * 4]).ToArray())));
            assets.Add(Save(coverage, $"masks/{id}-coverage.png", new(image.Width, image.Height, 1, image.Pixels.Select(p => p == 0 ? (byte)0 : (byte)255).ToArray())));
            maskRecords.Add(new(id, weight, coverage));
        }
        var bridgeImages = Images("G_LHashi", 2); var additionalLandPalettes = Palettes("P_LndAdd", 2); var bridgePalette = additionalLandPalettes[0];
        for (int i = 0; i < 2; i++) assets.Add(Save($"bridge-{i}", $"objects/bridge-{i}.png", MapImageArchive.Decode(bridgeImages[i], bridgePalette, true, false)));
        List<MapSprite> bridges = [];
        foreach (var s in rawSquares.Where(s => s.TerrainCode == 11 && s.HasSquareFlagBit(9)))
        {
            if (s.OpaqueField0C > 1) throw new InvalidDataException("Invalid bridge orientation.");
            var c = MapTerrainCompiler.Center(s.GridX, s.GridY); bridges.Add(new($"bridge-{s.OpaqueField0C}", c.X - 96, c.Y - 16));
        }
        List<MapComposition> compositions = [];
        foreach (var location in root.GetProperty("compositions").EnumerateArray())
        {
            int start = location.GetProperty("offset").GetInt32(), count = location.GetProperty("count").GetInt32();
            var parts = Enumerable.Range(0, count).Select(i => { int p = start + 28 * i; return new MapCompositionPart(Int(p) != 0, Int(p + 4) != 0, Int(p + 12), Int(p + 16), Int(p + 20)); }).ToArray();
            compositions.Add(new(location.GetProperty("id").GetInt32(), parts));
        }
        var buildingImages = Images("G_LBUILD", 284); var buildingPalettes = Palettes("P_LBUILD", 6);
        var addImages = Images("G_LBAdd", 28); var addPalettes = Palettes("P_BAdd", 7);
        HashSet<string> exported = [];
        string BuildingImage(int id, bool alpha)
        {
            string key = $"building-{id}-{(alpha ? "alpha" : "key")}";
            if (!exported.Add(key)) return key;
            IndexedMapImage image; byte[] palette;
            if (id >= 5947 && id <= 5974) { image = addImages[id - 5947]; palette = addPalettes[(id - 5947) / 4]; }
            else
            {
                int p = id switch { >= 5663 and <= 5673 => 0, >= 5674 and <= 5684 => 1, >= 5685 and <= 5695 => 2, >= 5696 and <= 5737 => 3, >= 5738 and <= 5842 => 4, >= 5843 and <= 5946 => 5, _ => throw new InvalidDataException("Unknown building resource.") };
                image = buildingImages[id - 5663]; palette = buildingPalettes[p];
            }
            assets.Add(Save(key, $"objects/{key}.png", MapImageArchive.Decode(image, palette, true, alpha))); return key;
        }
        // Export the confirmed visual catalog independently of the scenario snapshot.
        foreach (var composition in compositions)
            foreach (var part in composition.Parts)
                for (int variant = 0; variant < (composition.Id < 15 && part.DevelopmentVariant ? 3 : 1); variant++)
                    BuildingImage(part.ResourceId + variant, part.PaletteAlpha);
        progress?.Invoke($"Exported {exported.Count} building textures for {compositions.Count} confirmed compositions, including all city development variants.");
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
        var cloudImages = Images("G_LCLOUD", 17); var cloudPalettes = Palettes("P_LCLOUD", 5);
        assets.Add(Save("cloud-fill", "objects/cloud-fill.png", MapImageArchive.Decode(cloudImages[6], cloudPalettes[1], false, false)));
        foreach (int id in Enumerable.Range(10777, 6).Concat(Enumerable.Range(10788, 6)))
            assets.Add(Save($"cloud-{id}", $"objects/cloud-{id}.png", MapImageArchive.Decode(cloudImages[id - 10777], cloudPalettes[id < 10783 ? 0 : 4], true, true)));
        var cloudPlacements = root.GetProperty("clouds").EnumerateArray().Select(c =>
        {
            var offsets = c.GetProperty("offsets").EnumerateArray().Select(o => Int(o.GetInt32())).ToArray();
            return (c.GetProperty("side").GetInt32(), offsets[0], offsets[1], offsets[2]);
        }).ToArray();
        var edges = MapCloudCompiler.Edges(squares);
        var clouds = MapCloudCompiler.Foreground(squares, orderedBuildings, assets.ToDictionary(a => a.Id), cloudPlacements);
        var animations = MapAnimationCompiler.Compile(exe, scenario, dataDir, root, rawSquares, cities, zones, orderedBuildings, additionalLandPalettes[1], Read, Save, assets);
        progress?.Invoke($"Animations: {animations.Clips.Length} clips, {animations.Water.Length} water tiles, {animations.Buildings.Length} building sprites.");
        progress?.Invoke($"Objects: {bridges.Count} bridges, {buildings.Count} buildings, {clouds.Length} foreground clouds; fallback=0.");
        return new(3, San9PkTc101Detector.SourceBuildId, header.ScenarioId.ToString(System.Globalization.CultureInfo.InvariantCulture), names[(header.StartDate.Month - 1) / 3], 200, 200, 13056, 6656, squares, seasons.ToArray(), assets.ToArray(), maskRecords.ToArray(), terrain, bridges.ToArray(), orderedBuildings, compositions.ToArray(), clouds, edges.Left, edges.Right, sources.OrderBy(s => s.Name, StringComparer.Ordinal).ToArray(), animations);
    }
}
