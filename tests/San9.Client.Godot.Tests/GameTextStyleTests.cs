using Godot;

namespace San9.Client.Godot.Tests;

public sealed class GameTextStyleTests
{
    [Fact]
    public void HelpTopicsPreservePairedFiveEntryTablesAndTheirEightStateExpansion()
    {
        string[][] foreground = [
            ["d8d8d8", "c0e0e8", "f0f0f0", "f0f0f0", "a4a4a4", "a4a4a4", "a4a4a4", "a4a4a4"],
            ["f0f0f0", "f0f0f0", "f0f0f0", "f0f0f0", "000000", "000000", "000000", "000000"]];
        for (int current = 0; current < 2; current++)
        for (int state = 0; state < 8; state++)
        {
            var style = GameTextStyles.HelpTopic((state & 1) != 0, (state & 2) != 0, (state & 4) != 0, current != 0);
            Assert.Equal(new Color(foreground[current][state]), style.Foreground);
            Assert.Equal(current == 0 && state == 0 ? GameTextEffect.None : GameTextEffect.Outline, style.Effect);
            if (style.Effect == GameTextEffect.Outline)
                Assert.Equal(new Color(state < 4 ? "5828c8" : "000000"), style.EffectColor);
        }
    }

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
    [InlineData(GameTextRole.Building, GameTextEffect.Outline, 4)]
    [InlineData(GameTextRole.Message, GameTextEffect.Shadow, 0)]
    [InlineData(GameTextRole.Region, GameTextEffect.Outline, 4)]
    [InlineData(GameTextRole.Spring, GameTextEffect.Outline, 4)]
    [InlineData(GameTextRole.Compass, GameTextEffect.Outline, 4)]
    public void RolesKeepTheirDistinctEffects(GameTextRole role, GameTextEffect effect, int outlineSize)
    {
        var style = GameTextStyles.For(role);
        Assert.Equal(effect, style.Effect);
        Assert.Equal(outlineSize, GameTextEffects.OutlineSize(style));
    }

    [Fact]
    public void NativeEffectsHaveIndependentOutlineAndShadowProperties()
    {
        var outline = GameTextStyles.InformationMenu(true, false, true);
        Assert.Equal(4, GameTextEffects.OutlineSize(outline));
        Assert.Equal(new Color(new Color("f0f0f0"), .57f), GameTextEffects.OutlineColor(outline));
        Assert.Equal(Colors.Transparent, GameTextEffects.ShadowColor(outline));
        var shadow = GameTextStyles.Message(true);
        Assert.Equal(0, GameTextEffects.OutlineSize(shadow));
        Assert.Equal(Colors.Transparent, GameTextEffects.OutlineColor(shadow));
        Assert.Equal(Vector2.One, GameTextEffects.ShadowOffset(shadow));
        Assert.Equal(Colors.Black, GameTextEffects.ShadowColor(shadow));
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
    [InlineData(GameButtonTextRole.ConfirmYes, "005028", true)]
    [InlineData(GameButtonTextRole.ConfirmNo, "983810", true)]
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
