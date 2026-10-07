namespace San9.Client.Godot.Tests;

public sealed class StrategyInteractionTests
{
    [Theory]
    [InlineData(TurnScreen.Strategy, true, true)]
    [InlineData(TurnScreen.Playing, true, false)]
    [InlineData(TurnScreen.Confirmation, false, false)]
    [InlineData(TurnScreen.Entering, false, false)]
    [InlineData(TurnScreen.AwaitingDecision, false, false)]
    public void TurnSeparatesNavigationFromOrders(TurnScreen screen, bool navigate, bool orders)
    {
        var result = StrategyInteraction.Resolve(true, screen, screen == TurnScreen.Strategy, MilitaryCommandStage.None, false, false);
        Assert.Equal(navigate, result.CanNavigate);
        Assert.Equal(orders, result.CanIssueOrders);
        Assert.Equal(navigate, result.CanToggleBoundaries);
    }
    [Theory]
    [InlineData(MilitaryCommandStage.Preparation, false)]
    [InlineData(MilitaryCommandStage.Targets, true)]
    [InlineData(MilitaryCommandStage.TargetModal, false)]
    public void MilitaryCommandAllowsMapInputOnlyForTargetPicking(MilitaryCommandStage stage, bool canUseMap)
    {
        var result = StrategyInteraction.Resolve(true, TurnScreen.Strategy, true, stage, false, false);
        Assert.Equal(canUseMap, result.CanUseMap);
        Assert.False(result.CanIssueOrders);
        var modal = StrategyInteraction.Resolve(true, TurnScreen.Strategy, true, stage, false, true);
        Assert.False(modal.CanUseMap);
        Assert.False(modal.CanNavigate);
    }
    [Fact]
    public void OverviewAndStartupBlockNavigation()
    {
        Assert.False(StrategyInteraction.Resolve(true, TurnScreen.Strategy, true, MilitaryCommandStage.None, true, false).CanNavigate);
        Assert.Equal(default, StrategyInteraction.Resolve(false, null, false, MilitaryCommandStage.None, false, false));
    }
}
