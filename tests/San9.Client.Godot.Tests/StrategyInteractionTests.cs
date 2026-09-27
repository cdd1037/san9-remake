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
        var result = StrategyInteraction.Resolve(true, screen, screen == TurnScreen.Strategy, DeploymentStage.None, false, false);
        Assert.Equal(navigate, result.CanNavigate);
        Assert.Equal(orders, result.CanIssueOrders);
        Assert.Equal(navigate, result.CanToggleBoundaries);
    }
    [Theory]
    [InlineData(DeploymentStage.Preparation, false)]
    [InlineData(DeploymentStage.Targets, true)]
    [InlineData(DeploymentStage.TargetModal, false)]
    public void DeploymentAllowsMapInputOnlyForTargetPicking(DeploymentStage stage, bool canUseMap)
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
        Assert.False(StrategyInteraction.Resolve(true, TurnScreen.Strategy, true, DeploymentStage.None, true, false).CanNavigate);
        Assert.Equal(default, StrategyInteraction.Resolve(false, null, false, DeploymentStage.None, false, false));
    }
}
