using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class BuildingInfoTests
{
    [Theory]
    [InlineData(0, 100, 0, "critical")]
    [InlineData(1, 100, 1, "critical")]
    [InlineData(19, 100, 13, "critical")]
    [InlineData(20, 100, 13, "warning")]
    [InlineData(40, 100, 27, "normal")]
    [InlineData(50, 100, 34, "normal")]
    [InlineData(440, 800, 38, "normal")]
    [InlineData(300, 300, 68, "normal")]
    public void OriginalBarWidthAndThresholds(int current, int maximum, int width, string state)
    {
        Assert.Equal(width, BuildingInfoGeometry.FillWidth(current, maximum));
        Assert.Equal(state, BuildingInfoGeometry.FillState(current, maximum));
    }

    [Fact]
    public void BuildingTypesHaveDistinctAnchorsAndElevationMovesTheWholePanel()
    {
        var center = MapGeometry.Center(89, 64);
        Assert.Equal((center.X + 12, center.Y - 128), BuildingInfoGeometry.Anchor(89, 64, 0, 1));
        Assert.Equal((center.X + 80, center.Y - 106), BuildingInfoGeometry.Anchor(89, 64, 4, 1));
        Assert.Equal((center.X + 12, center.Y - 148), BuildingInfoGeometry.Anchor(89, 64, 0, 3));
        Assert.Equal(68, BuildingInfoGeometry.FillWidth(int.MaxValue, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildingInfoGeometry.FillWidth(1, 0));
    }
}
