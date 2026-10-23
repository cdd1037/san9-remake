using San9.Content.Interface;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class EndingSceneAnimationTests
{
    [Fact]
    public void SkippingEntranceDoesNotDismissThePageAndSuccessionWaitsAfterScrolling()
    {
        var animation = new EndingSceneAnimation(3.2, .384, 2.5);
        animation.Advance(1);
        Assert.False(animation.Reading);
        animation.SkipEntrance();
        Assert.True(animation.Reading);
        Assert.False(animation.Complete);
        animation.Leave();
        animation.Advance(.384);
        Assert.Equal(1, animation.ScrollProgress);
        Assert.False(animation.Complete);
        animation.Advance(2.5);
        Assert.True(animation.Complete);
    }

    [Fact]
    public void LoopingSceneryAndSuccessionUseElapsedTimeInsteadOfRenderFrameCount()
    {
        var layer = new EndingScrollLayer(new("cloud", 512, 96, .5f), -816, 1024, 1024);
        Assert.Equal(512, EndingSceneAnimation.ScrollX(layer, 0, 20));
        Assert.Equal(1024, EndingSceneAnimation.ScrollX(layer, 66.4, 20));
        var whole = new EndingSceneAnimation(0, .384, 5);
        var partitioned = new EndingSceneAnimation(0, .384, 5);
        whole.Leave(); partitioned.Leave();
        whole.Advance(1);
        for (int i = 0; i < 100; i++) partitioned.Advance(.01);
        Assert.Equal(whole.ScrollProgress, partitioned.ScrollProgress);
        Assert.Equal(whole.Complete, partitioned.Complete);
        partitioned.SkipExit();
        Assert.True(partitioned.Complete);
    }
}
