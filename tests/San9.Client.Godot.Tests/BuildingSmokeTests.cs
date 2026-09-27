using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class BuildingSmokeTests
{
    private static readonly MapBuildingSmoke Smoke = new([new("dust", 3, 4, 100, true)],
        [new("smoke-a", 5, 6, 100, false)], [new("smoke-b", 7, 8, 100, false)]);
    private static readonly Dictionary<string, MapAnimationClip> Clips = new[] { "dust", "smoke-a", "smoke-b" }
        .ToDictionary(id => id, id => new MapAnimationClip(id, ["a", "b", "c"], 100, []));
    private static readonly BuildingVisualState State = new(1000, 1000, 0, 0, 1000, 1, -1) { Morale = 80, Month = 1 };
    private static MapAnimatedSprite[] Resolve(BuildingVisualState state, int maximum = 1000, bool city = true) =>
        BuildingSmokeVisuals.Resolve(Smoke, state, maximum, city, Clips);

    [Fact]
    public void EngagementDustDoesNotRequireDamageAndStopsOnDisengagement()
    {
        Assert.Empty(Resolve(State));
        var dust = Assert.Single(Resolve(State with { IsEngaged = true }));
        Assert.Equal("dust", dust.ClipId);
        Assert.True(dust.BehindBuilding);
        Assert.Equal((3, 4), (dust.X, dust.Y));
        Assert.Empty(Resolve(State with { IsEngaged = true, IsDestroyed = true }));
    }

    [Theory]
    [InlineData(199, false, false, 1)]
    [InlineData(200, false, false, 0)]
    [InlineData(199, true, false, 0)]
    [InlineData(199, true, true, 2)]
    public void CriticalSmokeHonorsStrictThresholdAndConstructionGate(int durability, bool incomplete, bool engaged, int count)
    {
        Assert.Equal(count, Resolve(State with { Durability = durability, IsUnderConstruction = incomplete, IsEngaged = engaged }).Length);
        // Original threshold truncates before the comparison, including non-divisible authored maxima.
        Assert.Empty(Resolve(State with { Durability = 200 }, 1004));
    }

    [Fact]
    public void DevelopmentSelectsVariantsOnlyAfterDamageGateAndOnlyCitiesGetBoth()
    {
        Assert.Empty(Resolve(State with { Development = 900 }));
        var low = State with { Durability = 199 };
        Assert.Equal("smoke-a", Assert.Single(Resolve(low with { Development = 300 })).ClipId);
        Assert.Equal("smoke-a", Assert.Single(Resolve(low with { Development = 600 })).ClipId);
        Assert.Equal("smoke-b", Assert.Single(Resolve(low with { Month = 2 })).ClipId);
        Assert.Equal(2, Resolve(low with { Development = 601 }).Length);
        Assert.Single(Resolve(low with { Development = 601 }, city: false));
    }

    [Fact]
    public void StableSamplingKeepsEmissionsAndPhaseAcrossRepeatedProjection()
    {
        var varied = new MapBuildingSmoke(Enumerable.Range(0, 100).Select(i => new MapSmokeEmitter("dust", i, 0, 25, false)).ToArray(), [], []);
        var state = State with { IsEngaged = true };
        var first = BuildingSmokeVisuals.Resolve(varied, state, 1000, true, Clips);
        Assert.InRange(first.Length, 10, 40);
        Assert.Equal(first, BuildingSmokeVisuals.Resolve(varied, state, 1000, true, Clips));
        Assert.All(first, s => Assert.InRange(s.Phase, 0, 2));
    }

    [Fact]
    public void InvalidEmitterReferencesAndProbabilitiesAreRejected()
    {
        var composition = new MapComposition(1, []) { Smoke = Smoke };
        MapPackageFiles.ValidateBuildingSmoke([composition], Clips);
        Assert.Throws<InvalidDataException>(() => MapPackageFiles.ValidateBuildingSmoke([composition], new Dictionary<string, MapAnimationClip>()));
        Assert.Throws<InvalidDataException>(() => MapPackageFiles.ValidateBuildingSmoke(
            [composition with { Smoke = Smoke with { Engagement = [new("dust", 0, 0, 101, false)] } }], Clips));
    }
}
