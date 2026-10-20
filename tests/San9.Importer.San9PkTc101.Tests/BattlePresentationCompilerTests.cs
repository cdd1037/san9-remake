using San9.Content.Interface;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class BattlePresentationCompilerTests
{
    [Theory]
    [InlineData(23, 9, 6794, 10781, 10799, 10808, 10790, null)]
    [InlineData(25, 12, 6844, 10867, 10885, 10892, 10876, null)]
    [InlineData(26, 4, 6943, 11037, 11053, 11062, 11046, 17)]
    [InlineData(27, 11, 6816, 10817, 10839, 10848, 10830, null)]
    [InlineData(30, 5, 6965, 11071, 11087, 11094, 11080, 18)]
    [InlineData(35, 2, 6868, 10901, 10919, 10928, 10910, null)]
    [InlineData(36, 0, 6893, 10937, 10955, 10964, 10946, null)]
    [InlineData(37, 1, 6913, 10971, 10989, 10998, 10980, null)]
    [InlineData(38, 3, 6928, 11005, 11023, 11032, 11014, null)]
    public void IntelligencePresentationKeepsMessagesAndEffectsWithTheSourceTactic(
        int tacticId, int nativeType, int announcement, int action, int hit, int reduced, int failed, int? dynamicType)
    {
        var tactics = BattlePresentationCompiler.CompileIntelligenceTactics(
            type => new BattleSprite($"native/{type}", 8, 100),
            type => Enumerable.Range(0, 4).Select(frame => new BattleSprite($"dynamic/{type}/{frame}", 1, 100)).ToArray());
        var tactic = tactics[tacticId];
        Assert.Equal($"native/{nativeType}", tactic.MapEffect!.Asset);
        foreach (var (role, key) in new[] { ("announcement", announcement), ("action", action),
            ("hit", hit), ("reduced", reduced), ("failed", failed) })
            Assert.Equal($"messages.{key}", tactic.Messages[role].Key);
        if (dynamicType is int screen)
        {
            Assert.Equal(11, tactic.Prelude.Length);
            Assert.Equal($"dynamic/{screen}/0", tactic.Prelude[0].Asset);
            Assert.Equal(2875, tactic.Prelude.Sum(s => s.FrameMilliseconds));
            Assert.All(tactic.Prelude.Skip(4), s => Assert.Equal($"dynamic/{screen}/3", s.Asset));
            Assert.True(tactic.Prelude[4].Opacity < tactic.Prelude[3].Opacity);
            Assert.True(tactic.Prelude[^1].Opacity < tactic.Prelude[4].Opacity);
        }
        else Assert.Empty(tactic.Prelude);
        Assert.Equal(tacticId == 25 ? "messages.10736" : "messages.10729", tactic.Messages["defensive"].Key);
        Assert.Equal("target-force", tactics[25].Messages["announcement"].Bindings["force.0"]);
    }
}
