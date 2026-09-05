using San9.Importer.San9PkTc101.Images;
using San9.Content.Images;
using System.Buffers.Binary;
using System.Text.Json;
using San9.Content.Maps;

namespace San9.Importer.San9PkTc101;

public static class MapAnimationCompiler
{
    public static bool HasWater(int terrain, int x, int y, Func<int, int, bool> bridge)
    {
        if (terrain is 9 or 10 or 15 or 16) return true;
        return terrain == 12 && (bridge(x + 1, y - 1) || bridge(x - 3, y - 1) || bridge(x - 1, y + 1) || bridge(x - 1, y - 3));
    }

    public static MapAnimations Compile(byte[] exe, byte[] scenario, string dataDir, JsonElement locations,
        IReadOnlyList<San9ScenarioSquareRecord> squares, IReadOnlyList<San9ScenarioCityRecord> cities,
        IReadOnlyList<San9ScenarioZoneRecord> zones, MapBuilding[] buildings, byte[] waterPalette, Func<string, byte[]> read,
        Func<string, string, PngImage, ImageAsset> save, List<ImageAsset> assets)
    {
        int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(offset, 4));
        byte[] Read(string name) => read(Path.Combine(dataDir, name + ".s9"));
        using var forceStream = new MemoryStream(scenario); using var personStream = new MemoryStream(scenario);
        var forces = San9ScenarioForceArchive.ReadKnownRecords(forceStream);
        var persons = San9ScenarioPersonArchive.ReadKnownRecords(personStream);
        var flagArchive = IndexedImageArchive.ReadCombined(Read("G_Flag"), 4, 52);
        var glyphArchive = IndexedImageArchive.ReadCombined(Read("G_Name"), 2, 372);
        var soldierArchive = IndexedImageArchive.ReadCombined(Read("G_UnitS"), 21, 611);
        var waterImages = IndexedImageArchive.ReadImages(Read("G_LMask"), 4);
        Dictionary<int, int> palettes = [];
        foreach (var (start, count) in new[] { (0x24ea90, 1425), (0x2601d0, 1145) })
            for (int i = 0; i < count; i++) palettes[Int(start + i * 8) & 65535] = Int(start + i * 8 + 4);
        List<MapAnimationClip> clips = []; List<MapAnimatedSprite> water = [], objects = [];
        var clipIds = new HashSet<string>();
        void Clip(string id, int count, int milliseconds, Func<int, PngImage> frame, Func<int, PngImage>? autumnWinterFrame = null)
        {
            if (!clipIds.Add(id)) return;
            string[] frames = new string[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = $"{id}-{i}";
                assets.Add(save(frames[i], $"animations/{id}/{i}.png", frame(i)));
            }
            Dictionary<string, string[]> seasonFrames = [];
            if (autumnWinterFrame is not null)
            {
                string[] autumnWinter = new string[count];
                for (int i = 0; i < count; i++)
                {
                    autumnWinter[i] = $"{id}-autumn-winter-{i}";
                    assets.Add(save(autumnWinter[i], $"animations/{id}/autumn-winter-{i}.png", autumnWinterFrame(i)));
                }
                seasonFrames = new() { ["spring"] = frames, ["summer"] = frames, ["autumn"] = autumnWinter, ["winter"] = autumnWinter };
            }
            clips.Add(new(id, frames, milliseconds, seasonFrames));
        }
        for (int parity = 0; parity < 4; parity++)
        {
            var image = waterImages[parity];
            if (image.Width != 384 || image.Height != 32) throw new InvalidDataException("Unexpected water sheet.");
            Clip($"water-{parity}", 6, 200, f =>
            {
                var rgba = IndexedImageArchive.Decode(IndexedImageArchive.Frame(image, 6, 1, f, 0), waterPalette, true, false);
                for (int i = 0; i < rgba.Pixels.Length; i += 4)
                    for (int c = 0; c < 3; c++) rgba.Pixels[i + c] = (byte)((rgba.Pixels[i + c] * 179 + 128) / 256);
                return rgba;
            });
        }
        bool Bridge(int x, int y) => x is >= 0 and < 200 && y is >= 0 and < 200 && squares[y * 200 + x].TerrainCode == 11 && squares[y * 200 + x].HasSquareFlagBit(9);
        for (int y = 0; y < 200; y++) for (int x = 0; x < 200; x++)
            if (HasWater(squares[y * 200 + x].TerrainCode, x, y, Bridge))
            {
                var center = MapTerrainCompiler.Center(x, y);
                water.Add(new($"water-{(x & 1) + 2 * (y & 1)}", center.X - 32, center.Y - 16, 0, -1));
            }
        for (int direction = 0; direction < 8; direction++)
        {
            int resource = 5384 + direction;
            var image = soldierArchive.Images[resource - 5000];
            if (image.Width != 288 || image.Height != 36 || palettes[resource] != 11) throw new InvalidDataException("Unexpected soldier resource.");
            Clip($"soldier-{direction}", 8, 100, f => IndexedImageArchive.Decode(IndexedImageArchive.Frame(image, 8, 1, f, 0), soldierArchive.Palettes[11], true, false));
        }
        var byComposition = locations.GetProperty("compositions").EnumerateArray().ToDictionary(c => c.GetProperty("id").GetInt32());
        var secondGlyphColors = new HashSet<int> { 3, 6, 7, 8, 11, 16, 18, 21, 22, 23, 24, 26, 27, 29, 31, 33, 38, 39, 40, 46, 49, 50 };
        foreach (var building in buildings)
        {
            int? forceId = building.Id < 50 ? cities[building.Id].ForceId : zones.Single(z => z.Id == building.Id - 50).ForceId;
            uint soldiers = building.Id < 50 ? cities[building.Id].Soldiers : zones.Single(z => z.Id == building.Id - 50).Soldiers;
            int color = 50, glyph = -1;
            if (forceId is int fid)
            {
                HashSet<int> visited = [];
                while (true)
                {
                    if (fid < 0 || fid >= forces.Count || !visited.Add(fid)) throw new InvalidDataException("Invalid root force chain.");
                    var force = forces[fid]; int root = force.RuntimeOwningForceId;
                    if (root != fid) { fid = root; continue; }
                    color = force.ColorCode;
                    glyph = force.CountryTitleId is int title ? Int(0x253bb0 + title * 4) : force.LeaderId is int leader ? persons[leader].FlagCode : -1;
                    break;
                }
            }
            if (color is < 0 or > 50) throw new InvalidDataException("Invalid flag color.");
            if (glyph is < 0 or > 185) glyph = -1;
            string flagId = $"flag-{color}-{glyph}";
            PngImage FlagFrame(int f, int direction)
            {
                var result = new PngImage(68, 52, 4, new byte[68 * 52 * 4]);
                // Direction 1 places the pole behind the cloth; direction 5 places it in front.
                int clothX = direction == 1 ? 1 : 34, glyphRow = direction == 1 ? 1 : 4;
                var pole = IndexedImageArchive.Decode(flagArchive.Images[0], flagArchive.Palettes[palettes[491] - 285], true, false);
                if (direction == 1) Overlay(result, pole, 31, 8);
                var face = IndexedImageArchive.Frame(flagArchive.Images[color + 1], 4, 8, f, direction);
                Overlay(result, IndexedImageArchive.Decode(face, flagArchive.Palettes[palettes[492 + color] - 285], true, false), clothX, 3);
                if (glyph >= 0)
                {
                    int variant = secondGlyphColors.Contains(color) ? 1 : 0;
                    var letter = IndexedImageArchive.Frame(glyphArchive.Images[variant * 186 + glyph], 4, 6, f, glyphRow);
                    Overlay(result, IndexedImageArchive.Decode(letter, glyphArchive.Palettes[variant], true, true), clothX, 3);
                }
                if (direction == 5) Overlay(result, pole, 31, 8);
                IndexedImageArchive.ExtendTransparentColors(result.Pixels, result.Width, result.Height);
                return result;
            }
            Clip(flagId, 4, 142, f => FlagFrame(f, 1), f => FlagFrame(f, 5));
            var basePoint = MapTerrainCompiler.Center(building.X, building.Y);
            int bx = basePoint.X, by = basePoint.Y - 10 * (squares[building.Y * 200 + building.X].OffsetCode - 1);
            var definition = byComposition[building.CompositionId];
            var guardTable = definition.GetProperty("soldiers");
            if (soldiers > 0)
                for (int i = 0; i < guardTable.GetProperty("count").GetInt32(); i++)
                {
                    int p = guardTable.GetProperty("offset").GetInt32() + i * 16, direction = Int(p + 12);
                    if (direction is < 0 or > 7) throw new InvalidDataException("Invalid guard direction.");
                    objects.Add(new($"soldier-{direction}", bx + Int(p), by + Int(p + 4), (building.Id + i) % 8, building.Id));
                }
            var flagTable = definition.GetProperty("flags");
            for (int i = 0; i < flagTable.GetProperty("count").GetInt32(); i++)
            {
                int p = flagTable.GetProperty("offset").GetInt32() + i * 8;
                objects.Add(new(flagId, bx + Int(p), by + Int(p + 4), (building.Id + i) % 4, building.Id));
            }
        }
        return new(clips.ToArray(), water.ToArray(), objects.ToArray());
    }

    private static void Overlay(PngImage target, PngImage source, int x, int y)
    {
        if (x < 0 || y < 0 || x + source.Width > target.Width || y + source.Height > target.Height) throw new InvalidDataException("Flag layer outside canvas.");
        for (int sy = 0; sy < source.Height; sy++) for (int sx = 0; sx < source.Width; sx++)
        {
            int s = (sy * source.Width + sx) * 4, d = ((y + sy) * target.Width + x + sx) * 4;
            int sa = source.Pixels[s + 3], da = target.Pixels[d + 3], alpha = sa + (da * (255 - sa) + 127) / 255;
            if (alpha == 0) continue;
            for (int c = 0; c < 3; c++) target.Pixels[d + c] = (byte)Math.Min(255, (source.Pixels[s + c] * sa + (target.Pixels[d + c] * da * (255 - sa) + 127) / 255 + alpha / 2) / alpha);
            target.Pixels[d + 3] = (byte)alpha;
        }
    }
}
