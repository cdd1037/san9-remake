using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

internal static class DuelPresentationCompiler
{
    internal static DuelPresentationDefinition Compile(Func<string, int, int, string> asset,
        Func<int, int> read, Func<int, string> sound, Func<int, BattleSprite[]> dynamic)
    {
        Dictionary<string, DecisionMessageDefinition> messages = [];
        void Speech(string role, int id) => messages.Add(role, new($"messages.{id}", new()
        { ["person.0"] = "actor", ["person.1"] = "opponent", ["force.0"] = "force" }, "actor"));
        foreach (var (role, id) in new[] { ("entry",6995),("answer",7029),("victory",7057),("strong",7134),
            ("finisher",7148),("strong-hit",7141),("finisher-hit",7155),("parry",7162) }) Speech(role, id);
        messages.Add("introduction", new("messages.7133", new()
        { ["person.0"]="actor", ["person.1"]="source-commander", ["person.2"]="opponent", ["person.3"]="target-commander" }));
        messages.Add("won", new("messages.10098", new() { ["person.0"]="actor", ["person.1"]="opponent" }));
        messages.Add("lost", new("messages.10099", new() { ["person.0"]="actor", ["person.1"]="opponent" }));
        messages.Add("death", new("messages.7167", new() { ["person.0"]="actor" }));
        string Image(int i) => asset("g_ikki", i, i < 9 ? i : i >= 70 ? 18 : i >= 60 ? 17 : read(0x665590 + (i - 3) * 8 + 4) - 107);
        DuelBody Body(bool female, bool right) => new(female, right, Enum.GetValues<DuelAction>().ToDictionary(a => a, a =>
        {
            int action = a > DuelAction.Defeated ? 6 : (int)a;
            int count = a == DuelAction.EntryPreparation ? 14 : a == DuelAction.FinisherPreparation ? 20 : 0;
            int frames = a == DuelAction.EntryPreparation ? 0x617C30 : 0x617CA0;
            int intervals = a == DuelAction.EntryPreparation ? 0x617C68 : 0x617CF0;
            return new DuelAnimation(Image((female ? 60 : 40) + (right ? 10 : 0) + action), read(0x618400 + action * 4), 100)
            { Frames = Enumerable.Range(0, count).Select(i => read(frames + i * 4)).ToArray(),
              Intervals = Enumerable.Range(0, count).Select(i => read(intervals + i * 4)).ToArray() };
        }));
        Dictionary<string, string> sounds = new[] { ("ambience",164),("entry",148),("return",143),("parry",144),
            ("defeat",145),("win",146),("lose",147),("prepare",149),("hit",150),("heavy-hit",151),("strong",152),
            ("cheer",153),("despair",154),("entry-prepare",155),("finisher",156),
            ("finisher-effect",137),("resolution-effect",138) }.ToDictionary(p => p.Item1, p => sound(p.Item2));
        BattleSprite[] Effect(int type)
        {
            var frames = dynamic(type);
            return frames.Select(f => f with { FrameMilliseconds = Math.Max(1, 800 / frames.Length) }).ToArray();
        }
        BattlePoint Point(int address) => new(read(address), read(address + 4));
        BattlePoint[] Points(int address, int count) => Enumerable.Range(0, count).Select(i => Point(address + i * 8)).ToArray();
        DuelUiRect Rect(int address) => new(read(address), read(address + 4), read(address + 8) - read(address), read(address + 12) - read(address + 4));
        return new(messages, [Body(false,false),Body(false,true),Body(true,false),Body(true,true)],
            [new([0],[2],Image(1)),new([0],[3],Image(2)),new([0],[0,1],Image(0)),
             new([3,5,8],[2],Image(5)),new([3,5,8],[3],Image(6)),new([3,5,8],[0,1],Image(4)),
             new([6,9,10,12,15,16],[0,1,2,3],Image(7)),new([11],[0,1,2,3],Image(8))], Image(3), sounds,
            Enumerable.Range(19,8).Select(Effect).ToArray(), Effect(27))
        {
            Interface = new(new(24, 104), "072746", 179 / 256f,
                Enumerable.Range(0, 2).Select(side => new DuelPanelLayout(side != 0, Image(17 + side), Point(0x617E28 + side * 8),
                    new(read(0x617E48 + side * 8), read(0x617E4C + side * 8), 160, 180),
                    Rect(0x6657A8 + side * 16), Rect(0x6657C8 + side * 16), Rect(0x6657E8 + side * 16),
                    Rect(0x665808 + side * 16), Image(9 + side), Point(0x617E58 + side * 8))).ToArray(),
                Enumerable.Range(0, 2).Select(side => new DuelSpeechLayout(side != 0,
                    Image(11 + side * 3), Image(12 + side * 3), Image(13 + side * 3),
                    Point(0x617E38 + side * 8), 336, new(0, 20), 336, 28)).ToArray()),
            Flags = new(Points(0x6182A8, 11), Points(0x618300, 11)),
            Particles = Enumerable.Range(0, 3).Select(kind => new DuelParticleDefinition((DuelParticleKind)kind,
                new(Image(37 + kind), read(0x618498 + kind * 4), 100) { Additive = true, Opacity = 165 / 256f },
                Points(0x618358 + (kind == 0 ? 0 : kind == 1 ? 1 : 13) * 8, kind == 0 ? 1 : kind == 1 ? 6 : 4),
                Points(0x618358 + (kind == 0 ? 0 : kind == 1 ? 7 : 17) * 8, kind == 0 ? 1 : kind == 1 ? 6 : 4))).ToArray(),
            Positions = new(Point(0x618148), Point(0x618150), Point(0x618158), Point(0x618160), 60),
            Audience = Enumerable.Range(0, 6).Select(group =>
            {
                int descriptor = 0x618450 + group * 12, image = read(descriptor) - 30006 + 9;
                int counts = read(descriptor + 8), positions = read(0x61827C + group * 8);
                DuelAnimation Animation(int action) => new(Image(image + action), read(counts + action * 4), 100);
                return new DuelAudience(group >= 3, Animation(0), Animation(1), Animation(2),
                    Enumerable.Range(0, read(0x618278 + group * 8)).Select(i => new BattlePoint(read(positions + i * 8), read(positions + i * 8 + 4))).ToArray());
            }).ToArray()
        };
    }
}
