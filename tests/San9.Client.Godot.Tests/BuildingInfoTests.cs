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
        var center = MapGeometry.Center(89, 64, 200);
        Assert.Equal((center.X + 12, center.Y - 128), BuildingInfoGeometry.Anchor(89, 64, 0, 1, 200));
        Assert.Equal((center.X + 80, center.Y - 106), BuildingInfoGeometry.Anchor(89, 64, 4, 1, 200));
        Assert.Equal((center.X + 12, center.Y - 148), BuildingInfoGeometry.Anchor(89, 64, 0, 3, 200));
        Assert.Equal(68, BuildingInfoGeometry.FillWidth(int.MaxValue, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildingInfoGeometry.FillWidth(1, 0));
    }

    [Fact]
    public void WarningAndCriticalPulseWithoutChangingNormalBarsOrThresholds()
    {
        var warning = Enumerable.Range(0, 8).Select(i => MapInfoBarAnimation.Sample(20, 100, i * 125, 0)).ToArray();
        var critical = Enumerable.Range(0, 8).Select(i => MapInfoBarAnimation.Sample(19, 100, i * 125, 0)).ToArray();
        Assert.All(warning, value => { Assert.Equal(1, value.Normal); Assert.Equal(0, value.Critical); });
        Assert.Contains(warning, value => value.Warning == 0);
        Assert.Contains(warning, value => value.Warning == 1);
        Assert.Contains(warning, value => value.Warning is > 0 and < 1);
        Assert.All(critical, value => { Assert.Equal(0, value.Warning); Assert.Equal(1, value.Normal + value.Critical); });
        Assert.Contains(critical, value => value.Critical == 1 && value.Normal == 0);
        Assert.Contains(critical, value => value.Critical == 0 && value.Normal == 1);
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal((1f, 0f, 0f), MapInfoBarAnimation.Sample(40, 100, i * 125, 0));
            Assert.Equal(warning[i], MapInfoBarAnimation.Sample(160, 800, i * 125, 0));
            Assert.Equal(critical[i], MapInfoBarAnimation.Sample(159, 800, i * 125, 0));
        }
    }

    [Fact]
    public void PulseUsesElapsedDisplayTimeAndStableIdentityRatherThanSamplingHistory()
    {
        Assert.Equal(0, MapInfoBarAnimation.Frame(124.999));
        Assert.Equal(1, MapInfoBarAnimation.Frame(125));
        foreach (int id in new[] { 0, 1, 15, int.MaxValue })
        {
            var before = MapInfoBarAnimation.Sample(30, 100, 375, id);
            Assert.Equal(before, MapInfoBarAnimation.Sample(25, 100, 100375, id));
            Assert.Equal(before, MapInfoBarAnimation.Sample(30, 100, 375, id));
        }
        Assert.NotEqual(MapInfoBarAnimation.Sample(30, 100, 0, 0), MapInfoBarAnimation.Sample(30, 100, 0, 1));
    }
}
