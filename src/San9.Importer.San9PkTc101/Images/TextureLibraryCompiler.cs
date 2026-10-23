using System.Buffers.Binary;
using San9.Content.Images;
using San9.Content.Packages;

namespace San9.Importer.San9PkTc101.Images;

public static class TextureLibraryCompiler
{
    // Offsets identify source data; the loader evidence is recorded in RE-SAN9PK-0169.
    private sealed record Recipe(string Category, string? Palette = null, int Table = 0, int Count = 0,
        int FirstImage = 0, int FirstPalette = 0);
    private static readonly Dictionary<string, Recipe> Recipes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["G_Common"] = new("interface/common", Table: 0x24ea90, Count: 1425, FirstPalette: 200),
        ["G_EditPK"] = new("interface/editor", Table: 0x24ea90, Count: 1425, FirstImage: 1254, FirstPalette: 267),
        ["G_Flag"] = new("map/flags", Table: 0x24ea90, Count: 1425, FirstImage: 491, FirstPalette: 285),
        ["G_Name"] = new("interface/glyphs", Table: 0x24ea90, Count: 1425, FirstImage: 543, FirstPalette: 289),
        ["G_Jinkei"] = new("interface/formations", Table: 0x24ea90, Count: 1425, FirstImage: 915, FirstPalette: 291),
        ["G_MMap"] = new("interface/overview", Table: 0x24ea90, Count: 1425, FirstImage: 999, FirstPalette: 295),
        ["G_Marker"] = new("interface/markers", Table: 0x24ea90, Count: 1425, FirstImage: 1220, FirstPalette: 311),
        ["G_LocCPK"] = new("interface/local-map", Table: 0x24ea90, Count: 1425, FirstImage: 1367, FirstPalette: 283),
        ["G_Main"] = new("interface/strategy", Table: 0x2601d0, Count: 1145, FirstImage: 5975, FirstPalette: 60),
        ["G_UnitS"] = new("map/units", Table: 0x2601d0, Count: 1145, FirstImage: 5000),
        ["G_Effect"] = new("effects/map", Table: 0x2601d0, Count: 1145, FirstImage: 5611, FirstPalette: 21),
        ["G_LBUILD"] = new("map/buildings", "P_LBUILD.s9", 0x2601d0, 1145, 5663, 41),
        ["G_LBAdd"] = new("map/buildings/additions", "P_BAdd.s9", 0x2601d0, 1145, 5947, 53),
        ["G_LocalM"] = new("interface/local-map", Table: 0x2601d0, Count: 1145, FirstImage: 6073, FirstPalette: 81),
        ["G_LocMPK"] = new("interface/local-map"),
        ["G_LocalS"] = new("interface/local-map"),
        ["G_LocSPK"] = new("scenes/start", Table: 0x267550, Count: 447, FirstImage: 3652, FirstPalette: 50),
        ["G_Start"] = new("scenes/start", Table: 0x267550, Count: 447, FirstImage: 3600),
        ["G_StrtPK"] = new("scenes/start", Table: 0x267550, Count: 447, FirstImage: 3653, FirstPalette: 51),
        ["G_Start2"] = new("scenes/scenario", Table: 0x267550, Count: 447, FirstImage: 3672, FirstPalette: 59),
        ["G_BGGAL"] = new("scenes/gallery", Table: 0x267550, Count: 447, FirstImage: 3884, FirstPalette: 144),
        ["G_Ending"] = new("scenes/ending", Table: 0x264698, Count: 51, FirstImage: 3000),
        ["G_TitlPK"] = new("scenes/title", Table: 0x268968, Count: 4, FirstImage: 3000),
        ["G_Title"] = new("scenes/title"), ["G_KLogo"] = new("scenes/logo"), ["G_KLgPK"] = new("scenes/logo"),
        ["G_Still"] = new("illustrations"), ["G_StilPK"] = new("illustrations"),
        ["G_FaceL"] = new("portraits", "P_Face.s9"), ["G_FaceS"] = new("portraits", "P_Face.s9"),
        ["G_FaceT"] = new("portraits", "P_Face.s9"), ["G_FacLPK"] = new("portraits", "P_Face.s9"),
        ["G_FacSPK"] = new("portraits", "P_Face.s9"), ["G_FacTPK"] = new("portraits", "P_Face.s9"),
        ["G_LLand"] = new("map/terrain", "P_Land_S.s9", 0x25cfd0, 1070, 10006, 91),
        ["G_LHashi"] = new("map/bridges", "P_LndAdd.s9", 0x25cfd0, 1070, 10004, 89),
        ["G_LMask"] = new("map/water", "P_LndAdd.s9", 0x25cfd0, 1070, 10000, 89),
        ["G_LCLOUD"] = new("map/clouds", "P_LCLOUD.s9", 0x25cfd0, 1070, 10777, 104),
        ["G_MAPMKR"] = new("map/boundaries", "P_MAPMKR.s9", 0x25cfd0, 1070, 10794, 109),
        ["G_MASK"] = new("map/masks", "P_MASK.s9", 0x25cfd0, 1070, 10806, 110),
        ["G_MESH"] = new("map/mesh", "P_MESH.s9", 0x25cfd0, 1070, 10972, 111),
        ["G_MapCov"] = new("map/cover", "P_MapEtc.s9", 0x25cfd0, 1070, 10968, 112),
        ["G_MpMkPK"] = new("map/markers"), ["G_LNMASK"] = new("map/unresolved-masks"),
        ["G_LNUKI"] = new("map/unresolved-cutouts", "P_NUKI.s9"),
        ["G_DynEff"] = new("effects/dynamic", "P_DynEff.s9"), ["G_Ikki"] = new("scenes/duel", "P_Ikki.s9"),
        ["G_Gaikou"] = new("scenes/diplomacy", "P_Gaikou.s9"), ["G_ZoomIn"] = new("scenes/zoom", "P_ZoomIn.s9"),
        ["G_Radar"] = new("map/radar"), ["G_RadarM"] = new("map/radar"), ["G_RadarL"] = new("map/radar")
    };

    public static IReadOnlyList<string> ArchiveIds { get; } = Recipes.Keys.Select(name => name.ToLowerInvariant()).Order(StringComparer.Ordinal).ToArray();

    public static void ValidateInventory(string dataDirectory)
    {
        var actual = Directory.EnumerateFiles(dataDirectory, "G_*.s9").Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actual.SetEquals(Recipes.Keys)) throw new InvalidDataException("Graphics archive inventory differs from the supported source; review the texture recipes.");
    }

    public static TextureLibrary Compile(byte[] exe, string dataDirectory, Func<string, byte[]> read,
        Func<string, string, PngImage, ImageAsset> save, Action<string>? progress, CancellationToken token, string? archiveId = null)
    {
        if (archiveId is not null && !Recipes.Keys.Any(name => name.ToLowerInvariant() == archiveId))
            throw new ArgumentException($"Unknown texture archive: {archiveId}.");
        if (archiveId is null) ValidateInventory(dataDirectory);
        List<TextureArchive> archives = [];
        foreach (var (name, recipe) in Recipes.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (archiveId is not null && name.ToLowerInvariant() != archiveId) continue;
            token.ThrowIfCancellationRequested();
            // PortraitCompiler owns the single normal-color mother image and crop metadata.
            if (recipe.Palette == "P_Face.s9") continue;
            byte[] bytes = read(Path.Combine(dataDirectory, name + ".s9"));
            byte[]? paletteBytes = recipe.Palette is null ? null : read(Path.Combine(dataDirectory, recipe.Palette));
            string id = name.ToLowerInvariant();
            ImageAsset Save(string key, PngImage image) => save($"library/{id}/{key}", $"{recipe.Category}/{id}/{key}.png", image);
            if (bytes[0] == 'B' && bytes[1] == 'M')
            {
                var asset = Save("image/0", DecodeBitmap(bytes));
                archives.Add(new(id, recipe.Category, [new(0, -1, asset)])); continue;
            }
            var (palettes, images) = IndexedImageArchive.ReadArchive(bytes);
            if (paletteBytes is not null) palettes = IndexedImageArchive.ReadPalettes(paletteBytes, Int(paletteBytes, 0));
            var bindings = Bindings(exe, name, recipe, images.Length, palettes.Length);
            Dictionary<int, PngImage> openingLayers = [];
            if (name == "G_MMap")
            {
                var layers = OpeningIslandArtwork.Separate(
                    IndexedImageArchive.Decode(images[168], palettes[12], true, false),
                    IndexedImageArchive.Decode(images[165], palettes[10], true, false), 496, 320, 63, 106, images[166]);
                openingLayers.Add(168, layers.Background); openingLayers.Add(165, layers.Island);
            }
            if (name == "G_Start2")
            {
                var layers = OpeningIslandArtwork.Separate(
                    IndexedImageArchive.Decode(Interface.OpeningMapCompiler.AlignPreviewArtwork(exe, images[67], false), palettes[63], true, true),
                    IndexedImageArchive.Decode(Interface.OpeningMapCompiler.AlignPreviewArtwork(exe, images[68], true), palettes[63], true, true),
                    344, 214, 99, 80);
                openingLayers.Add(67, layers.Background); openingLayers.Add(68, layers.Island);
            }
            List<TextureVariant> variants = [];
            // Portraits, illustrations and CPU land are intentionally opaque. Other indexed display
            // sources use the complete palette-entry key. Palette-alpha consumers remain explicit
            // below and in map/interface compilers; source alpha is not guessed from its numeric range.
            bool opaque = recipe.Category.StartsWith("portraits/", StringComparison.Ordinal) ||
                recipe.Category == "illustrations" || name == "G_LLand";
            // Emit source colors; the package builder packs every consumer through one catalog.
            // G_Still 72..171 are small copies of treasure images 172..271; consumers scale the large image.
            foreach (var group in bindings.Where(b => name != "G_Still" || b.Image < 72 || b.Image >= 172)
                .GroupBy(b => b.Palette).OrderBy(g => g.Key))
            {
                var converted = group.OrderBy(b => b.Image).Select(b =>
                {
                    // RE-SAN9PK-0194: arena, speech and panels use palette-alpha mode.
                    // Health bars, bodies and additive particles have separate native modes.
                    // Zoom terrain, transitions, edges, facilities and both panels also use palette alpha.
                    // RE-0200/0201: opening and scenario-selection frames, previews and controls use mode 64.
                    // RE-0071: settings illustration, choice highlight/bars and expert dialog also use mode 64.
                    bool paletteAlpha = name == "G_LocSPK" || name == "G_StrtPK" && b.Image is 0 or 1 or 2 or 15 ||
                        name == "G_Start" && (b.Image is 20 or 21 or 23 or 24 or 38 or 39 || b.Image is >= 27 and <= 36 || b.Image >= 45) ||
                        name == "G_Start2" && (b.Image == 52 || b.Image is >= 66 and <= 81 || b.Image >= 161) ||
                        name == "G_Ikki" && (b.Image < 9 || b.Image is >= 11 and <= 18) ||
                        name == "G_ZoomIn" && (b.Image < 58 || b.Image is 141 or 142) ||
                        name == "G_Gaikou" && b.Image >= 19 || name == "G_Ending" && b.Image is 1 or 2 or 7 or 8 or 9;
                    bool openingLayer = openingLayers.TryGetValue(b.Image, out var layer);
                    return new TextureVariant(b.Image, b.Palette,
                        save($"library/{id}/variant-{b.Palette:D3}/{b.Image:D4}",
                            TextureSourcePath.Image(name, b.Image, b.Palette, !opaque, paletteAlpha, recipe.Palette,
                                transform: openingLayer ? "opening-framed-island" : null),
                            openingLayer ? layer! : IndexedImageArchive.Decode(images[b.Image], palettes[b.Palette], !opaque, paletteAlpha)));
                }).ToArray();
                variants.AddRange(converted);
            }
            if (variants.Count != 0) archives.Add(new(id, recipe.Category, variants.ToArray()));
            progress?.Invoke($"Texture catalog {name}: {variants.Count} converted textures.");
        }
        var library = new TextureLibrary(3, archives.ToArray()); library.Validate(); return library;
    }

    private static HashSet<(int Image, int Palette)> Bindings(byte[] exe, string name, Recipe recipe, int images, int palettes)
    {
        HashSet<(int Image, int Palette)> result = [];
        void Add(int image, int palette)
        {
            if (image < 0 || image >= images || palette < 0 || palette >= palettes)
                throw new InvalidDataException($"Invalid confirmed palette binding: {name}/{image}/{palette}");
            result.Add((image, palette));
        }
        if (recipe.Count != 0)
        {
            for (int i = 0; i < recipe.Count; i++)
            {
                int image = (Int(exe, recipe.Table + i * 8) & 65535) - recipe.FirstImage;
                if (image >= 0 && image < images) Add(image, Int(exe, recipe.Table + i * 8 + 4) - recipe.FirstPalette);
            }
        }
        else if (name is "G_Still" or "G_StilPK")
            for (int i = 0; i < images; i++) Add(i, i);
        else if (name == "G_DynEff")
            for (int p = 0; p < 28; p++)
                for (int i = 0; i < Int(exe, 0x263c40 + p * 12 + 4); i++) Add(Int(exe, 0x263c40 + p * 12) + i, p);
        else if (name == "G_ZoomIn")
        {
            for (int i = 0; i < 58; i++) Add(i, i);
            for (int t = 0; t < 23; t++)
                for (int v = 0; v < 6; v++)
                    if (Int(exe, 0x2635bc + t * 52 + v * 8) != -1)
                        Add(Int(exe, 0x2635c0 + t * 52 + v * 8), Int(exe, 0x2635b8 + t * 52));
            Add(Int(exe, 0x2635b4), Int(exe, 0x2635b8 + 9 * 52));
            for (int i = 120; i <= 144; i++) Add(i, i - 43);
        }
        else if (name == "G_LocMPK")
        {
            for (int i = 0; i < 64; i++) Add(i, i % 2);
            for (int i = 64; i < 68; i++) Add(i, i - 62);
        }
        else if (name == "G_Ikki")
        {
            for (int i = 0; i < 9; i++) Add(i, i);
            for (int i = 9; i < 60; i++) Add(i, Int(exe, 0x263d90 + (i - 3) * 8 + 4) - 107);
            for (int i = 60; i < 70; i++) Add(i, 17);
            for (int i = 70; i < 80; i++) Add(i, 18);
        }
        else if (name == "G_Gaikou")
        {
            for (int i = 0; i <= 24; i++) Add(i, 0);
            for (int i = 25; i <= 47; i++) Add(i, 1);
            for (int i = 48; i <= 50; i++) Add(i, 2);
        }
        else if (palettes == 1)
            for (int i = 0; i < images; i++) Add(i, 0);
        return result;
    }

    private static int Int(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));

    public static PngImage DecodeBitmap(byte[] bytes)
    {
        if (bytes.Length < 54 || bytes[0] != 'B' || bytes[1] != 'M' || Int(bytes, 14) != 40 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26)) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28)) != 24 || Int(bytes, 30) != 0)
            throw new InvalidDataException("Unsupported radar bitmap.");
        int offset = Int(bytes, 10), width = Int(bytes, 18), signedHeight = Int(bytes, 22);
        if (width is < 1 or > 16384 || signedHeight is 0 or < -16384 or > 16384) throw new InvalidDataException("Invalid bitmap dimensions.");
        int height = Math.Abs(signedHeight), stride = (width * 3 + 3) & ~3;
        if (offset < 54 || (long)offset + (long)stride * height > bytes.Length) throw new InvalidDataException("Invalid bitmap length.");
        byte[] pixels = new byte[width * height * 3];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int from = offset + (signedHeight > 0 ? height - y - 1 : y) * stride + x * 3, to = (y * width + x) * 3;
            pixels[to] = bytes[from + 2]; pixels[to + 1] = bytes[from + 1]; pixels[to + 2] = bytes[from];
        }
        return new(width, height, 3, pixels);
    }
}
