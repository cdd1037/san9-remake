namespace San9.Client.Godot.Tests;

public sealed class StrategyInteractionTests
{
    [Fact]
    public void HiddenWorkflowRetainsRestrictionsUntilLastOwnerCloses()
    {
        var locks = new InteractionLocks();
        var first = locks.Acquire(InteractionBlock.Orders);
        var second = locks.Acquire(InteractionBlock.Orders);
        StrategyInteraction Read() => StrategyInteraction.Resolve(true, TurnScreen.Strategy, true,
            MilitaryCommandStage.None, false, false, locks.Blocked);
        Assert.True(Read().CanNavigate);
        Assert.False(Read().CanIssueOrders);
        Assert.False(Read().CanSave);
        Assert.False(Read().CanAdvance);
        first.Dispose(); first.Dispose();
        Assert.False(Read().CanIssueOrders);
        second.Dispose();
        Assert.True(Read().CanIssueOrders);
        Assert.True(Read().CanSave);
        Assert.True(Read().CanAdvance);
    }

    [Fact]
    public void DecisionAndNarrationHaveIndependentPlaybackLifetimes()
    {
        var locks = new InteractionLocks();
        var decision = locks.Acquire(InteractionBlock.Continuation);
        var narration = locks.Acquire(InteractionBlock.Continuation | InteractionBlock.Playback);
        StrategyInteraction Read(bool queued = false) => StrategyInteraction.Resolve(true, TurnScreen.Playing, false,
            MilitaryCommandStage.None, false, false, locks.Blocked, narrationQueued: queued);
        Assert.False(Read().CanPlay);
        narration.Dispose();
        Assert.True(Read().CanPlay);
        Assert.False(Read().CanContinue);
        decision.Dispose();
        Assert.False(Read(true).CanContinue);
        Assert.True(Read().CanContinue);
    }

    [Fact]
    public void ModalAndNavigationRestrictionsHaveSeparateCapabilities()
    {
        var modal = StrategyInteraction.Resolve(true, TurnScreen.Strategy, true, MilitaryCommandStage.None, false, true);
        Assert.False(modal.CanSave);
        Assert.False(modal.CanAdvance);
        var popup = StrategyInteraction.Resolve(true, TurnScreen.Strategy, true, MilitaryCommandStage.None, false, false,
            navigationBlocked: true);
        Assert.False(popup.CanNavigate);
        Assert.True(popup.CanUseMap);
    }

    [Fact]
    public void DailyPublicationKeepsMapInformationWithoutEnablingInput()
    {
        var playing = StrategyInteraction.Resolve(true, TurnScreen.Playing, false, MilitaryCommandStage.None, false, false);
        var publishing = StrategyInteraction.Resolve(true, TurnScreen.Presenting, false, MilitaryCommandStage.None, false, false);
        Assert.True(playing.CanInspectMap);
        Assert.True(publishing.CanInspectMap);
        Assert.False(publishing.CanUseMap);
        Assert.False(publishing.CanNavigate);
        Assert.False(publishing.CanIssueOrders);
        Assert.False(StrategyInteraction.Resolve(true, TurnScreen.Presenting, false, MilitaryCommandStage.None, false, true).CanInspectMap);
    }

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
