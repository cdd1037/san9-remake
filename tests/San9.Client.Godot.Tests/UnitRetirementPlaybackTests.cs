using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class UnitRetirementPlaybackTests
{
    internal static MapUnitCatalog Catalog => UnitVisualFixture.Catalog with
    {
        Bodies = UnitVisualFixture.Catalog.Bodies.Select(b => b with
        {
            Retirement = new(Enumerable.Range(0, 8).Select(d => $"retire-{d}").ToArray(), new("splash", -12, -10), 500, 1000, 100)
        }).ToArray()
    };
    internal static Dictionary<string, MapAnimationClip> Clips => UnitVisualFixture.Clips.Concat(
        Enumerable.Range(0, 8).Select(d => new KeyValuePair<string, MapAnimationClip>($"retire-{d}",
            new($"retire-{d}", Enumerable.Range(0, 8).Select(i => $"retire-{d}-{i}").ToArray(), 100, []))))
        .Append(new("splash", new("splash", ["splash-0", "splash-1"], 125, []) )).ToDictionary();
    internal static Dictionary<string, ImageAsset> Assets => Clips.Values.SelectMany(c => c.Frames).Distinct()
        .ToDictionary(f => f, f => new ImageAsset(f, f + ".png", 32, 40));
    private static MapAudioDefinition Audio => new("title", [], [], [], new()
    {
        ["body"] = new([], [], []) { Retirement = new(["defeat"], 500, "end") },
        ["imperial"] = new([], [], []) { Retirement = new(["defeat"], 500, "end") }
    }, [], []);

    [Fact]
    public void DetachedMembersKeepTheirPosesThenAnimateFadeAndExpireWithIndependentEffectAndSounds()
    {
        var state = UnitVisualFixture.State with { Soldiers = 1001, Morale = 70 };
        var motion = new UnitMotionPlayback();
        motion.Begin(state, Catalog, 0, 0, false);
        var retirement = new UnitRetirementPlayback("defeat", 1000, state, motion, Catalog, Clips, Assets, false, (_, _) => 1);
        var first = retirement.Sample(1000).ToArray();
        Assert.Equal(2, first.Length);
        Assert.All(first, s => { Assert.Equal(0, s.Frame); Assert.Equal(1, s.Opacity); Assert.True(s.IsBody); });
        var last = retirement.Sample(1799).First(s => s.IsBody);
        Assert.Equal(7, last.Frame);
        Assert.Equal((first[0].X, first[0].Y), (last.X, last.Y));
        Assert.DoesNotContain(retirement.Sample(1499), s => s.ClipId == "splash");
        Assert.Equal(2, retirement.Sample(1500).Count(s => s.ClipId == "splash"));
        Assert.Contains(retirement.Sample(2000), s => s.IsBody && s.Opacity is > 0 and < 1);
        Assert.Empty(retirement.TakeSounds(1499, Audio));
        Assert.Equal("defeat", Assert.Single(retirement.TakeSounds(1500, Audio)).Role);
        Assert.Empty(retirement.TakeSounds(1500, Audio));
        Assert.Equal("end", Assert.Single(retirement.TakeSounds(1800, Audio)).Role);
        Assert.Empty(retirement.TakeSounds(5000, Audio));
        Assert.Empty(retirement.Sample(1000 + retirement.Duration));
        Assert.True(retirement.IsComplete(1000 + retirement.Duration));
    }

    [Fact]
    public void SelfCollapseFacesMembersOutwardWithoutMovingTheirBodiesOrSelectingAttackClips()
    {
        var state = UnitVisualFixture.State with { Soldiers = 1001, Morale = 0, Direction = 3 };
        var motion = new UnitMotionPlayback(); motion.Begin(state, Catalog, 0, 0, false);
        var defeated = new UnitRetirementPlayback("defeat", 0, state, motion, Catalog, Clips, Assets, false, (_, _) => 1);
        var collapsed = new UnitRetirementPlayback("collapse", 0, state, motion, Catalog, Clips, Assets, true, (_, _) => 1);
        Assert.All(defeated.Sample(0), s => Assert.Equal("retire-3", s.ClipId));
        Assert.Contains(collapsed.Sample(0), s => s.ClipId != "retire-3");
        Assert.Equal(defeated.Sample(0).Select(s => (s.X, s.Y)), collapsed.Sample(0).Select(s => (s.X, s.Y)));
    }
}
