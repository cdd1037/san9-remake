using San9.Content.Interface;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class BattleReinforcementPlaybackTests
{
    private static readonly BattleSprite[] Frames = Enumerable.Range(0, 4)
        .Select(i => new BattleSprite($"illustration-{i}", 1, 300)).ToArray();

    [Fact]
    public void ScreenEffectRetainsItsLastFrameUntilSteppedFadeCompletes()
    {
        Assert.Equal(1, BattleEffectFade.Opacity(-.001));
        Assert.Equal(223 / 255f, BattleEffectFade.Opacity(0));
        Assert.Equal(191 / 255f, BattleEffectFade.Opacity(.1));
        Assert.Equal(31 / 255f, BattleEffectFade.Opacity(.699));
        Assert.Equal(0, BattleEffectFade.Opacity(.7));
        Assert.Equal(BattleEffectFade.Opacity(.4), BattleEffectFade.Opacity(.4));
    }

    [Fact]
    public void SamplingAfterSkippedFramesMatchesContinuousPlaybackAndKeepsLastImageWhileFading()
    {
        Assert.Null(BattleReinforcementPlayback.Sample(Frames, 3.499));
        var first = BattleReinforcementPlayback.Sample(Frames, 3.5)!;
        Assert.Equal(Frames[0], first.Sprite);
        Assert.Equal(1, first.Opacity);
        Assert.Equal(Frames[1], BattleReinforcementPlayback.Sample(Frames, 3.8)!.Sprite);
        Assert.Equal(Frames[2], BattleReinforcementPlayback.Sample(Frames, 4.1)!.Sprite);
        Assert.Equal(Frames[3], BattleReinforcementPlayback.Sample(Frames, 4.4)!.Sprite);
        Assert.Equal(1, BattleReinforcementPlayback.Sample(Frames, 4.699)!.Opacity);
        for (double time = 3.5; time < 4.15; time += 0.017)
            _ = BattleReinforcementPlayback.Sample(Frames, time);
        var jumped = BattleReinforcementPlayback.Sample(Frames, 4.15)!;
        Assert.Equal(Frames[2], jumped.Sprite);
        Assert.Equal(jumped, BattleReinforcementPlayback.Sample(Frames, 4.15));
        var fade = BattleReinforcementPlayback.Sample(Frames, 4.85)!;
        Assert.Equal(Frames[^1], fade.Sprite);
        Assert.InRange(fade.Opacity, 0, 1);
        Assert.True(BattleReinforcementPlayback.Sample(Frames, 4.95)!.Opacity < fade.Opacity);
        Assert.Null(BattleReinforcementPlayback.Sample(Frames, 5.4));
        Assert.Null(BattleReinforcementPlayback.Sample([], 4));
    }

    [Fact]
    public void SequenceUsesAuthoredFrameCountAndDurationsInsteadOfAssumingFourImages()
    {
        BattleSprite[] frames = [new("first", 1, 200), new("last", 1, 500)];
        Assert.Equal("last", BattleReinforcementPlayback.Sample(frames, 3.75)!.Sprite.Asset);
        Assert.Equal(1, BattleReinforcementPlayback.Sample(frames, 4.15)!.Opacity);
        Assert.True(BattleReinforcementPlayback.Sample(frames, 4.25)!.Opacity < 1);
        Assert.Null(BattleReinforcementPlayback.Sample(frames, 4.9));
    }
}
