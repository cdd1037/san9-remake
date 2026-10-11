using San9.Content.Interface;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class BattleMapNumberPlaybackTests
{
    [Theory]
    [InlineData(-123,1,4)]
    [InlineData(123,0,-4)]
    public void SignedNumbersRemainCenteredAndAdvanceOnlyAtAnimationSteps(long amount,int row,int step)
    {
        var definition = new BattleMapNumberDefinition("digits",12,20,125,8,4);
        Assert.Null(BattleMapNumberPlayback.Sample(definition,amount,-.1));
        var first = BattleMapNumberPlayback.Sample(definition,amount,0)!;
        Assert.Equal(row,first.Row);
        Assert.Equal(-24,first.X);
        Assert.Equal(first,BattleMapNumberPlayback.Sample(definition,amount,.124));
        Assert.Equal(first.Y+step,BattleMapNumberPlayback.Sample(definition,amount,.125)!.Y);
        Assert.NotNull(BattleMapNumberPlayback.Sample(definition,amount,.999));
        Assert.Null(BattleMapNumberPlayback.Sample(definition,amount,1));
        Assert.Null(BattleMapNumberPlayback.Sample(definition,0,0));
        Assert.Equal(amount<0?11:10,BattleMapNumberPlayback.Column(first.Text[0]));
    }
}
