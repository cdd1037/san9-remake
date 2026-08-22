using San9.Client.Godot;

namespace San9.Client.Godot.Tests;

public sealed class ClientLayoutTests
{
    [Fact]
    public void FitToWindow_UsesTheWholeFourByThreeWindow()
    {
        LogicalCanvasTransform transform = ClientLayout.FitToWindow(1024, 768);

        Assert.Equal(1, transform.Scale);
        Assert.Equal(new CanvasRectangle(0, 0, 1024, 768), transform.ContentRectangle);
    }

    [Theory]
    [InlineData(1280, 720, 0.9375, 160, 0, 960, 720)]
    [InlineData(1920, 1080, 1.40625, 240, 0, 1440, 1080)]
    [InlineData(1024, 1024, 1, 0, 128, 1024, 768)]
    public void FitToWindow_CentersFourByThreeContent(
        int windowWidth,
        int windowHeight,
        double expectedScale,
        double expectedX,
        double expectedY,
        double expectedWidth,
        double expectedHeight)
    {
        LogicalCanvasTransform transform = ClientLayout.FitToWindow(windowWidth, windowHeight);

        Assert.Equal(expectedScale, transform.Scale);
        Assert.Equal(
            new CanvasRectangle(expectedX, expectedY, expectedWidth, expectedHeight),
            transform.ContentRectangle);
    }

    [Fact]
    public void Coordinates_RoundTripBetweenWindowAndLogicalCanvas()
    {
        LogicalCanvasTransform transform = ClientLayout.FitToWindow(1920, 1080);
        CanvasPoint logicalPoint = new(512, 384);

        CanvasPoint windowPoint = transform.LogicalToWindow(logicalPoint);
        bool isInside = transform.TryWindowToLogical(windowPoint, out CanvasPoint restoredPoint);

        Assert.True(isInside);
        Assert.Equal(new CanvasPoint(960, 540), windowPoint);
        Assert.Equal(logicalPoint, restoredPoint);
    }

    [Fact]
    public void WindowToLogical_RejectsPillarboxInput()
    {
        LogicalCanvasTransform transform = ClientLayout.FitToWindow(1920, 1080);

        bool isInside = transform.TryWindowToLogical(new CanvasPoint(100, 540), out _);

        Assert.False(isInside);
    }
}
