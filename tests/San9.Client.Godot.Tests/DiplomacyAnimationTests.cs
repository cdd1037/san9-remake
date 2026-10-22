using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class DiplomacyAnimationTests
{
    [Fact]
    public void SaluteReturnsThroughFramesAndBowReturnsDirectlyToRest()
    {
        var animation = new DiplomacyAnimation();
        animation.Play(DiplomacyGesture.Salute, 10, 100);
        animation.Advance(.9);
        Assert.Equal(9, animation.Frame);
        animation.Advance(.1);
        Assert.Equal(8, animation.Frame);
        animation.Advance(.85);
        Assert.Equal(0, animation.Frame);
        Assert.False(animation.IsComplete);
        animation.Advance(.06);
        Assert.True(animation.IsComplete);
        animation.Play(DiplomacyGesture.Bow, 10, 143);
        animation.Advance(1.3);
        Assert.Equal(9, animation.Frame);
        animation.Advance(.14);
        Assert.True(animation.IsComplete);
        Assert.Equal(0, animation.Frame);
    }

    [Fact]
    public void SkipSettlesOnlyCurrentGestureAndNewGestureStartsAtFirstFrame()
    {
        var animation = new DiplomacyAnimation();
        animation.Play(DiplomacyGesture.Salute, 10, 100);
        animation.Advance(.4); animation.Skip(); animation.Advance(20);
        Assert.True(animation.IsComplete);
        animation.Play(DiplomacyGesture.Bow, 6, 80);
        Assert.Equal(0, animation.Frame);
        Assert.False(animation.IsComplete);
        animation.Advance(.2);
        Assert.Equal(2, animation.Frame);
    }
}
