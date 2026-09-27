namespace San9.Client.Godot;

public enum DeploymentStage { None, Preparation, Targets, TargetModal }

/// <summary>Independent turn, command and modal state determine available input.</summary>
public readonly record struct StrategyInteraction(bool CanUseMap, bool CanNavigate,
    bool CanIssueOrders, bool CanToggleBoundaries, bool ShowSidebar)
{
    public static StrategyInteraction Resolve(bool ready, TurnScreen? turn, bool canAdvance,
        DeploymentStage deployment, bool overview, bool modal)
    {
        bool browsing = ready && turn is TurnScreen.Strategy or TurnScreen.Playing;
        bool commandModal = deployment is DeploymentStage.Preparation or DeploymentStage.TargetModal;
        bool map = browsing && !modal && !commandModal;
        bool orders = ready && canAdvance && deployment == DeploymentStage.None;
        return new(map, map && !overview, orders,
            browsing && !overview && !modal && !commandModal,
            ready && turn is TurnScreen.Strategy or TurnScreen.Confirmation && deployment != DeploymentStage.Targets && deployment != DeploymentStage.TargetModal);
    }
}
