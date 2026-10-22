using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AdvisedAmountTests
{
    [Theory]
    [InlineData(true, true, 734200u, 734200u)]
    [InlineData(false, false, 734200u, 734200u)]
    [InlineData(true, false, 734200u, 1u)]
    [InlineData(false, true, 734200u, 1000000u)]
    [InlineData(true, true, 1u, 1u)]
    [InlineData(true, true, 1000000u, 1000000u)]
    [InlineData(false, false, 1u, 1u)]
    [InlineData(false, false, 1000000u, 1000000u)]
    public void FindsThePreferredAdvisedBoundaryWithoutScanningEveryAmount(bool preferMinimum, bool increasing, uint boundary, uint expected)
    {
        int queries = 0;
        var amount = AdvisedAmount.Find(1,1000000,preferMinimum, value =>
        {
            queries++;
            bool favorable = increasing ? value >= boundary : value <= boundary;
            return new(CommandRejection.None,7,favorable ? AdviserOpinion.Favorable : AdviserOpinion.Unfavorable);
        });
        Assert.Equal(expected,amount);
        Assert.InRange(queries,1,22);
    }

    [Theory]
    [InlineData(AdviserOpinion.Unfavorable, CommandRejection.None)]
    [InlineData(AdviserOpinion.Unavailable, CommandRejection.None)]
    [InlineData(AdviserOpinion.NoAdviser, CommandRejection.None)]
    [InlineData(AdviserOpinion.Uncertain, CommandRejection.None)]
    [InlineData(AdviserOpinion.Favorable, CommandRejection.InvalidTask)]
    public void DoesNotInventARecommendationWithoutValidFavorableAdvice(AdviserOpinion opinion, CommandRejection rejection)
    {
        foreach (bool preferMinimum in new[] { true,false })
            Assert.Null(AdvisedAmount.Find(1,1000000,preferMinimum,_ => new(rejection,7,opinion)));
    }

    [Fact]
    public void SupportsASingleLegalAmountAndAvoidsOverflowAtTheUnsignedBoundary()
    {
        Assert.Equal(42u,AdvisedAmount.Find(42,42,true,_ => new(CommandRejection.None,7,AdviserOpinion.Favorable)));
        Assert.Null(AdvisedAmount.Find(42,42,false,_ => new(CommandRejection.None,7,AdviserOpinion.Unfavorable)));
        Assert.Equal(uint.MaxValue,AdvisedAmount.Find(1,uint.MaxValue,true,
            value => new(CommandRejection.None,7,value == uint.MaxValue ? AdviserOpinion.Favorable : AdviserOpinion.Unfavorable)));
    }
}
