namespace San9.Client.Godot.Tests;

public sealed class EdgeScrollTests
{
    [Theory]
    [InlineData(512, 0, 0, -1)]
    [InlineData(1023, 384, 1, 0)]
    [InlineData(512, 767, 0, 1)]
    [InlineData(0, 384, -1, 0)]
    [InlineData(1023, 0, 1, -1)]
    [InlineData(1023, 767, 1, 1)]
    [InlineData(0, 767, -1, 1)]
    [InlineData(0, 0, -1, -1)]
    [InlineData(512, 384, 0, 0)]
    [InlineData(-1, 384, 0, 0)]
    [InlineData(1024, 384, 0, 0)]
    [InlineData(512, 768, 0, 0)]
    [InlineData(512, -1, 0, 0)]
    public void CanvasEdgesProduceScreenDirectionsWithoutBlackBarActivation(double x, double y, int dx, int dy)
    {
        Assert.Equal(new(dx, dy), EdgeScrollController.Direction(new(x, y), true));
        Assert.True(EdgeScrollController.Direction(new(x, y), false).IsNone);
    }

    [Fact]
    public void HoldingRequiresAnEdgePressAndInterruptedGesturesCannotResumeThroughUi()
    {
        var controller = new EdgeScrollController();
        controller.ToggleMode();
        var edge = new CanvasPoint(0, 384);
        Assert.False(controller.IsActive(true, false));
        Assert.False(controller.PressLeft(edge, false)); // UI owns this press.
        Assert.False(controller.IsActive(true, true)); // Moving out of UI while held is insufficient.
        Assert.False(controller.PressLeft(new(512, 384), true));
        Assert.False(controller.IsActive(true, true));
        Assert.True(controller.PressLeft(edge, true));
        Assert.True(controller.IsActive(true, true));
        Assert.False(controller.IsActive(false, true)); // Modal, focus loss, pointer exit or middle drag.
        Assert.False(controller.IsActive(true, true));
        Assert.True(controller.ReleaseLeft()); // Still consume the interrupted gesture's release.
        Assert.False(controller.ReleaseLeft());
        Assert.True(controller.PressLeft(edge, true));
        Assert.False(controller.IsActive(true, false)); // Recover even if release was handled elsewhere.
        Assert.False(controller.OwnsLeftButton);
    }

    [Fact]
    public void HoverNeedsNoNewMotionAndModeSwitchDoesNotInheritAHeldButton()
    {
        var controller = new EdgeScrollController();
        for (int frame = 0; frame < 120; frame++) Assert.True(controller.IsActive(true, false));
        Assert.False(controller.PressLeft(new(0, 384), false));
        Assert.False(controller.IsActive(true, true)); // Hover must not steal a scrollbar drag reaching an edge.
        Assert.True(controller.IsActive(true, false));
        Assert.False(controller.IsActive(false, false));
        controller.ToggleMode();
        Assert.False(controller.IsActive(true, true));
        Assert.True(controller.PressLeft(new(0, 384), true));
        Assert.True(controller.IsActive(true, true));
    }

    [Theory]
    [InlineData(30, .25)]
    [InlineData(60, 1)]
    [InlineData(144, 2)]
    public void OneSecondTravelsTheSameScreenDistanceAtEveryFrameRateZoomAndDirection(int fps, double zoom)
    {
        foreach (var direction in new EdgeScrollDirection[] { new(1, 0), new(1, 1), new(-1, -1) })
        {
            double x = 0, y = 0;
            for (int frame = 0; frame < fps; frame++)
            {
                var step = EdgeScrollController.Displacement(direction, 1.0 / fps, zoom, true);
                x += step.X; y += step.Y;
            }
            Assert.Equal(EdgeScrollController.Speed, Math.Sqrt(x * x + y * y) * zoom, 8);
        }
    }

    [Fact]
    public void WaitingForACompleteViewDoesNotAccumulateMovement()
    {
        for (int frame = 0; frame < 600; frame++)
            Assert.Equal(default, EdgeScrollController.Displacement(new(1, 0), 1.0 / 60, 1, false));
        var resumed = EdgeScrollController.Displacement(new(1, 0), 1.0 / 60, 1, true);
        Assert.Equal(20, resumed.X, 8);
        var stalled = EdgeScrollController.Displacement(new(1, 0), 5, 1, true);
        Assert.InRange(stalled.X, 0, 60);
    }
}
