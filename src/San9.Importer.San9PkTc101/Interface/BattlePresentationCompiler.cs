using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;
using San9.Content.Images;
using San9.Content.Interface;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Interface;

/// <summary>TC101 display descriptors and speech dispatch verified against the project IDA database.</summary>
internal static class BattlePresentationCompiler
{
    internal static (BattlePresentationDefinition Definition, (string Role, string File)[] Sources) Compile(byte[] exe, TextureLibrary library)
    {
        using var pe = new PEReader(new MemoryStream(exe));
        int Raw(int offset) => BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(offset, 4));
        int Int(int address) => BinaryPrimitives.ReadInt32LittleEndian(pe.GetSectionData(address - 0x400000).GetContent(0, 4).AsSpan());
        List<(string Role, string File)> sources = [];
        string Sound(int index)
        {
            string role = $"battle-sound-{index}";
            if (sources.Any(s => s.Role == role)) return role;
            var bytes = pe.GetSectionData(Int(0x64E9A8 + index * 4) - 0x400000).GetContent(0, 64).ToArray();
            sources.Add((role, Encoding.ASCII.GetString(bytes, 0, Array.IndexOf(bytes, (byte)0)).Replace('\\', '/')));
            return role;
        }
        string Asset(string archive, int image, int palette) => library.Archives.Single(a => a.Id == archive)
            .Variants.Single(v => v.ImageIndex == image && v.PaletteIndex == palette).Image.Id;
        Dictionary<int, int> palettes = [];
        for (int i = 0; i < 1145; i++) palettes[Raw(0x2601d0 + i * 8) & 65535] = Raw(0x2601d4 + i * 8) - 21;
        BattleSprite Native(int type)
        {
            // Read the VA table through PE sections; native descriptors are independent of the palette table.
            int address = 6701424 + type * 20, resource = Int(address);
            return new(Asset("g_effect", resource - 5611, palettes[resource]), Int(address + 4), 100, Int(address + 12),
                Int(address + 8) != 0, Sound(Int(address + 16)))
            { Opacity = Int(address + 8) != 0 ? 178 / 256f : 1 };
        }
        BattleSprite? BodySprite(int type, int action)
        {
            int offset = 0x2635bc + type * 52 + action * 8;
            return Raw(offset) == -1 ? null : new(Asset("g_zoomin", Raw(offset + 4), Raw(0x2635b8 + type * 52)), 8, 125);
        }
        BattleZoomBody Body(int type, int mode, bool target = false, int facilityGroup = -1)
        {
            var body = target
                ? new BattleZoomBody(BodySprite(type, 0)!, BodySprite(type, 1), BodySprite(type, 2), null)
                : new BattleZoomBody(BodySprite(type, 0)!, BodySprite(type, 4), BodySprite(type, 3), BodySprite(type, 5));
            int blockTable = facilityGroup switch { 1 => 0x664D98, 2 => 0x664DB0, _ => 0x664D80 };
            return body with
            {
                Motion = BattleBodyCompiler.Motion(mode, type),
                FeedbackSound = target ? Sound(type is 21 or 22 ? 96 : type is 19 or 20 ? 95 : 94) : null,
                DamageEffects = type is >= 19 and <= 22 ? [new(new(Asset("g_zoomin",type==21?133:type==22?134:132,type==21?90:type==22?91:89),8,125,Additive:true)
                    {Centered=false,Opacity=153/256f},[type switch {19=>new(-35,-33),20=>new(-46,-30),21=>new(-102,-94),_=>new BattlePoint(-130,-135)}],100,false,false)] : [],
                Effects = target ? [] : BattleBodyCompiler.Effects(type, mode,
                    (image, palette, columns, interval, additive) => new(Asset("g_zoomin", image, palette), columns, interval, Additive: additive)
                    { Centered = false, Opacity = additive ? 166 / 256f : 1 }, Sound),
                Wake = type is >= 13 and <= 16 or >= 19 and <= 22 ? new(Asset("g_zoomin", type is 15 or 21 ? 130 : type is 16 or 22 ? 131 : 129,
                    type is 15 or 21 ? 87 : type is 16 or 22 ? 88 : 86), 8, 125, Additive: true)
                { Centered = false, Opacity = 166 / 256f } : null,
                WakeOffset = type is 15 or 21 ? new(-7, 69) : type is 16 or 22 ? new(-21, 91) : type is 14 or 20 ? new(7, 49) : new(2, 48),
                Formation = BattleFormationCompiler.Compile(mode, type, facilityGroup, Int),
                ImpactX = type is 7 or 8 or 9 ? 36 : type is 13 or 14 or 19 or 20 ? 47 : type is 15 or 21 ? 72 : type is 16 or 22 ? 96 : 28,
                ImpactY = type is 7 or 8 or 9 ? 50 : type is 13 or 14 or 19 or 20 ? 69 : type is 15 or 21 ? 100 : type is 16 or 22 ? 136 : 44,
                Projectiles = type == 13 ? 2 : type == 15 ? 5 : type == 16 ? 8 : 1,
                ProjectileStartFrame = type == 10 ? 4 : type == 12 ? 0 : 3,
                BlockPoints = Enumerable.Range(0, 3).Select(i => new BattlePoint(Int(blockTable - (3 - i) * 8), Int(blockTable + 4 - (3 - i) * 8))).ToArray()
            };
        }
        BattleZoomBody SourceBody(int type, int mode, int facilityGroup = -1) => Body(type, mode, facilityGroup: facilityGroup) with
        {
            AdvanceSound = Sound(mode is 1 or 2 ? 52 : mode is 4 or 5 ? 53 : mode == 15 ? type is 13 or 14 ? 54 : 55 :
                type is 7 or 8 or 9 ? 49 : type is 13 or 14 ? 50 : type is 15 or 16 ? 51 : 48),
            AttackSound = Sound(mode is 7 or 8 ? 84 : mode is 1 or 2 or 4 or 5 ? 80 : mode == 15 ? type is 13 or 14 ? 82 : 83 :
                mode == 12 ? 77 : mode == 13 ? 81 : type is >= 9 and <= 12 ? 77 : type is 13 or 14 ? 78 : type is 15 or 16 ? 79 : 76)
        };
        BattleOfficerDisplay Officers(int type, int dx = 0, int dy = 0) => new(BodySprite(type, 0)!,
            Enumerable.Range(0, 5).Select(i => new BattlePoint(Int(0x6653A8 + type * 40 + i * 8) + dx,
                Int(0x6653AC + type * 40 + i * 8) + dy)).ToArray(),
            new(type == 0 ? 96 : 64, type == 0 ? 48 : 32),
            new(Int(0x665398 + type * 8), Int(0x66539C + type * 8)), 0);
        BattleSprite ZoomSprite(int image) => new(Asset("g_zoomin", image, image - 43), 8, 100, Additive: true);
        BattleSprite[] Dynamic(int type)
        {
            int first = Raw(0x263c40 + type * 12), count = Raw(0x263c44 + type * 12);
            return Enumerable.Range(first, count).Select(i => new BattleSprite(Asset("g_dyneff", i, type), 1, 100,
                Sound: Sound(Raw(0x263c48 + type * 12)))).ToArray();
        }
        DecisionMessageDefinition Speech(int id, string speaker, string other) => new($"messages.{id}", new()
        { ["person.0"] = speaker, ["person.1"] = other, ["tactic.0"] = "tactic", ["force.0"] = "force" }, speaker);
        Dictionary<string, DecisionMessageDefinition> common = [];
        void Report(string role, int id, params (string, string)[] values) => common.Add(role, new($"messages.{id}", values.ToDictionary(p => p.Item1, p => p.Item2)));
        foreach (var (role, id) in new[] { ("activation-own", 9970), ("activation-enemy", 9971) })
            Report(role, id, ("person.0", "actor"), ("unit.0", "source"), ("unit.1", "target"), ("tactic.0", "tactic"));
        foreach (var (role, id) in new[] { ("chain-own", 10102), ("chain-enemy", 10103) })
            Report(role, id, ("person.0", "actor"), ("unit.0", "target"), ("tactic.0", "tactic"));
        Report("support", 10083, ("person.0", "actor"), ("unit.0", "source"), ("tactic.0", "tactic"));
        Report("link", 9973, ("person.0", "actor"), ("tactic.0", "tactic"));
        Report("blocked", 9974, ("person.0", "responder"));
        Report("damage", 10064, ("unit.0", "target"), ("number.0", "damage"));
        Report("defection", 9980, ("unit.0", "target"), ("person.0", "source-commander"));
        Report("healed", 9982, ("person.0", "target-commander"), ("number.0", "damage"));
        common.Add("hit-reaction", Speech(6706, "target-commander", "source-commander"));
        common.Add("block-reaction", Speech(6716, "responder", "actor"));
        common.Add("block-reply", Speech(6725, "actor", "responder"));
        common.Add("chain-confused", Speech(7115, "actor", "target-commander"));
        common.Add("chain-unformed", Speech(7118, "actor", "target-commander"));
        for (int i = 0; i < 4; i++) common.Add($"link-{i}", Speech(6694 + i * 3, "actor", "target-commander"));
        common.Add("sniping-action", new("messages.6687", [], "actor"));
        foreach (var (role, key) in new[] { ("fatal", 6688), ("heavy", 6689), ("light", 6690) })
            common.Add("sniping-" + role, new($"messages.{key}", new() { ["person.0"] = "responder" }, "responder"));
        Report("sniping-own", 9969, ("person.0", "responder"), ("person.1", "actor"), ("string.0", "injury"));
        Report("sniping-enemy", 10119, ("person.0", "actor"), ("person.1", "responder"), ("string.0", "injury"));
        Dictionary<int, TacticPresentationDefinition> tactics = [];
        foreach (var (id, key) in new[] { (0,6691),(1,6728),(2,7121),(3,7124),(4,6734),(5,6731),
            (6,6737),(7,6742),(8,7127),(9,6747),(10,6752),(11,6757),(31,6764),(32,6769),(33,6775),(34,6778) })
        {
            int mode = id <= 11 ? id : id - 19;
            int body = mode switch { <= 2 => 2, <= 5 => 7, <= 8 => 9, 9 => 11, 10 => 11, 11 => 12, _ => 13 };
            int target = mode >= 12 ? 19 : 17;
            var sourceBody = SourceBody(body, mode);
            var zoom = new BattleZoomDefinition(sourceBody, Body(target, mode, true),
                ZoomSprite(139), ZoomSprite(140), Asset("g_zoomin", 141, 98), Asset("g_zoomin", 142, 99),
                Dynamic(mode).Select(s => s with { FrameMilliseconds = 300 }).ToArray())
            {
                Numbers = new(new(Asset("g_zoomin", 144, 101), 20, 28, 15), new(Asset("g_zoomin", 143, 100), 48, 96, 48),
                    new(733, 144), 48, 10, Sound(120), Sound(118), Sound(119), Sound(118)),
                FacilityNumberPositions = new[] { (0, 0), (1, 1), (2, 0), (3, 1), (4, 2), (5, 2), (6, 0) }
                    .ToDictionary(p => p.Item1, p => p.Item2 switch { 0 => new BattlePoint(694, 113), 1 => new(644, 204), _ => new(624, 264) }),
                AdvanceSound = sourceBody.AdvanceSound!,
                CountrySources = mode <= 2 ? new() { [4] = SourceBody(5, mode), [7] = SourceBody(3, mode), [8] = SourceBody(4, mode) } :
                    mode is >= 3 and <= 5 ? new() { [6] = SourceBody(8, mode) } : [],
                ImperialSource = mode <= 2 ? SourceBody(6, mode) : null,
                FeedbackSound = Sound(mode >= 12 ? 95 : 94),
                Officers = mode < 12 ? Officers(0) : null,
                FacilityOfficers = new[] { (0, 1), (2, 1), (4, 2), (5, 2), (6, 1) }
                    .ToDictionary(p => p.Item1, p => p.Item2 == 1 ? Officers(1, -120, -90) : Officers(1, -64, 32)),
                AttackSound = sourceBody.AttackSound!,
                AmbienceSound = Sound(body is 9 or 11 or 12 ? 163 : 162),
                Action = BattleFormationCompiler.Action(mode),
                Wave = mode is 7 or 8 ? new(Asset("g_zoomin", Int(0x664DB4), Raw(0x2635b8 + 9 * 52)), 8, 125) : null,
                WaveEffects = mode is 7 or 8 ? BattleBodyCompiler.Effects(9, mode,
                    (image, palette, columns, interval, additive) => new(Asset("g_zoomin", image, palette), columns, interval, Additive: additive)
                    { Centered = false }, Sound).Select(e => e with { Origins = [new(4, 62), new(42, 62), new(22, 29), new(61, 29)] }).ToArray() : [],
                DamageBands = [new(2501, 2), new(4500, 3), new(6500, 4), new(9000, 5), new(12500, 6), new(20000, 7), new(null, 8)],
                Projectile = mode is 6 or 7 or 9 or 12 ? new(Asset("g_zoomin", 120, 77), 16, 64) :
                    mode is 8 or 10 or 11 or 13 ? new(Asset("g_zoomin", 121, 78), 16, mode == 11 ? 32 : 64) : null,
                SourceFormations = new() { [21] = SourceBody(14, mode), [26] = SourceBody(14, mode), [22] = SourceBody(15, mode), [23] = SourceBody(16, mode) },
                TargetFormations = new() { [21] = Body(20, mode, true), [26] = Body(20, mode, true), [22] = Body(21, mode, true), [23] = Body(22, mode, true) },
                FacilitySources = new[] { (0, 1), (1, 0), (2, 1), (3, 0), (4, 2), (5, 2), (6, 1) }
                    .ToDictionary(p => p.Item1, p => SourceBody(mode == 11 ? 12 : 10, mode, p.Item2)),
                FacilityTargets = new[] { (0, 1), (1, 0), (2, 1), (3, 0), (4, 2), (5, 2), (6, 1) }
                    .ToDictionary(p => p.Item1, p => Body(18, mode, true, p.Item2))
            };
            tactics.Add(id, new(new() { ["action"] = Speech(key, "actor", "target-commander") }, null, zoom));
        }
        foreach (var (id, type, announcement, action, hit, reduced, failed) in new[] { (23,9,6794,10781,10799,10808,10790),
            (25,12,6816,10817,10839,10848,10830),(26,4,6844,10867,10885,10892,10876),(27,11,6943,11037,11053,11062,11046),
            (30,5,6965,11071,11087,11094,11080),(35,2,6868,10901,10919,10928,10910),(36,0,6893,10937,10955,10964,10946),
            (37,1,6913,10971,10989,10998,10980),(38,3,6928,11005,11023,11032,11014) })
            tactics.Add(id, new(new()
            {
                ["announcement"] = new($"messages.{announcement}", new()
                { ["person.0"] = "actor", ["person.1"] = "target-commander", ["tactic.0"] = "tactic", ["force.0"] = "target-force" }, "actor"),
                ["action"] = Speech(action, "actor", "target-commander"),
                ["hit"] = Speech(hit, "target-commander", "actor"),
                ["reduced"] = Speech(reduced, "responder", "actor"),
                ["failed"] = Speech(failed, "responder", "actor"),
                ["united"] = Speech(10745, "target-commander", "actor"),
                ["counter-confusion"] = Speech(10754, "responder", "actor"),
                ["counter-trap"] = Speech(10763, "responder", "actor"),
                ["defensive"] = Speech(id == 26 ? 10736 : 10729, "target-commander", "actor")
            }, Native(type), null)
            {
                UnitedResponse = id == 23,
                FormationResponse = id == 23,
                CounterResponse = id == 23 ? "counter-confusion" : id >= 35 ? "counter-trap" : null,
                Prelude = id is 27 or 30 ? Dynamic(id == 27 ? 17 : 18) : []
            });
        foreach (var (id, type, announcement, action, hit) in new[] { (28, 14, 6836, 10857, 10866), (29, 6, 6973, 11099, 11104) })
            tactics.Add(id, new(new()
            {
                ["announcement"] = new($"messages.{announcement}", new() { ["person.0"] = "actor", ["tactic.0"] = "tactic" }, "actor"),
                ["action"] = Speech(action, "actor", "target-commander"),
                ["hit"] = Speech(hit, "soldier", "actor")
            }, Native(type), null)
            { Support = true, EffectAtSource = id == 28 });
        Dictionary<int, BattleSprite[]> burn = [], seize = [];
        foreach (int type in Enumerable.Range(0, 10))
        {
            var (address, count) = type switch
            {
                0 => (0x6649D8, 7),
                1 or 6 or 7 or 8 or 9 => (0x6649B0, 5),
                2 => (0x6649A0, 2),
                3 => (0x664990, 2),
                4 => (0x664978, 3),
                _ => (0x664960, 3)
            };
            burn.Add(type, Enumerable.Range(0, count).Select(i => new BattleSprite(Asset("g_effect", 3, palettes[5614]), 8, 125,
                Int(address + i * 8 + 4), true, i == 0 ? Sound(28) : null)
            { OffsetX = Int(address + i * 8), Centered = false, Opacity = 0.5f }).ToArray());
            int ring = type switch { 0 => 0x664A38, 1 or 2 or 3 or 6 => 0x664A30, 4 or 5 => 0x664A28, 7 => 0x664A20, 8 => 0x664A18, _ => 0x664A10 };
            seize.Add(type, [new(Asset("g_effect",4,palettes[5615]),8,125,Int(ring+4),true,Sound(29))
                { OffsetX=Int(ring),Centered=false,Opacity=0.5f }]);
        }
        return (new(common, tactics, Sound(113), Sound(114))
        {
            ConditionEffects = new[] { (UnitCondition.Disguised,7),(UnitCondition.NoFormation,8),
                (UnitCondition.Confused,9),(UnitCondition.Routed,10),(UnitCondition.HighSpirits,15) }
                .ToDictionary(p => p.Item1, p => Native(p.Item2)),
            MapNumbers = new(Asset("g_effect", 51, palettes[5662]), 12, 20, 125, 8, 4),
            Scene = BattleSceneCompiler.Compile((image, palette) => Asset("g_zoomin", image, palette)),
            BuildingEffects = new() { ["burn"] = burn, ["seize"] = seize },
            SnipingEffect = Dynamic(16).Select(s => s with { FrameMilliseconds = 300 }).ToArray()
        }, sources.ToArray());
    }
}
