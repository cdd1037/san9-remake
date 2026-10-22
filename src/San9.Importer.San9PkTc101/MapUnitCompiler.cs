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
        string Export(string key, int resource, int milliseconds, int additiveWeight = 0, int frameCount = 8, int opacity = 256, bool paletteAlpha = false, bool fade = false)
        {
            var (archive, first, paletteBase) = resource >= 5611 ? (effects, 5611, 21) :
                resource >= 5000 ? (units, 5000, 0) : (markers, 1220, 311);
            var sheet = archive.Images[resource - first];
            int[] sequence = resource is 1246 or 1250 or 1247 ? [0, 1, 2, 3, 3, 3] : Enumerable.Range(0, frameCount).ToArray();
            var frames = Enumerable.Range(0, sequence.Length).Select(frame =>
            {
                string id = $"{key}-{frame}";
                var image = IndexedImageArchive.Decode(IndexedImageArchive.Frame(sheet, frameCount, 1, sequence[frame], 0),
                    archive.Palettes[palettes[resource] - paletteBase], true, paletteAlpha);
                // Fixed-weight additive consumers do not read palette alpha (RE-SAN9PK-0156).
                if (additiveWeight != 0)
                    for (int pixel = 0; pixel < image.Pixels.Length; pixel += 4)
                        for (int channel = 0; channel < 3; channel++)
                            image.Pixels[pixel + channel] = (byte)(image.Pixels[pixel + channel] * additiveWeight * (fade ? (1 + Math.Cos(Math.PI * frame / frameCount)) / 2 : 1) / 256);
                if (opacity != 256)
                    for (int pixel = 3; pixel < image.Pixels.Length; pixel += 4)
                        image.Pixels[pixel] = (byte)(image.Pixels[pixel] * opacity / 256);
                assets.Add(save(id, $"animations/{key}/{frame}.png", image));
                return id;
            }).ToArray();
            clips.Add(new(key, frames, milliseconds, []) { BlendMode = additiveWeight != 0 ? MapBlendMode.Additive : MapBlendMode.Alpha });
            return key;
        }
        using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(exe));
        int At(int address) => BinaryPrimitives.ReadInt32LittleEndian(pe.GetSectionData(checked(address - (int)pe.PEHeaders.PEHeader!.ImageBase)).GetContent(0, 4).AsSpan());
        MapUnitAttack? Attack(int type, int table)
        {
            var actions = Enumerable.Range(0, At(table + type * 16)).Select(i =>
            {
                int action = At(table + type * 16 + 4 * (i + 1));
                int resource = At(0x664a40 + 4 * (action * 20 + type));
                if (resource < 0) throw new InvalidDataException("Attack references an absent body action.");
                var directions = Enumerable.Range(0, 8).Select(direction =>
                {
                    string key = $"unit-action-{type}-{action}-{direction}";
                    return clips.Any(c => c.Id == key) ? key : Export(key, resource + direction, 100);
                }).ToArray();
                return new MapUnitAction(directions) { BlocksDayProgress = action == 7 };
            }).ToArray();
            return actions.Length == 0 ? null : new(actions,
                type == 13 ? MapProjectileKind.Stone : MapProjectileKind.Arrow, type == 13 ? 1 : 5)
            {
                Timings = CompileAttackTimings(type),
                ShotSpacingMilliseconds = 200
            };
        }
        var wakes = Enumerable.Range(0, 3).Select(i => new MapUnitEffect(Export($"unit-wake-{i}", 5643 + i, 125, 83),
            At(0x618b4c + i * 8), At(0x618b50 + i * 8))).ToArray();
        string dust = Export("unit-trail-dust", 5635, 125, frameCount: 4);
        var trails = Enumerable.Range(0, 3).Select(i => new MapUnitEffect(
            Export($"unit-trail-water-{i}", 5643 + i, 125, 83, fade: true),
            -effects.Images[32 + i].Width / 16, -effects.Images[32 + i].Height / 2)).ToArray();
        string landCombat = Export("unit-engagement-land", 5638, 125, paletteAlpha: true);
        string waterCombat = Export("unit-engagement-water", 5634, 125, 83, frameCount: 4);
        MapUnitParticle[] Engagement(int type)
        {
            int category = At(0x618778 + type * 4);
            int[] counts = [1, 3, 2, 5, 8], behind = [0, 0, int.MaxValue, 3, 7];
            int[] tables = [0x618840, 0, 0x618830, 0x618808, 0x6187c8];
            int[] local = [-16, 1, -30, 7, -1, 8];
            var sheet = units.Images[Int(0x263240 + 4 * type) - 5000];
            int cx = sheet.Width / 16 - Int(0x214980 + 8 * type);
            int cy = sheet.Height / 2 - Int(0x214984 + 8 * type);
            return Enumerable.Range(0, counts[category]).Select(i => new MapUnitParticle(
                category < 2 ? landCombat : waterCombat,
                cx + (category == 1 ? local[i * 2] : At(tables[category] + i * 8)),
                cy + (category == 1 ? local[i * 2 + 1] : At(tables[category] + i * 8 + 4)),
                i < behind[category] ? -1 : 1)).ToArray();
        }
        var bodies = Enumerable.Range(0, 20).Select(type => new MapUnitBody($"unit-body-{type}",
            Enumerable.Range(0, 8).Select(direction => Export($"unit-idle-{type}-{direction}",
                Int(0x263240 + 4 * type) + direction, 100)).ToArray(),
            Int(0x214980 + 8 * type), Int(0x214984 + 8 * type))
        {
            ImperialBodyId = type == 0 ? "unit-body-3" : null,
            MeleeAttack = Attack(type, 0x663fd0), RangedAttack = Attack(type, 0x663e90),
            Flags = CompileFlags(At, type),
            TrailChance = type >= 16 ? 100 : 6.25,
            Trail = type >= 16 ? trails[type <= 17 ? 0 : type - 17] : new(dust,
                units.Images[Int(0x263240 + 4 * type) - 5000].Width / 16 - Int(0x214980 + 8 * type) - 12,
                units.Images[Int(0x263240 + 4 * type) - 5000].Height / 2 - Int(0x214984 + 8 * type) - 10),
            Engagement = Engagement(type),
            Wake = type is 16 or 17 ? wakes[0] : type == 18 ? wakes[1] : type == 19 ? wakes[2] : null
        }).ToArray();
        var influenceClips = Enumerable.Range(0, 4).Select(i =>
            Export($"unit-influence-{i}", 5646 + i, 125, 72)).ToArray();
        var formations = CompileFormations(At, bodies, influenceClips);
        Dictionary<UnitCondition, MapUnitEffect> conditions = [];
        foreach (var (condition, resource) in new[] { (UnitCondition.Disguised, 1246),
            (UnitCondition.NoFormation, 1250), (UnitCondition.Confused, 1248),
            (UnitCondition.Routed, 1249), (UnitCondition.HighSpirits, 1247) })
            conditions.Add(condition, new(Export($"unit-condition-{condition.ToString().ToLowerInvariant()}", resource, 125, 128), 12, 0));
        MapProjectile Projectile(string name, int resource, int count, bool directional)
        {
            var sheet = units.Images[resource - 5000];
            var frames = Enumerable.Range(0, count).Select(i =>
            {
                string key = $"projectile-{name}-{i}";
                var image = IndexedImageArchive.Decode(IndexedImageArchive.Frame(sheet, count, 1, directional ? (i + 3) % count : i, 0),
                    units.Palettes[palettes[resource]], true, false);
                assets.Add(save(key, $"animations/projectile-{name}/{i}.png", image));
                return key;
            }).ToArray();
            return new(frames, 64, directional, sheet.Width / count / 2, sheet.Height / 2);
        }
        return new(bodies, formations, conditions, 0)
        {
            VisionSlices = Enumerable.Range(0, 16).Select(i => new MapUnitEffect(
                Export($"unit-vision-{i}", 1222 + i, 125, 128, frameCount: 1),
                -markers.Images[2 + i].Width / 2, -markers.Images[2 + i].Height / 2)).ToArray(),
            Projectiles = new()
            {
                [MapProjectileKind.Arrow] = Projectile("arrow", 5610, 16, true),
                [MapProjectileKind.Stone] = Projectile("stone", 5608, 1, false),
                [MapProjectileKind.Rockfall] = Projectile("rockfall", 5609, 4, false) with
                { Impact = new(Export("rockfall-impact", 5637, 100, frameCount: 4, opacity: 96),
                    -effects.Images[5637 - 5611].Width / 8, -effects.Images[5637 - 5611].Height / 2) }
            }
        };
    }

    internal static MapAttackTiming[] CompileAttackTimings(int body) => body == 13
        ? Enumerable.Range(0, 5).Select(n => new MapAttackTiming(n * 100, (n + 5) * 100)).ToArray()
        : [new(0, 0)];

    // Source-format tables are decoded here; consumers only receive complete projected layouts.
    internal static MapUnitFormation[] CompileFormations(Func<int, int> At, MapUnitBody[] bodies, string[] influenceClips)
    {
        uint[] thresholds = [1, .. Enumerable.Range(0, 6).Select(i => checked((uint)At(0x656c78 + i * 4)))];
        return Enumerable.Range(0, 28).Select(id =>
        {
            int primary = At(0x663d98 + id * 4);
            bool water = id is >= 20 and <= 26;
            int[] scales = water ? [id switch { 21 or 26 => 2, 22 => 4, 23 => 6, _ => 0 }] : Enumerable.Range(0, 7).ToArray();
            var layouts = scales.Select(scale =>
            {
                List<MapUnitMember> members = [];
                for (int group = 0; group < (water ? 1 : 5); group++)
                {
                    int body = group > 0 && id is >= 8 and <= 11 ? 0 : primary;
                    int category = At(0x6609d0 + body * 4);
                    // Large central bodies select the large layout for escort counts as well as offsets.
                    if (category == 0 && At(0x6609d0 + primary * 4) == 2) category = 2;
                    int count = At(0x660a20 + (scale * 3 + category) * 8 + (group == 0 ? 0 : 4));
                    int groups = At(0x660c70 + (scale * 3 + category) * 4);
                    int points = At(groups + group * 4);
                    for (int i = 0; i < count; i++)
                        members.Add(new(bodies[body].Id, At(points + i * 8), At(points + i * 8 + 4)) { Group = group, Index = i });
                }
                int effect = 0x616078 + scale * 12;
                return new MapUnitLayout(water ? 1 : thresholds[scale], members.ToArray(),
                    new(influenceClips[At(effect) - 5646], At(effect + 4), At(effect + 8)));
            }).ToArray();
            return new MapUnitFormation(id, layouts, At(0x66596c + 8 * primary));
        }).ToArray();
    }

    internal static MapUnitFlagPlacement[][] CompileFlags(Func<int, int> at, int body)
    {
        (int Position, int Depth)[] tables = body switch
        {
            16 => [(0x660e68, 0x660e48)],
            17 => [(0x660e08, 0x660de8)],
            18 => [(0x660da8, 0x660d88)],
            19 => [(0x660d48, 0x660d28), (0x660ce8, 0x660cc4)],
            _ => []
        };
        return Enumerable.Range(0, 8).Select(d => tables.Length == 0
            ? new[] { new MapUnitFlagPlacement(-at(0x64bb58) / 2, -16 - at(0x64bb5c), 0) }
            : tables.Select(t => new MapUnitFlagPlacement(at(t.Position + d * 8), at(t.Position + d * 8 + 4),
                at(t.Depth + d * 4))).ToArray()).ToArray();
    }
}
