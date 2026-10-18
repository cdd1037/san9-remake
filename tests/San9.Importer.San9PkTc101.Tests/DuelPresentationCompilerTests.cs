using San9.Content.Interface;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class DuelPresentationCompilerTests
{
    [Fact]
    public void NativeInterfaceResourcesAndParticleCompositeSurviveSemanticCompilation()
    {
        // Distinct input coordinates ensure the output consumes the source layout rather than a client fallback.
        int Read(int address) => address is >= 0x617E28 and < 0x617E68 ? address - 0x617E28 : 8;
        var duel = DuelPresentationCompiler.Compile((archive, image, palette) => $"{archive}/{image}", Read,
            id => $"sound/{id}", type => [new BattleSprite($"effect/{type}", 1, 100)]);
        Assert.Equal(new BattlePoint(24, 104), duel.Interface.BackdropPosition);
        Assert.Equal("072746", duel.Interface.TintColor);
        Assert.Equal(179 / 256f, duel.Interface.TintOpacity);
        Assert.Equal(new BattlePoint(0, 4), duel.Interface.Panels[0].Position);
        Assert.Equal(new BattlePoint(8, 12), duel.Interface.Panels[1].Position);
        Assert.Equal(new BattlePoint(32, 36), new(duel.Interface.Panels[0].Portrait.X, duel.Interface.Panels[0].Portrait.Y));
        Assert.Equal(new[] { "g_ikki/17", "g_ikki/18" }, duel.Interface.Panels.Select(p => p.Asset));
        Assert.Equal(new[] { "g_ikki/9", "g_ikki/10" }, duel.Interface.Panels.Select(p => p.HealthAsset));
        Assert.All(duel.Interface.Assets, asset => Assert.Contains(asset, duel.Assets));
        Assert.All(duel.Particles, p =>
        {
            Assert.True(p.Animation.Additive);
            Assert.Equal(165 / 256f, p.Animation.Opacity);
        });
        Assert.All(duel.Bodies.SelectMany(b => b.Actions.Values), a => Assert.False(a.Additive));
    }
}
