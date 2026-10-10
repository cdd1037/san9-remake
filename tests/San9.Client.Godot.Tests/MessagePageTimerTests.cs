using San9.Client.Godot.Settings;

namespace San9.Client.Godot.Tests;

public sealed class MessagePageTimerTests
{
    [Theory]
    [InlineData(MessageSpeed.Normal, 4)]
    [InlineData(MessageSpeed.Fast, 2)]
    public void EachReadyPageReceivesItsOwnFullWait(MessageSpeed speed, double seconds)
    {
        var timer = new MessagePageTimer();
        Assert.False(timer.Advance(20, speed, false));
        Assert.False(timer.Advance(seconds - .1, speed, true));
        Assert.True(timer.Advance(.1, speed, true));
        timer.Reset();
        Assert.False(timer.Advance(.1, speed, true));
        Assert.True(timer.Advance(seconds - .1, speed, true));
    }
    [Fact]
    public void PauseNeverAutomaticallyAdvances()
    {
        var timer = new MessagePageTimer();
        Assert.False(timer.Advance(10000, MessageSpeed.Pause, true));
        Assert.False(timer.Advance(1, MessageSpeed.Normal, true));
    }
}
