using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class DuelInterfacePlaybackTests
{
    [Theory]
    [InlineData(100, 196)]
    [InlineData(50, 98)]
    [InlineData(1, 1)]
    [InlineData(.1, 1)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(150, 196)]
    public void HealthClipsNativeGradientAndDistinguishesAliveFromDefeated(double hp, int expected) =>
        Assert.Equal(expected, DuelInterfacePlayback.HealthWidth(196, hp));
}

