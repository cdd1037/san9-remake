using San9.Importer.San9PkTc101.Images;
using San9.Content.Images;
using System.Buffers.Binary;
using San9.Content.Maps;

namespace San9.Importer.San9PkTc101;

public static class MapAnimationCompiler
{
    public static bool HasWater(int terrain, int x, int y, Func<int, int, bool> bridge)
    {
        if (terrain is 9 or 10 or 15 or 16) return true;
        return terrain == 12 && (bridge(x + 1, y - 1) || bridge(x - 3, y - 1) || bridge(x - 1, y + 1) || bridge(x - 1, y - 3));
    }

    public static MapAnimatedSprite[] PlaceWater(IReadOnlyList<San9ScenarioSquareRecord> squares)
    {
        List<MapAnimatedSprite> water = [];
        bool Bridge(int x, int y) => x is >= 0 and < 200 && y is >= 0 and < 200 && squares[y * 200 + x].TerrainCode == 11 && squares[y * 200 + x].HasSquareFlagBit(9);
        for (int y = 0; y < 200; y++) for (int x = 0; x < 200; x++)
            if (HasWater(squares[y * 200 + x].TerrainCode, x, y, Bridge))
            {
                var center = MapTerrainCompiler.Center(x, y);
                water.Add(new($"water-{(x & 1) + 2 * (y & 1)}", center.X - 32, center.Y - 16, 0, -1));
            }
        return water.ToArray();
    }

    public static MapAnimations Compile(byte[] exe, string dataDir,
        byte[] waterPalette, Func<string, byte[]> read,
        Func<string, string, PngImage, ImageAsset> save, List<ImageAsset> assets)
    {
        int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(offset, 4));
        byte[] Read(string name) => read(Path.Combine(dataDir, name + ".s9"));
        var flagArchive = IndexedImageArchive.ReadCombined(Read("G_Flag"), 4, 52);
        var glyphArchive = IndexedImageArchive.ReadCombined(Read("G_Name"), 2, 372);
        var soldierArchive = IndexedImageArchive.ReadCombined(Read("G_UnitS"), 21, 611);
        var waterImages = IndexedImageArchive.ReadImages(Read("G_LMask"), 4);
        Dictionary<int, int> palettes = [];
        foreach (var (start, count) in new[] { (0x24ea90, 1425), (0x2601d0, 1145) })
            for (int i = 0; i < count; i++) palettes[Int(start + i * 8) & 65535] = Int(start + i * 8 + 4);
        List<MapAnimationClip> clips = []; List<MapAnimatedSprite> water = [];
        var clipIds = new HashSet<string>();
        void Clip(string id, int count, int milliseconds, Func<int, PngImage> frame, Func<int, PngImage>? autumnWinterFrame = null, MapBlendMode blend = MapBlendMode.Alpha)
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
            clips.Add(new(id, frames, milliseconds, seasonFrames) { BlendMode = blend });
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
            }, blend: MapBlendMode.Additive);
        }
        for (int direction = 0; direction < 8; direction++)
        {
            int resource = 5384 + direction;
            var image = soldierArchive.Images[resource - 5000];
            if (image.Width != 288 || image.Height != 36 || palettes[resource] != 11) throw new InvalidDataException("Unexpected soldier resource.");
            Clip($"soldier-{direction}", 8, 100, f => IndexedImageArchive.Decode(IndexedImageArchive.Frame(image, 8, 1, f, 0), soldierArchive.Palettes[11], true, false));
        }
        // Export independent layers, not the scenario's color/glyph combinations.
        // Runtime composition avoids a 51 x 187 cross-product of baked flag textures.
        PngImage Canvas(PngImage? layer, int x, int y)
        {
            var result = new PngImage(68,52,4,new byte[68*52*4]);
            if (layer is not null) Overlay(result,layer,x,y);
            IndexedImageArchive.ExtendTransparentColors(result.Pixels,result.Width,result.Height);
            return result;
        }
        var pole = IndexedImageArchive.Decode(flagArchive.Images[0], flagArchive.Palettes[palettes[491]-285],true,false);
        Clip("flag-pole-back",4,142,f => Canvas(pole,31,8),f => Canvas(null,0,0));
        Clip("flag-pole-front",4,142,f => Canvas(null,0,0),f => Canvas(pole,31,8));
        for (int color = 0; color <= 50; color++)
        {
            int c = color;
            PngImage Cloth(int f,int direction) => Canvas(IndexedImageArchive.Decode(
                IndexedImageArchive.Frame(flagArchive.Images[c+1],4,8,f,direction),flagArchive.Palettes[palettes[492+c]-285],true,false),direction==1?1:34,3);
            Clip($"flag-cloth-{c}",4,142,f => Cloth(f,1),f => Cloth(f,5));
        }
        for (int variant = 0; variant < 2; variant++) for (int glyph = 0; glyph < 186; glyph++)
        {
            int v = variant, g = glyph;
            PngImage Glyph(int f,int direction) => Canvas(IndexedImageArchive.Decode(
                IndexedImageArchive.Frame(glyphArchive.Images[v*186+g],4,6,f,direction==1?1:4),glyphArchive.Palettes[v],true,true),direction==1?1:34,3);
            Clip($"flag-glyph-{v}-{g}",4,142,f => Glyph(f,1),f => Glyph(f,5));
        }
        return new(clips.ToArray(), water.ToArray());
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
