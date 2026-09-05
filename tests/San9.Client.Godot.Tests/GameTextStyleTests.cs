using Godot;

namespace San9.Client.Godot.Tests;

public sealed class GameTextStyleTests
{
    [Theory]
    [InlineData(GameTextRole.Header, 0, false)]
    [InlineData(GameTextRole.Resource, 0, false)]
    [InlineData(GameTextRole.Facility, 0, false)]
    [InlineData(GameTextRole.Building, 1, false)]
    [InlineData(GameTextRole.Message, 1, true)]
    [InlineData(GameTextRole.Region, 1, false)]
    [InlineData(GameTextRole.Spring, 1, false)]
    [InlineData(GameTextRole.Compass, 1, false)]
    public void RolesKeepTheirDistinctEffects(GameTextRole role, int outline, bool shadow)
    {
        var style = GameTextStyles.For(role);
        Assert.Equal(outline, style.OutlineSize);
        Assert.Equal(shadow, style.Shadow.A > 0);
        Assert.Equal(shadow ? Vector2.One : Vector2.Zero, style.ShadowOffset);
    }

    [Fact]
    public void RegionAndSpringUseTheirOwnEdgeColors()
    {
        Assert.Equal(new Color(new Color("005028"), .65f), GameTextStyles.For(GameTextRole.Region).Outline);
        Assert.Equal(new Color(new Color("770000"), .65f), GameTextStyles.For(GameTextRole.Spring).Outline);
    }

    [Theory]
    [InlineData(GameButtonTextRole.Information, "005028", true)]
    [InlineData(GameButtonTextRole.Functions, "5828c8", true)]
    [InlineData(GameButtonTextRole.Help, "603000", true)]
    [InlineData(GameButtonTextRole.Advance, "005028", true)]
    [InlineData(GameButtonTextRole.Territory, "603000", false)]
    [InlineData(GameButtonTextRole.Zoom, "603000", false)]
    [InlineData(GameButtonTextRole.Facilities, "005028", false)]
    [InlineData(GameButtonTextRole.Officers, "800090", false)]
    [InlineData(GameButtonTextRole.Units, "5828c8", false)]
    public void ButtonStateColorsFollowOriginalTables(GameButtonTextRole role, string edge, bool selectedWhite)
    {
        Assert.Equal(new Color("d8d8d8"), GameTextStyles.Button(role, false, false, false).Foreground);
        Assert.Equal(new Color(selectedWhite ? "f0f0f0" : "d8d8d8"), GameTextStyles.Button(role, false, true, false).Foreground);
        Assert.Equal(new Color(new Color(edge), .65f), GameTextStyles.Button(role, true, true, false).Outline);
        foreach (bool selected in new[] { false, true })
        {
            Assert.Equal(new Color("a4a4a4"), GameTextStyles.Button(role, false, selected, true).Foreground);
            Assert.Equal(Colors.Black, GameTextStyles.Button(role, true, selected, true).Foreground);
        }
    }
}
