using System.Collections.Immutable;
using San9.Content.Interface;
using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class BattleOfficerPlaybackTests
{
    [Fact]
    public void ActorsExtendBeyondAuthoredSlotsAndSamplingKeepsFlagAndBodyTogether()
    {
        var actors = new BattleOfficerPlayback(new(new("body", 8, 125),
            [new(304,416),new(400,464),new(208,368),new(496,512),new(112,320)],
            new(96,48), new(9,-5), 0), Enumerable.Range(0,7).Select(i => new BattleOfficerVisual(i, 3, i == 0 ? -1 : i, i)).ToImmutableArray());
        var clips = actors.FlagClips.ToDictionary(id => id, id => new MapAnimationClip(id, [id+"-0",id+"-1"], 142, []));
        Assert.Empty(actors.Sample(-1, clips));
        var first = actors.Sample(.2, clips).ToArray();
        Assert.Equal(first, actors.Sample(.2, clips));
        var bodies = first.Where(f => f.Sprite.Asset == "body").ToArray();
        Assert.Equal(7, bodies.Length);
        Assert.Equal(7, bodies.Select(f => (f.X,f.Y)).Distinct().Count());
        Assert.All(first.Where(f => f.Depth is not null), flag =>
            Assert.Contains(bodies, body => body.Y == flag.Depth && body.X + 9 == flag.X && body.Y - 5 == flag.Y));
        Assert.All(first.Where(f => f.Depth is not null), flag => Assert.EndsWith("-1", flag.Sprite.Asset));
        Assert.NotEqual(first[0].Frame, actors.Sample(.4, clips).First().Frame);
    }
}
