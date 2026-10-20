using San9.Application.Presentation;

namespace San9.Client.Godot;

public enum MilitaryCommandStage { None, Preparation, Targets, TargetModal }

/// <summary>Independent turn, command and modal state determine available input.</summary>
public readonly record struct StrategyInteraction(bool CanUseMap, bool CanNavigate,
    bool CanIssueOrders, bool ShowSidebar)
{
    public bool CanSave { get; init; }
    public bool CanAdvance { get; init; }
    public bool CanContinue { get; init; }
    public bool CanPlay { get; init; }
    public bool CanInspectMap { get; init; }
    public MapInformationContext InformationContext { get; init; }

    public static StrategyInteraction Resolve(bool ready, TurnScreen? turn, bool canAdvance,
        MilitaryCommandStage command, bool overview, bool modal, InteractionBlock blocked = InteractionBlock.None,
        bool sidebarMoving = false, bool navigationBlocked = false, bool narrationQueued = false, bool hasPlayerForce = true)
    {
        bool browsing = ready && turn is TurnScreen.Strategy or TurnScreen.Playing;
        bool commandModal = command is MilitaryCommandStage.Preparation or MilitaryCommandStage.TargetModal;
        bool map = browsing && !modal && !commandModal;
        bool orders = ready && canAdvance && command == MilitaryCommandStage.None && !sidebarMoving &&
            !blocked.HasFlag(InteractionBlock.Orders);
        return new(map, map && !overview && !navigationBlocked, orders && hasPlayerForce,
            ready && hasPlayerForce && turn is TurnScreen.Strategy or TurnScreen.Confirmation && command != MilitaryCommandStage.Targets && command != MilitaryCommandStage.TargetModal)
        {
            CanSave = orders && turn == TurnScreen.Strategy && !modal,
            CanAdvance = orders && !modal,
            CanContinue = ready && !blocked.HasFlag(InteractionBlock.Continuation) && !narrationQueued,
            CanPlay = ready && !blocked.HasFlag(InteractionBlock.Playback),
            // Daily publication blocks input while retaining the visible map and its information.
            CanInspectMap = ready && turn is TurnScreen.Strategy or TurnScreen.Playing or TurnScreen.Presenting && !modal && !commandModal,
            InformationContext = turn is TurnScreen.Playing or TurnScreen.Presenting
                ? MapInformationContext.Execution : MapInformationContext.Strategy
        };
    }
}
