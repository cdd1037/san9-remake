using Godot;

namespace San9.Client.Godot.Tests;

public sealed class KeyboardScrollTests
{
    [Fact]
    public void TextInputWithMissingReleasesCannotStartMapMovement()
    {
        var scroll = new KeyboardScrollController();
        foreach (var key in new[] { Key.W, Key.A, Key.S, Key.D })
            Assert.False(scroll.Press(key, echo: false, available: false));

        Assert.True(scroll.Direction(available: true).IsNone);
        Assert.False(scroll.Press(Key.D, echo: true, available: true));
        Assert.True(scroll.Direction(available: true).IsNone);
        Assert.True(scroll.Press(Key.D, echo: false, available: true));
        Assert.Equal(new(1, 0), scroll.Direction(available: true));
    }

    [Fact]
    public void BlockingNavigationCancelsHeldKeysUntilAFreshPress()
    {
        var scroll = new KeyboardScrollController();
        scroll.Press(Key.W, echo: false, available: true);
        Assert.True(scroll.Direction(available: false).IsNone);
        Assert.False(scroll.Press(Key.W, echo: true, available: true));
        Assert.True(scroll.Direction(available: true).IsNone);
        scroll.Release(Key.W);
        scroll.Press(Key.W, echo: false, available: true);
        Assert.Equal(new(0, -1), scroll.Direction(available: true));
    }

    [Fact]
    public void FocusOrSessionCancellationDoesNotResumeFromRepeats()
    {
        var scroll = new KeyboardScrollController();
        scroll.Press(Key.A, echo: false, available: true);
        scroll.Press(Key.S, echo: false, available: true);
        scroll.Cancel();
        scroll.Press(Key.A, echo: true, available: true);
        scroll.Press(Key.S, echo: true, available: true);
        Assert.True(scroll.Direction(available: true).IsNone);
    }

    [Fact]
    public void MapOwnsDiagonalAndOpposingKeysAndReleasesIndependently()
    {
        var scroll = new KeyboardScrollController();
        scroll.Press(Key.W, echo: false, available: true);
        scroll.Press(Key.D, echo: false, available: true);
        Assert.Equal(new(1, -1), scroll.Direction(available: true));
        scroll.Press(Key.A, echo: false, available: true);
        Assert.Equal(new(0, -1), scroll.Direction(available: true));
        scroll.Release(Key.W);
        scroll.Release(Key.A);
        Assert.Equal(new(1, 0), scroll.Direction(available: true));
        scroll.Release(Key.D);
        Assert.True(scroll.Direction(available: true).IsNone);
        Assert.False(scroll.Press(Key.F8, echo: false, available: true));
    }
}
