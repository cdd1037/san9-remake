using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using San9.Content.Images;
using San9.Content.Maps;
using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101;

public static class MapBuildingSmokeCompiler
{
    // RE-0156: the switches select fixed building types; these are not smoke-state numbers.
    public static MapBuildingSmoke[] Compile(byte[] exe, string dataDirectory, Func<string, byte[]> read,
        Func<string, string, TextureSourceImage, ImageAsset> save, List<ImageAsset> assets, List<MapAnimationClip> clips)
    {
        using var pe = new PEReader(new MemoryStream(exe));
        int At(int address) => BinaryPrimitives.ReadInt32LittleEndian(pe.GetSectionData(address - (int)pe.PEHeaders.PEHeader!.ImageBase).GetContent(0, 4).AsSpan());
        int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(offset, 4));
        Dictionary<int, int> palettes = [];
        foreach (var (start, count) in new[] { (0x24ea90, 1425), (0x2601d0, 1145) })
            for (int i = 0; i < count; i++) palettes[Int(start + i * 8) & 65535] = Int(start + i * 8 + 4);
        var archive = IndexedImageArchive.ReadArchive(read(Path.Combine(dataDirectory, "G_Effect.s9")));
        foreach (var (key, resource) in new[] { ("engagement", 5613), ("damage-a", 5611), ("damage-b", 5612) })
        {
            string clip = $"building-smoke-{key}";
            var sheet = archive.Images[resource - 5611];
            string[] frames = Enumerable.Range(0, 8).Select(i =>
            {
                string id = $"{clip}-{i}";
                // draw_flags 0x40: palette alpha, not fixed opacity or additive blending.
                var image = TextureSourceImage.Indexed(IndexedImageArchive.Frame(sheet, 8, 1, i, 0), archive.Palettes[palettes[resource] - 21], true, true);
                assets.Add(save(id, $"animations/{clip}/{i}.png", image));
                return id;
            }).ToArray();
            clips.Add(new(clip, frames, 100, []));
        }
        (int Address, int Count)[] dust = [(0x6647d0,20),(0x664778,11),(0x664710,8),(0x6646d0,8),
            (0x664668,13),(0x664600,13),(0x664598,13),(0x664508,13),(0x6644b0,11),(0x664508,13),
            (0x664448,8),(0x664408,8),(0x6643c0,9),(0x664378,9),(0x664330,9),(0x6642e8,9),(0x664778,11)];
        (int Address, int Count)[] damageA = [(0x6648b8,4),(0x6648a8,2),(0x664898,2),(0x664888,2),
            (0x664898,2),(0x664888,2),(0x6648a8,2),(0x6648a8,2),(0x6648a8,2),(0x6648a8,2),
            (0x664878,2),(0x664870,1),(0x664878,2),(0x664870,1),(0x664878,2),(0x664870,1),(0x6648a8,2)];
        (int Address, int Count)[] damageB = [(0x664950,2),(0x664940,2),(0x664930,2),(0x664920,2),
            (0x664908,3),(0x6648f0,3),(0x664940,2),(0x664940,2),(0x664940,2),(0x664940,2),
            (0x6648e8,1),(0x6648d8,2),(0x6648e8,1),(0x6648d8,2),(0x6648e8,1),(0x6648d8,2),(0x664940,2)];
        MapSmokeEmitter[] Points((int Address, int Count) table, string key, int chance, bool behind = false) =>
            Enumerable.Range(0, table.Count).Select(i => new MapSmokeEmitter($"building-smoke-{key}",
                At(table.Address + i * 8), At(table.Address + i * 8 + 4), chance, behind)).ToArray();
        return Enumerable.Range(0, 17).Select(type =>
        {
            int rear = type switch { 1 or 16 => 0x664750, 6 => 0x664570, 8 => 0x664488, _ => 0 };
            return new MapBuildingSmoke(Points(dust[type], "engagement", 25)
                .Concat(rear == 0 ? [] : Points((rear, 5), "engagement", 25, true)).ToArray(),
                Points(damageA[type], "damage-a", 50), Points(damageB[type], "damage-b", 50));
        }).ToArray();
    }
}
