using Godot;

namespace San9.Client.Godot.Tests;

public sealed class GameTextStyleTests
{
    [Fact]
    public void HintAvailabilityChangesForegroundButPreservesEffects()
    {
        var enabled = GameTextStyles.Message(true);
        var disabled = GameTextStyles.Message(false);
        Assert.Equal(new Color("f0f0f0"), enabled.Foreground);
        Assert.Equal(new Color("f06850"), disabled.Foreground);
        Assert.Equal(enabled with { Foreground = disabled.Foreground }, disabled);
        Assert.Equal(GameTextEffect.Shadow, disabled.Effect);
    }
    [Theory]
    [InlineData(GameTextRole.Header, GameTextEffect.None, 0)]
    [InlineData(GameTextRole.Resource, GameTextEffect.None, 0)]
    [InlineData(GameTextRole.Roster, GameTextEffect.None, 0)]
    [InlineData(GameTextRole.Building, GameTextEffect.NeighborOutline, 8)]
    [InlineData(GameTextRole.Message, GameTextEffect.Shadow, 1)]
    [InlineData(GameTextRole.Region, GameTextEffect.NeighborOutline, 8)]
    [InlineData(GameTextRole.Spring, GameTextEffect.NeighborOutline, 8)]
    [InlineData(GameTextRole.Compass, GameTextEffect.NeighborOutline, 8)]
    public void RolesKeepTheirDistinctEffects(GameTextRole role, GameTextEffect effect, int passes)
    {
        var style = GameTextStyles.For(role);
        Assert.Equal(effect, style.Effect);
        Assert.Equal(passes, GameTextEffects.PassCount(style));
    }

    [Fact]
    public void OutlineUsesEightUniqueNeighborsAtQuarterCoverage()
    {
        var style = GameTextStyles.InformationMenu(true, false, true);
        var offsets = Enumerable.Range(0, GameTextEffects.PassCount(style)).Select(i => GameTextEffects.Offset(style, i)).ToArray();
        Assert.Equal(8, offsets.Distinct().Count());
        Assert.DoesNotContain(Vector2.Zero, offsets);
        Assert.All(offsets, p => Assert.True(Math.Abs(p.X) <= 1 && Math.Abs(p.Y) <= 1));
        Assert.Equal(new Color(new Color("f0f0f0"), .25f), GameTextEffects.Color(style));
        Assert.Equal(Colors.Black, style.Foreground);
    }

    [Fact]
    public void MessageHasOneFullCoverageOffsetWithoutOutline()
    {
        var style = GameTextStyles.Message(true);
        Assert.Equal(1, GameTextEffects.PassCount(style));
        Assert.Equal(Vector2.One, GameTextEffects.Offset(style, 0));
        Assert.Equal(Colors.Black, GameTextEffects.Color(style));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameTextEffects.Offset(style, 1));
    }

    [Fact]
    public void RegionAndSpringUseTheirOwnEdgeColors()
    {
        Assert.Equal(new Color("005028"), GameTextStyles.For(GameTextRole.Region).EffectColor);
        Assert.Equal(new Color("770000"), GameTextStyles.For(GameTextRole.Spring).EffectColor);
    }

    [Theory]
    [InlineData(GameButtonTextRole.Information, "005028", true)]
    [InlineData(GameButtonTextRole.Functions, "5828c8", true)]
    [InlineData(GameButtonTextRole.Help, "603000", true)]
    [InlineData(GameButtonTextRole.Advance, "005028", true)]
    [InlineData(GameButtonTextRole.Territory, "603000", false)]
    [InlineData(GameButtonTextRole.MapMode, "603000", false)]
    [InlineData(GameButtonTextRole.Facilities, "005028", false)]
    [InlineData(GameButtonTextRole.Officers, "800090", false)]
    [InlineData(GameButtonTextRole.Units, "5828c8", false)]
    public void ButtonStateColorsFollowOriginalTables(GameButtonTextRole role, string edge, bool selectedWhite)
    {
        Assert.Equal(new Color("d8d8d8"), GameTextStyles.Button(role, false, false, false).Foreground);
        Assert.Equal(new Color(selectedWhite ? "f0f0f0" : "d8d8d8"), GameTextStyles.Button(role, false, true, false).Foreground);
        Assert.Equal(new Color(edge), GameTextStyles.Button(role, true, true, false).EffectColor);
        foreach (bool selected in new[] { false, true })
        {
            Assert.Equal(new Color("a4a4a4"), GameTextStyles.Button(role, false, selected, true).Foreground);
            Assert.Equal(Colors.Black, GameTextStyles.Button(role, true, selected, true).Foreground);
        }
    }
}
