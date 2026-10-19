namespace San9.Client.Godot.Tests;

public sealed class MapDragTests
{
    [Fact]
    public void RepeatedJitterDoesNotAccumulateIntoADrag()
    {
        var drag = new MapDragController();
        drag.Press(new(100, 100));
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(default, drag.Move(new(103, 104)));
            Assert.Equal(default, drag.Move(new(97, 96)));
        }
        Assert.True(drag.IsHeld); // Wheel zoom remains suppressed while waiting for a drag.
        Assert.False(drag.IsDragging);
        drag.Cancel();
        Assert.False(drag.IsHeld);
        Assert.Equal(default, drag.Move(new(200, 200)));
    }

    [Fact]
    public void DragStartsFromTheLatestMotionAndStaysActiveWhenReturningToOrigin()
    {
        var drag = new MapDragController();
        drag.Press(new(100, 100));
        Assert.Equal(default, drag.Move(new(104, 100)));
        Assert.Equal(new(2, 0), drag.Move(new(106, 100)));
        Assert.True(drag.IsDragging);
        Assert.Equal(new(-6, 0), drag.Move(new(100, 100)));
        Assert.Equal(new(1, 0), drag.Move(new(101, 100)));
        drag.Cancel();
        Assert.False(drag.IsDragging);
        Assert.Equal(default, drag.Move(new(200, 200)));
        drag.Press(new(200, 200));
        Assert.Equal(default, drag.Move(new(201, 200)));
        Assert.False(drag.IsDragging);
        Assert.Equal(new(3, 4), drag.Move(new(204, 204)));
        Assert.True(drag.IsDragging);
    }
}
