using San9.Content.Images;
using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

internal static class EndingPresentationCompiler
{
    internal static EndingPresentationDefinition Compile(TextureLibrary library)
    {
        string Asset(int image) => library.Archives.Single(a => a.Id == "g_ending").Variants.Single(v => v.ImageIndex == image).Image.Id;
        EndingSprite Sprite(int image, int x, int y, float alpha = 1) => new(Asset(image), x, y, alpha);
        // RE-0181: native scene layers and completed-stage markers, not a gallery of ending cards.
        var expansion = new EndingMapPresentation(Asset(0),
            [new(Sprite(4, 512, 96, .5f), -816, 1024, 1024), new(Sprite(5, 744, 144, .5f), -264, 1024, 1024),
             new(Sprite(6, 80, 472, .5f), -816, 1024, 1024)],
            [Sprite(1, 0, 88), Sprite(2, 920, 88), Sprite(45, 0, 0), Sprite(46, 0, 648)],
            [new(Sprite(10, 560, 104), Sprite(37, 576, 122)), new(Sprite(11, 368, 116), Sprite(38, 384, 136)),
             new(Sprite(12, 480, 320), Sprite(39, 496, 344)), new(Sprite(13, 184, 128), Sprite(40, 204, 176))]);
        var domestic = new EndingMapPresentation(Asset(3),
            [new(Sprite(7, 0, 240), -1024, 0, 1024), new(Sprite(8, 0, 384), -1024, 0, 1024), new(Sprite(9, 0, 0), -1024, 0, 1024)],
            [Sprite(45, 0, 0), Sprite(46, 0, 648)],
            [new(Sprite(18, 112, 88), Sprite(41, 166, 162)), new(Sprite(19, 200, 392), Sprite(42, 256, 460)),
             new(Sprite(20, 624, 408), Sprite(43, 690, 483)), new(Sprite(21, 704, 80), Sprite(44, 763, 157))]);
        Dictionary<string, EndingCue> cues = [];
        void Cue(string role, EndingSceneKind scene, int? picture = null, int stages = 0) =>
            cues.Add(role, new(scene, picture is int image ? Asset(image) : null, stages));
        Cue("introduction", EndingSceneKind.Opening);
        foreach (string policy in new[] { "expansion", "domestic" })
            for (int stage = 0; stage < 4; stage++)
            {
                string[] starts = policy == "expansion" ? ["start", "west-start", "trade-start", "final-start"] : ["start", "government-start", "defense-start", "culture-start"];
                Cue(policy + "-" + starts[stage], EndingSceneKind.Map, stages: stage);
            }
        foreach (var (role, image, stages) in new (string, int, int)[] {
            ("expansion-army-ready",22,1),("expansion-west-ready",23,2),("expansion-trade-ready",24,3),("expansion-empire",26,4),
            ("expansion-undersized",33,0),("expansion-soldier-shortage",33,0),("expansion-leadership-shortage",33,0),
            ("expansion-order-shortage",33,1),("expansion-war-shortage",33,1),("expansion-commerce-shortage",25,2),("expansion-naval-shortage",25,2),
            ("expansion-barbarians",34,3),("expansion-tactic-shortage",34,3),
            ("domestic-building-ready",27,1),("domestic-government-ready",28,2),("domestic-defense-ready",30,3),("domestic-culture-ready",31,4),
            ("domestic-undersized",29,0),("domestic-durability-shortage",29,0),("domestic-knowledge-shortage",29,0),
            ("domestic-trust-shortage",29,1),("domestic-affinity-conflict",36,1),("domestic-resource-shortage",33,2),("domestic-leadership-shortage",33,2),
            ("domestic-order-shortage",32,3),("domestic-treasure-shortage",32,3) }) Cue(role, EndingSceneKind.Inset, image, stages);
        Cue("succession-year", EndingSceneKind.SuccessionYear);
        Cue("stable-succession", EndingSceneKind.StableSuccession);
        Cue("unstable-succession", EndingSceneKind.UnstableSuccession);
        Cue("successor-conflict", EndingSceneKind.UnstableSuccession);
        string Archive(string id, int image = 0) => library.Archives.Single(a => a.Id == id).Variants.Single(v => v.ImageIndex == image).Image.Id;
        return new(Asset(47), expansion, domestic, Sprite(50, 240, 176), [Sprite(45, 0, 0), Sprite(46, 0, 648)],
            Asset(35), Asset(48), Asset(49), cues, [9], 0, 664, 1024, 3.2, 20, .1, .384, 2.5, 5, 2,
            Archive("g_klgpk"), Archive("g_titlpk"),
            [new(new(Archive("g_titlpk", 1), 428, -18), 8, .2), new(new(Archive("g_titlpk", 2), 296, 594), 8, .2)],
            new(Archive("g_titlpk", 3), 224, 680), 2);
    }
}
