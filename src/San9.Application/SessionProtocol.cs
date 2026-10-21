using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

public static class SessionProtocol
{
    public const string RulesVersion = "san9-core-g10-v170";
    public const int SaveFormatVersion = 122;
    public const int LogFormatVersion = 129;
}

/// <summary>Non-authoritative load diagnostics; never included in saves or deterministic hashes.</summary>
public sealed record RulesVersionWarning(string SavedVersion, string CurrentVersion)
{
    public string Message => $"档案规则版本为 {SavedVersion}，当前为 {CurrentVersion}。已按当前规则载入，后续结算或回放结果可能不同。";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CommandOperation), "command")]
[JsonDerivedType(typeof(EndControlOperation), "end-control")]
[JsonDerivedType(typeof(AdvanceOperation), "advance-xun")]
[JsonDerivedType(typeof(PrepareAdvanceOperation), "prepare-advance")]
[JsonDerivedType(typeof(AdvanceDayOperation), "advance-day")]
[JsonDerivedType(typeof(ChronicleOperation), "chronicle-note")]
[JsonDerivedType(typeof(EndingOperation), "ending")]
[JsonDerivedType(typeof(PkSettingsOperation), "pk-settings")]
[JsonDerivedType(typeof(ForceControlOperation), "force-control")]
[JsonDerivedType(typeof(HandOverAllForcesOperation), "hand-over-all-forces")]
[JsonDerivedType(typeof(WorldEditOperation), "world-edit")]
public abstract record SessionOperation;
public sealed record WorldEditOperation(WorldEditorKind Kind, ImmutableArray<WorldEdit> Edits) : SessionOperation;
public sealed record ForceControlOperation(int ForceId, ControllerKind Kind) : SessionOperation;
public sealed record HandOverAllForcesOperation : SessionOperation;
public sealed record PkSettingsOperation(bool OtherForceOrdersVisible,
    bool OtherForceTacticsVisible, bool BattleDeathsEnabled) : SessionOperation;
public sealed record CommandOperation(GameCommand Command) : SessionOperation;
public sealed record EndControlOperation : SessionOperation;
public sealed record AdvanceOperation : SessionOperation;
public sealed record PrepareAdvanceOperation : SessionOperation;
public sealed record AdvanceDayOperation : SessionOperation;
public sealed record ChronicleOperation(string Text) : SessionOperation;
public sealed record EndingSelection([property: JsonRequired] EndingPolicy Policy, [property: JsonRequired] EndingOfficeholders? Officeholders);
public sealed record EndingOperation(EndingSelection Selection) : SessionOperation;

public sealed record SessionRequest(Guid Id, long Sequence, string ControllerId, SessionOperation Operation);
public enum SessionDisposition { Accepted, AlreadyProcessed, Rejected }
public enum SessionRejection { None, InvalidRequest, OutOfOrder, CommandRejected, NotStable, Terminal, AutomaticResponseFailed }

public sealed record SessionResult(SessionDisposition Disposition, SessionRejection Rejection,
    IReadOnlyList<DomainEvent> Events, WorldSnapshot Snapshot,
    CommandRejection CommandRejection = CommandRejection.None, DevelopmentOutcome? Development = null, AI.AutomaticResponseFailure? AutomaticResponseFailure = null);

public sealed record RecordedOperation(SessionRequest Request, ImmutableArray<DomainEvent> Events);
public sealed record SessionRecording(SessionSnapshot Initial, ImmutableArray<RecordedOperation> Operations);

public sealed record SessionSnapshot(WorldSnapshot World, ImmutableArray<SessionRecord> Records,
    ImmutableArray<ChronicleEntry> Chronicle, long NextOperationSequence)
{
    [JsonRequired] public ImmutableArray<ChronicleFact> History { get; init; } = [];
    [JsonRequired] public EndingSelection? Ending { get; init; }
    [JsonRequired] public ImmutableDictionary<int, Presentation.StrategicContextState> StrategicContexts { get; init; } = ImmutableDictionary<int, Presentation.StrategicContextState>.Empty;
    [JsonRequired] public ControllerKind PlayerForceController { get; init; } = ControllerKind.Player;
    [JsonRequired] public AI.OriginalAiState OriginalAi { get; init; } = new();
}
