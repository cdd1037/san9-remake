using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class SeasonChangeAnimationTests
{
    [Fact]
    public void LargeDeltaStopsForNewTerrainAndWaitingDoesNotConsumeTheFade()
    {
        var animation = new SeasonChangeAnimation();
        animation.Advance(20, true);
        Assert.True(animation.NeedsNewSeason);
        Assert.False(animation.IsComplete);
        double boundary = animation.Elapsed;
        float cover = animation.CoverAlpha;
        animation.Advance(20, false);
        Assert.Equal(boundary, animation.Elapsed);
        Assert.Equal(cover, animation.CoverAlpha);
        animation.Advance(.1, true);
        Assert.True(animation.CoverAlpha < cover);
        animation.Advance(20, true);
        Assert.True(animation.IsComplete);
        Assert.Equal(0, animation.CoverAlpha);
    }
}
