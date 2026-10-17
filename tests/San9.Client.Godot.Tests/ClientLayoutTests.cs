namespace San9.Client.Godot.Tests;

public sealed class ClientLayoutTests
{
    [Fact]
    public void WideningRetainsTheCenteredFrameAndRejectsNewlyExposedMapInput()
    {
        var bounds = ClientLayout.PresentedMapBounds(new(1792, 768), new(1024, 768));
        Assert.Equal(new CanvasRectangle(384, 0, 1024, 768), bounds);
        Assert.False(bounds.Contains(new(383.99, 384)));
        Assert.True(bounds.Contains(new(384, 384)));
        Assert.True(bounds.Contains(new(1407.99, 384)));
        Assert.False(bounds.Contains(new(1408, 384)));

        var prepared = ClientLayout.PresentedMapBounds(new(1792, 768), new(1792, 768));
        Assert.True(prepared.Contains(new(0, 384)));
        Assert.True(prepared.Contains(new(1791.99, 384)));
        Assert.False(prepared.Contains(new(1792, 384)));
    }

    [Theory]
    [InlineData(1024, 768, 1792, 768)]
    [InlineData(1365.333333, 768, 1792, 900)]
    public void ShrinkingUsesAllOfTheViewportWithoutNegativeCoverAreas(double width, double height, double frameWidth, double frameHeight)
    {
        Assert.Equal(new CanvasRectangle(0, 0, width, height),
            ClientLayout.PresentedMapBounds(new(width, height), new(frameWidth, frameHeight)));
    }

    [Fact]
    public void AnAbsentFrameNeverAcceptsMapInput()
    {
        var bounds = ClientLayout.PresentedMapBounds(new(1365.333333, 768), default);
        Assert.False(bounds.Contains(new(0, 0)));
        Assert.False(bounds.Contains(new(1365.333333 / 2, 384)));
    }

    [Fact]
    public void SuccessiveResizesUseThePresentedFrameRatherThanAnUnfinishedRequest()
    {
        var presented = new CanvasPoint(1024, 768);
        _ = ClientLayout.PresentedMapBounds(new(1792, 768), presented);
        var latest = ClientLayout.PresentedMapBounds(new(1200, 768), presented);
        Assert.Equal(new CanvasRectangle(88, 0, 1024, 768), latest);
        Assert.False(latest.Contains(new(1150, 384)));
    }
}
