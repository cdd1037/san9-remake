using San9.Content.Images;

namespace San9.Content.Tests;

public sealed class UnitFlagTextureGroupsTests
{
    [Fact]
    public void FacingAtlasesSeparateFromSeasonalFlagsAndBoundColorAndGlyphResidency()
    {
        Assert.Equal("map/animations/flags/poles/set-1", TextureGroups.MapAnimation("flag-pole-front", 1));
        Assert.Equal("map/animations/buildings/smoke/set-0", TextureGroups.MapAnimation("building-smoke-damage-a", 0));
        Assert.Equal("map/animations/units/actions/13/set-0", TextureGroups.MapAnimation("unit-action-13-7-2", 0));
        Assert.Equal("map/animations/units/wake/set-0", TextureGroups.MapAnimation("unit-wake-0", 0));
        Assert.Equal("map/animations/projectiles/rockfall-impact/set-0", TextureGroups.MapAnimation("rockfall-impact", 0));
        foreach (int direction in Enumerable.Range(0, 8))
        {
            Assert.Equal($"map/animations/units/flags/{direction}/poles/set-0", TextureGroups.MapAnimation($"unit-flag-{direction}-pole-back", 0));
            Assert.Equal($"map/animations/units/flags/{direction}/cloth/03/set-0", TextureGroups.MapAnimation($"unit-flag-{direction}-cloth-50", 0));
            Assert.Equal($"map/animations/units/flags/{direction}/glyphs/1/05/set-0", TextureGroups.MapAnimation($"unit-flag-{direction}-glyph-1-185", 0));
        }
    }
}
