using San9.Content.Interface;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class BattleZoomNumberPlaybackTests
{
    private static BattleZoomNumbers Definition => new(new("small",20,28,15),new("total",48,96,48),
        new(733,144),48,10,"roll","digit","completed","blocked");

    [Theory]
    [InlineData(false,"completed")]
    [InlineData(true,"blocked")]
    public void RollingDigitsStopRightToLeftAndSoundSharesTheirBoundaries(bool blocked, string finalSound)
    {
        int consumed = 0;
        var playback = new BattleZoomNumberPlayback(Definition, 1234, blocked, 2, n => { consumed++; return 0; });
        Assert.Equal(4, consumed);
        Assert.Empty(playback.SampleTotal(1.999));
        Assert.Single(playback.SampleTotal(2));
        Assert.True(playback.IsRolling(2));
        Assert.False(playback.IsRolling(playback.End));
        var final = playback.SampleTotal(playback.End).OrderBy(g => g.X).ToArray();
        Assert.Equal(new double[] { 96,192,288,384 }, final.Select(g => g.SourceY));
        Assert.Equal(733, final[^1].X);
        Assert.Equal(playback.Digits.Select(d => d.End), playback.Sounds.Select(s => s.Time));
        Assert.Equal(finalSound, playback.Sounds[^1].Role);
        Assert.Equal(playback.SampleTotal(2.1), playback.SampleTotal(2.1));
        Assert.Equal(4, consumed);
    }

    [Fact]
    public void LastInitialRollingPositionWrapsAtTheFirstTick()
    {
        var playback = new BattleZoomNumberPlayback(Definition,4,false,0,n=>3);
        Assert.Equal(936,Assert.Single(playback.SampleTotal(0)).SourceY);
        Assert.Equal(0,Assert.Single(playback.SampleTotal(.011)).SourceY);
        Assert.Equal(.17,playback.End,6);
        Assert.Equal(384,Assert.Single(playback.SampleTotal(playback.End)).SourceY);
    }

    [Fact]
    public void SmallNumbersBounceThenFadeWithoutAddingASign()
    {
        var playback = new BattleZoomNumberPlayback(Definition,123,false,0,n=>0);
        var hit = new BattleZoomImpact(1,300,200,123,true);
        var initial = playback.SampleSmall(hit,1).ToArray();
        Assert.Equal(3,initial.Length);
        Assert.Equal(new double[] { 28,56,84 },initial.Select(g=>g.SourceY));
        Assert.All(playback.SampleSmall(hit,1.05),g=>Assert.Equal(194,g.Y));
        Assert.All(playback.SampleSmall(hit,1.45),g=>Assert.Equal(.5f,g.Opacity));
        Assert.Empty(playback.SampleSmall(hit,1.768));
    }
}
