using San9.Application.Presentation;

namespace San9.Application.Tests;

public sealed class OfficerDetailTests
{
    [Fact]
    public void CallerOrderFocusDisplayedPersonAndPageAreIndependent()
    {
        var ids = Enumerable.Range(0, 24).Reverse().ToArray();
        var state = new OfficerDetailState(); state.Open(ids, 22);
        Assert.Equal(ids, state.Candidates);
        state.SelectPage(OfficerDetailPage.Tactics);
        state.PointAt(16);
        Assert.Equal(22, state.CurrentId);
        state.Activate(16);
        Assert.Equal(16, state.CurrentId);
        Assert.Equal(OfficerDetailPage.Tactics, state.Page);
        Assert.Equal(ids, state.Candidates);
    }
    [Theory]
    [InlineData(18, 0)]
    [InlineData(19, 1)]
    [InlineData(32, 14)]
    public void WholeListScrollsWithoutAFixedRulerRow(int count, int maximum)
    {
        var state = new OfficerDetailState(); state.Open(Enumerable.Range(0,count), 0);
        state.Scroll(int.MaxValue); Assert.Equal(maximum, state.Offset);
        state.Scroll(-1); Assert.Equal(0,state.Offset);
        state.PointAt(17); state.MoveFocus(1);
        Assert.Equal(count>18?18:17,state.FocusedId);
        Assert.Equal(count>18?1:0,state.Offset);
        Assert.Equal(0,state.CurrentId);
    }
}
