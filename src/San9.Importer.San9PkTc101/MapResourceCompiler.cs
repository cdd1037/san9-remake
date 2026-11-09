using San9.Rendering;
using San9.Importer.San9PkTc101.Images;
using San9.Content.Packages;
using San9.Content.Images;
using System.Buffers.Binary;
using System.Text.Json;
using San9.Content.Maps;

namespace San9.Importer.San9PkTc101;

public static class MapResourceCompiler
{
    public static MapResources Compile(byte[] exe, string dataDir, Func<string, byte[]> Read,
        Func<string, string, TextureSourceImage, ImageAsset> Save, Action<string>? progress, CancellationToken token)
    {
        List<ImageAsset> assets = [];
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
        IndexedImage[] Images(string name, int count) => IndexedImageArchive.ReadImages(Read(Path.Combine(dataDir, name + ".s9")), count);
        byte[][] Palettes(string name, int count) => IndexedImageArchive.ReadPalettes(Read(Path.Combine(dataDir, name + ".s9")), count);
        var land = Images("G_LLand", 771); List<MapSeason> seasons = [];
        string[] names = ["spring", "summer", "autumn", "winter"], suffixes = ["S", "N", "A", "W"];
        for (int s = 0; s < 4; s++)
        {
            var palettes = Palettes("P_Land_" + suffixes[s], 13); List<ImageAsset> textures = [];
            for (int i = 0; i < land.Length; i++)
            {
                int id = 10006 + i; int p = paletteBindings[id];
                textures.Add(Save(id.ToString(System.Globalization.CultureInfo.InvariantCulture), TextureSourcePath.Image("G_LLand", i, p, false, false, "P_Land_" + suffixes[s] + ".s9"), TextureSourceImage.Indexed(land[i], palettes[p], false, false)));
            }
            seasons.Add(new(names[s], textures.ToArray())); progress?.Invoke($"Converted {names[s]}: {textures.Count} RGB textures.");
        }
        var masks = Images("G_MASK", 162); var maskPalette = Palettes("P_MASK", 1)[0]; List<MapMask> maskRecords = [];
        for (int i = 0; i < masks.Length; i++)
        {
            int id = 10806 + i; var image = masks[i]; string weight = $"mask-{id}", coverage = $"coverage-{id}";
            assets.Add(Save(weight, $"masks/{id}.png", new PngImage(image.Width, image.Height, 1, image.Pixels.Select(p => maskPalette[p * 4]).ToArray())));
            assets.Add(Save(coverage, $"masks/{id}-coverage.png", new PngImage(image.Width, image.Height, 1, image.Pixels.Select(p => p == 0 ? (byte)0 : (byte)255).ToArray())));
            maskRecords.Add(new(id, weight, coverage));
        }
        var boundaryImages = Images("G_MAPMKR", 12); var boundaryPalette = Palettes("P_MAPMKR", 1)[0];
        for (int i = 0; i < 12; i++) assets.Add(Save($"boundary-{10794 + i}", TextureSourcePath.Image("G_MAPMKR", i, 0, true, false, "P_MAPMKR.s9"), TextureSourceImage.Indexed(boundaryImages[i], boundaryPalette, true, false)));
        var bridgeImages = Images("G_LHashi", 2); var additionalLandPalettes = Palettes("P_LndAdd", 2); var bridgePalette = additionalLandPalettes[0];
        for (int i = 0; i < 2; i++) assets.Add(Save($"bridge-{i}", TextureSourcePath.Image("G_LHashi", i, 0, true, false, "P_LndAdd.s9"), TextureSourceImage.Indexed(bridgeImages[i], bridgePalette, true, false)));
        List<MapComposition> compositions = [];
        using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(exe));
        int At(int address) => BinaryPrimitives.ReadInt32LittleEndian(pe.GetSectionData(checked(address - (int)pe.PEHeaders.PEHeader!.ImageBase)).GetContent(0, 4).AsSpan());
        foreach (var location in root.GetProperty("compositions").EnumerateArray())
        {
            int compositionId = location.GetProperty("id").GetInt32();
            int type = BuildingAppearance.CompositionType(compositionId);
            int group = At(0x615d60 + type * 4) + (type == 0 ? compositionId / 5 : 0);
            int start = location.GetProperty("offset").GetInt32(), count = location.GetProperty("count").GetInt32();
            var parts = Enumerable.Range(0, count).Select(i => { int p = start + 28 * i; return new MapCompositionPart(Int(p) != 0, Int(p + 4) != 0, Int(p + 12), Int(p + 16), Int(p + 20)) { DepthOffset = Int(p + 24), Ground = Int(p + 8) != 0 }; }).ToArray();
            var guards = location.GetProperty("soldiers"); var flags = location.GetProperty("flags");
            compositions.Add(new(location.GetProperty("id").GetInt32(), parts)
            {
                Guards = Enumerable.Range(0, guards.GetProperty("count").GetInt32()).Select(i => { int p = guards.GetProperty("offset").GetInt32() + 16*i; return new MapGuard(Int(p), Int(p+4), Int(p+12)) { DepthOffset = Int(p+8) }; }).ToArray(),
                Flags = Enumerable.Range(0, flags.GetProperty("count").GetInt32()).Select(i => { int p = flags.GetProperty("offset").GetInt32() + 8*i; return new MapFlag(Int(p), Int(p+4)); }).ToArray(),
                ArrowOrigins = Enumerable.Range(0, 8).Select(direction => Enumerable.Range(0, 8).Select(shot =>
                {
                    int point = 0x616da0 + (group * 8 + (shot % 3 + direction + 7) % 8) * 8;
                    return new MapFlag(At(point), At(point + 4));
                }).ToArray()).ToArray()
            });
        }
        var buildingImages = Images("G_LBUILD", 284); var buildingPalettes = Palettes("P_LBUILD", 6);
        var addImages = Images("G_LBAdd", 28); var addPalettes = Palettes("P_BAdd", 7);
        HashSet<string> exported = [];
        string BuildingImage(int id, bool alpha)
        {
            string key = $"building-{id}-{(alpha ? "alpha" : "key")}";
            if (!exported.Add(key)) return key;
            IndexedImage image; byte[] palette; string source;
            if (id >= 5947 && id <= 5974) { image = addImages[id - 5947]; palette = addPalettes[(id - 5947) / 4]; source = TextureSourcePath.Image("G_LBAdd", id - 5947, (id - 5947) / 4, true, alpha, "P_BAdd.s9"); }
            else
            {
                int p = id switch { >= 5663 and <= 5673 => 0, >= 5674 and <= 5684 => 1, >= 5685 and <= 5695 => 2, >= 5696 and <= 5737 => 3, >= 5738 and <= 5842 => 4, >= 5843 and <= 5946 => 5, _ => throw new InvalidDataException("Unknown building resource.") };
                image = buildingImages[id - 5663]; palette = buildingPalettes[p]; source = TextureSourcePath.Image("G_LBUILD", id - 5663, p, true, alpha, "P_LBUILD.s9");
            }
            assets.Add(Save(key, source, TextureSourceImage.Indexed(image, palette, true, alpha))); return key;
        }
        // Every decoded building resource has a known palette mapping. Export both supported
        // transparency interpretations; composition metadata selects the required one at runtime.
        for (int resource = 5663; resource <= 5974; resource++)
        {
            BuildingImage(resource, false);
            BuildingImage(resource, true);
        }
        // Validate every confirmed composition independently of the scenario snapshot.
        foreach (var composition in compositions)
            foreach (var part in composition.Parts)
                for (int variant = 0; variant < (composition.Id < 15 && part.DevelopmentVariant ? 3 : 1); variant++)
                    BuildingImage(part.ResourceId + variant, part.PaletteAlpha);
        progress?.Invoke($"Exported {exported.Count} building textures for {compositions.Count} confirmed compositions, including all city development variants.");
        var cloudImages = Images("G_LCLOUD", 17); var cloudPalettes = Palettes("P_LCLOUD", 5);
        assets.Add(Save("cloud-fill", TextureSourcePath.Image("G_LCLOUD", 6, 1, false, false, "P_LCLOUD.s9"), TextureSourceImage.Indexed(cloudImages[6], cloudPalettes[1], false, false)));
        foreach (int id in Enumerable.Range(10777, 6).Concat(Enumerable.Range(10788, 6)))
            assets.Add(Save($"cloud-{id}", TextureSourcePath.Image("G_LCLOUD", id - 10777, id < 10783 ? 0 : 4, true, true, "P_LCLOUD.s9"), TextureSourceImage.Indexed(cloudImages[id - 10777], cloudPalettes[id < 10783 ? 0 : 4], true, true)));
        var cloudPlacements = root.GetProperty("clouds").EnumerateArray().Select(c =>
        {
            var offsets = c.GetProperty("offsets").EnumerateArray().Select(o => Int(o.GetInt32())).ToArray();
            return (c.GetProperty("side").GetInt32(), offsets[0], offsets[1], offsets[2]);
        }).ToArray();
        var animations = MapAnimationCompiler.Compile(exe, dataDir, additionalLandPalettes[1], Read, Save, assets);
        var clips = animations.Clips.ToList();
        var units = MapUnitCompiler.Compile(exe, dataDir, Read, Save, assets, clips);
        var smoke = MapBuildingSmokeCompiler.Compile(exe, dataDir, Read, Save, assets, clips);
        compositions = compositions.Select(c => c with
        {
            Smoke = smoke[BuildingAppearance.CompositionType(c.Id)]
        }).ToList();
        var colors = Palettes("P_MapEtc", 2)[0].Skip(4).Take(32).ToArray();
        return new(seasons.ToArray(), assets.ToArray(), maskRecords.ToArray(), compositions.ToArray(), clips.ToArray(),
            positions, adjustments, cloudPlacements.Select(c => new CloudPlacement(c.Item1,c.Item2,c.Item3,c.Item4)).ToArray(), colors) { Units = units };
    }
}
