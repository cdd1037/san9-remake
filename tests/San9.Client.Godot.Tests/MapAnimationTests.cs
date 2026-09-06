using San9.Content.Packages;
using San9.Content.Images;
using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MapAnimationTests
{
    [Theory]
    [InlineData(6, 200)]
    [InlineData(4, 142)]
    [InlineData(8, 100)]
    public void ClockPreservesBoundariesAndCatchesUpAfterLongFrame(int count, int interval)
    {
        var clip = new MapAnimationClip("fixture", Enumerable.Range(0, count).Select(i => i.ToString()).ToArray(), interval, []);
        Assert.Equal(0, MapAnimationClock.Frame(clip, interval - 0.001));
        Assert.Equal(1, MapAnimationClock.Frame(clip, interval));
        Assert.Equal(0, MapAnimationClock.Frame(clip, interval * count));
        Assert.Equal(3 % count, MapAnimationClock.Frame(clip, interval * (count * 1000 + 3)));
        Assert.Equal(0, MapAnimationClock.Frame(clip, interval, count - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapAnimationClock.Frame(clip, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapAnimationClock.Frame(clip, -1));
    }

    [Fact]
    public void InvalidAnimationReferencesAndDimensionsAreRejected()
    {
        var a = new ImageAsset("a", "a.png", new string('0', 64), 32, 40);
        var b = a with { Id = "b", Width = 64 };
        var assets = new Dictionary<string, ImageAsset> { ["a"] = a, ["b"] = b };
        var clip = new MapAnimationClip("clip", ["a"], 100, []);
        var sprites = new[] { new MapAnimatedSprite("clip", 0, 0, 0, -1) };
        MapPackageFiles.ValidateAnimations(new([clip], sprites), assets, new HashSet<int> { 15 });
        Assert.Throws<InvalidDataException>(() => MapPackageFiles.ValidateAnimations(new([clip], [sprites[0] with { BuildingId = 15 }]), assets, new HashSet<int>()));
        Assert.Throws<InvalidDataException>(() => MapPackageFiles.ValidateAnimations(new([clip with { Frames = ["missing"] }], []), assets, new HashSet<int>()));
        Assert.Throws<InvalidDataException>(() => MapPackageFiles.ValidateAnimations(new([clip with { Frames = ["a", "b"] }], []), assets, new HashSet<int>()));
        Assert.Throws<InvalidDataException>(() => MapPackageFiles.ValidateAnimations(new([clip], [sprites[0] with { Phase = 1 }]), assets, new HashSet<int> { 15 }));
        Assert.Throws<InvalidDataException>(() => MapPackageFiles.ValidateAnimations(new([clip with { SeasonFrames = new() { ["winter"] = ["a"] } }], []), assets, new HashSet<int>()));
        var seasons = new Dictionary<string, string[]> { ["spring"] = ["a"], ["summer"] = ["a"], ["autumn"] = ["b"], ["winter"] = ["b"] };
        Assert.Throws<InvalidDataException>(() => MapPackageFiles.ValidateAnimations(new([clip with { SeasonFrames = seasons }], []), assets, new HashSet<int>()));
        seasons["autumn"] = ["a", "a"];
        Assert.Throws<InvalidDataException>(() => MapPackageFiles.ValidateAnimations(new([clip with { SeasonFrames = seasons }], []), assets, new HashSet<int>()));
    }

    [Fact]
    public void SeasonChangesFrameGroupWithoutResettingPhaseOrTiming()
    {
        string[] left = ["left-0", "left-1", "left-2", "left-3"], right = ["right-0", "right-1", "right-2", "right-3"];
        var clip = new MapAnimationClip("flag", left, 142, new() { ["spring"] = left, ["summer"] = left, ["autumn"] = right, ["winter"] = right });
        Assert.Equal("left-2", MapAnimationClock.Asset(clip, "spring", 142, 1));
        Assert.Equal("left-2", MapAnimationClock.Asset(clip, "summer", 142, 1));
        Assert.Equal("right-2", MapAnimationClock.Asset(clip, "autumn", 142, 1));
        Assert.Equal("right-2", MapAnimationClock.Asset(clip, "winter", 142, 1));
        Assert.Equal("left-2", MapAnimationClock.Asset(clip, "spring", 142, 1));
        Assert.Equal("right-3", MapAnimationClock.Asset(clip, "winter", 284, 1));
        Assert.Equal("right-0", MapAnimationClock.Asset(clip, "winter", 426, 1));
        var water = new MapAnimationClip("water", left, 200, []);
        Assert.Equal(MapAnimationClock.Asset(water, "spring", 400), MapAnimationClock.Asset(water, "winter", 400));
        Assert.Throws<ArgumentException>(() => MapAnimationClock.Asset(clip, "unknown", 0));
    }
}
