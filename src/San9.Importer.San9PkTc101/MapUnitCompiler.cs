using System.Buffers.Binary;
using San9.Content.Images;
using San9.Content.Maps;
using San9.Core.Domain;
using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101;

public static class MapUnitCompiler
{
    // RE-SAN9PK-0156: source tables supply body identities, anchors and idle sheets.
    public static MapUnitCatalog Compile(byte[] exe, string dataDirectory, Func<string, byte[]> read,
        Func<string, string, PngImage, ImageAsset> save, List<ImageAsset> assets, List<MapAnimationClip> clips)
    {
        int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(offset, 4));
        var units = IndexedImageArchive.ReadArchive(read(Path.Combine(dataDirectory, "G_UnitS.s9")));
        var effects = IndexedImageArchive.ReadArchive(read(Path.Combine(dataDirectory, "G_Effect.s9")));
        var markers = IndexedImageArchive.ReadArchive(read(Path.Combine(dataDirectory, "G_Marker.s9")));
        Dictionary<int, int> palettes = [];
        foreach (var (start, count) in new[] { (0x24ea90, 1425), (0x2601d0, 1145) })
            for (int i = 0; i < count; i++) palettes[Int(start + i * 8) & 65535] = Int(start + i * 8 + 4);
        string Export(string key, int resource, int milliseconds, bool alpha)
        {
            var (archive, first, paletteBase) = resource >= 5611 ? (effects, 5611, 21) :
                resource >= 5000 ? (units, 5000, 0) : (markers, 1220, 311);
            var sheet = archive.Images[resource - first];
            int[] sequence = resource is 1246 or 1250 or 1247 ? [0, 1, 2, 3, 3, 3] : Enumerable.Range(0, 8).ToArray();
            var frames = Enumerable.Range(0, sequence.Length).Select(frame =>
            {
                string id = $"{key}-{frame}";
                var image = IndexedImageArchive.Decode(IndexedImageArchive.Frame(sheet, 8, 1, sequence[frame], 0),
                    archive.Palettes[palettes[resource] - paletteBase], true, alpha);
                assets.Add(save(id, $"animations/{key}/{frame}.png", image));
                return id;
            }).ToArray();
            clips.Add(new(key, frames, milliseconds, []) { BlendMode = alpha ? MapBlendMode.Additive : MapBlendMode.Alpha });
            return key;
        }
        var bodies = Enumerable.Range(0, 20).Select(type => new MapUnitBody($"unit-body-{type}",
            Enumerable.Range(0, 8).Select(direction => Export($"unit-idle-{type}-{direction}",
                Int(0x263240 + 4 * type) + direction, 100, false)).ToArray(),
            Int(0x214980 + 8 * type), Int(0x214984 + 8 * type))).ToArray();
        var formations = Enumerable.Range(0, 28).Select(id =>
        {
            int type = Int(0x262598 + 4 * id);
            bool vehicle = type >= 10;
            List<MapUnitMember> members = [];
            // Distinct ordinary formation silhouettes are a remake presentation choice.
            // These authored ground-plane slots are independent of officer slots and collision footprints.
            for (int i = 0; i < (vehicle ? 3 : 25); i++)
            {
                int row = i / 5, column = i % 5 - 2;
                var point = id switch
                {
                    0 => (column * 10, row * 10 + Math.Abs(column) * 7),
                    1 => (column * 8, row * 12 + Math.Abs(column) * 14),
                    2 => (column * 12, row * 10 - Math.Abs(column) * 8),
                    3 => (column * 14, row * 8 - Math.Abs(column) * 14),
                    4 => (column * 6, row * 16),
                    5 => (column * 18, row * 6),
                    6 => (column * 11, row * 11),
                    7 => ((i % 3 - 1) * 9, i / 3 * 11),
                    _ => (column * 10, row * 10)
                };
                if (vehicle) point = (i == 0 ? 0 : i == 1 ? -40 : 40, 0);
                members.Add(new(i == 0 ? 1u : (uint)(i * (vehicle ? 10000 : 1000) + 1), bodies[type].Id,
                    point.Item1, point.Item2 - (vehicle ? 0 : 20)));
            }
            if (!vehicle)
            {
                var ordered = members.OrderBy(m => (long)m.X * m.X + (long)m.Y * m.Y).ThenBy(m => m.Y).ThenBy(m => m.X).ToArray();
                var origin = ordered[0];
                members = ordered.Select((m, i) => m with { MinimumSoldiers = i == 0 ? 1u : (uint)(i * 1000 + 1),
                    X = m.X - origin.X, Y = m.Y - origin.Y }).ToList();
            }
            if (id is >= 8 and <= 11)
                for (int i = 0; i < 8; i++) members.Add(new((uint)(i * 2000 + 1), bodies[0].Id, (i % 4 - 2) * 12, 22 + i / 4 * 12));
            return new MapUnitFormation(id, members.ToArray(), -64 - (vehicle ? 16 : 0), Int(0x26416c + 8 * type));
        }).ToArray();
        Dictionary<UnitCondition, MapUnitEffect> conditions = [];
        foreach (var (condition, resource) in new[] { (UnitCondition.Disguised, 1246),
            (UnitCondition.NoFormation, 1250), (UnitCondition.Confused, 1248),
            (UnitCondition.Routed, 1249), (UnitCondition.HighSpirits, 1247) })
            conditions.Add(condition, new(Export($"unit-condition-{condition.ToString().ToLowerInvariant()}", resource, 125, true), 12, 0));
        var influence = new[] { 1u, 5001u, 10001u, 20001u }.Select((minimum, i) =>
        {
            int size = new[] { 0, 1, 3, 5 }[i];
            return new MapUnitInfluence(minimum, new(Export($"unit-influence-{i}", 5646 + i, 125, true),
                Int(0x21527c + 12 * size), Int(0x215280 + 12 * size)));
        }).ToArray();
        return new(bodies, formations, conditions, influence);
    }
}
