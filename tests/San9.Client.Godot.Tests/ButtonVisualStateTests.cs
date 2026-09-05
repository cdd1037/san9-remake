using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class ButtonVisualStateTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    public void TextUsesAbsoluteLogicalOffset(bool selected, int offset)
    {
        Assert.Equal((offset, offset), ButtonVisualState.TextOffset(selected));
    }

    [Theory]
    [InlineData(false, new[] { 0, 1, 2, 2, 3, 3, 3, 3 })]
    [InlineData(true, new[] { 0, 1, 2, 3, 4, 4, 5, 5 })]
    public void OriginalStateTableCoversAllCombinations(bool territory, int[] expected)
    {
        for (int state = 0; state < 8; state++)
            Assert.Equal(expected[state], ButtonVisualState.Frame(territory,
                (state & 1) != 0, (state & 2) != 0, (state & 4) != 0));
    }
}
