using San9.Application.Presentation;

namespace San9.Client.Godot.Tests;

public sealed class TreasureDetailTests
{
    [Fact]
    public void ScrollingKeepsFocusedRowSeparateFromDisplayedTreasure()
    {
        var state = new TreasureDetailState();
        state.Open(Enumerable.Range(0, 40), 0);
        state.MoveFocus(1);
        Assert.Null(state.FocusedId);
        state.PointAt(4);
        state.Scroll(3);
        Assert.Equal(7, state.FocusedId);
        Assert.Equal(0, state.CurrentId);
        state.ClearFocus();
        state.MoveFocus(1);
        Assert.Null(state.FocusedId);
        Assert.Equal(0, state.CurrentId);
    }

    [Fact]
    public void FocusDoesNotActivateAndCandidateOrderControlsScrolling()
    {
        var ids = Enumerable.Range(0, 26).Reverse().ToArray();
        var state = new TreasureDetailState(); state.Open(ids, 25);
        ids[0] = 99;
        Assert.Equal(25, state.Candidates[0]);
        state.PointAt(25);
        for (int i = 0; i < 25; i++) state.MoveFocus(1);
        Assert.Equal(0, state.FocusedId);
        Assert.Equal(25, state.CurrentId);
        Assert.Equal(1, state.Offset);
        state.MoveFocus(1); Assert.Equal(0, state.FocusedId);
        state.Activate(0); Assert.Equal(0, state.CurrentId);
        state.Scroll(-10); Assert.Equal(0, state.Offset);
        state.Scroll(100); Assert.Equal(1, state.Offset);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Activate(99));
        Assert.Equal(0, state.CurrentId);
    }

    [Fact]
    public void EmptyInvalidAndSingleItemCandidatesHaveExplicitBoundaries()
    {
        var state = new TreasureDetailState();
        Assert.Throws<ArgumentException>(() => state.Open([], 0));
        Assert.Throws<ArgumentException>(() => state.Open([1, 1], 1));
        Assert.Throws<ArgumentException>(() => state.Open([1], 0));
        state.Open([1], 1); state.PointAt(1); state.Scroll(5); state.MoveFocus(-1); state.MoveFocus(1);
        Assert.Equal(0, state.Offset); Assert.Equal(1, state.FocusedId); Assert.Equal(1, state.CurrentId);
    }
}
