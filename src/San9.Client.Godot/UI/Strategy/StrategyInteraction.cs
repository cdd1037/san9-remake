using San9.Application.Presentation;

namespace San9.Client.Godot;

public enum MilitaryCommandStage { None, Preparation, Targets, TargetModal }

/// <summary>Independent turn, command and modal state determine available input.</summary>
public readonly record struct StrategyInteraction(bool CanUseMap, bool CanNavigate,
    bool CanIssueOrders, bool ShowSidebar)
{
    public bool CanInspectMap { get; init; }
    public MapInformationContext InformationContext { get; init; }

    public static StrategyInteraction Resolve(bool ready, TurnScreen? turn, bool canAdvance,
        MilitaryCommandStage command, bool overview, bool modal)
    {
        bool browsing = ready && turn is TurnScreen.Strategy or TurnScreen.Playing;
        bool commandModal = command is MilitaryCommandStage.Preparation or MilitaryCommandStage.TargetModal;
        bool map = browsing && !modal && !commandModal;
        bool orders = ready && canAdvance && command == MilitaryCommandStage.None;
        return new(map, map && !overview, orders,
            ready && turn is TurnScreen.Strategy or TurnScreen.Confirmation && command != MilitaryCommandStage.Targets && command != MilitaryCommandStage.TargetModal)
        {
            // Daily publication blocks input while retaining the visible map and its information.
            CanInspectMap = ready && turn is TurnScreen.Strategy or TurnScreen.Playing or TurnScreen.Presenting && !modal && !commandModal,
            InformationContext = turn is TurnScreen.Playing or TurnScreen.Presenting
                ? MapInformationContext.Execution : MapInformationContext.Strategy
        };
    }
}
