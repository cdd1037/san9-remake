using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

public static class SessionProtocol
{
    public const string RulesVersion = "san9-core-g10-v280";
    public const int SaveFormatVersion = 137;
    public const int LogFormatVersion = 181;
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
[JsonDerivedType(typeof(ConfigureOfficerAppearanceOperation), "configure-officer-appearance")]
[JsonDerivedType(typeof(ConfigureOfficerAppearancesOperation), "configure-officer-appearances")]
[JsonDerivedType(typeof(ConfigureIndividualAppearancesOperation), "configure-individual-appearances")]
[JsonDerivedType(typeof(SaveDeploymentPresetOperation), "save-deployment-preset")]
[JsonDerivedType(typeof(DeleteDeploymentPresetOperation), "delete-deployment-preset")]
public abstract record SessionOperation;
public sealed record SaveDeploymentPresetOperation(DeploymentPreset Preset) : SessionOperation;
public sealed record DeleteDeploymentPresetOperation(Guid PresetId) : SessionOperation;
public sealed record WorldEditOperation(WorldEditorKind Kind, ImmutableArray<WorldEdit> Edits) : SessionOperation
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<EquipmentInstanceEdit>? EquipmentEdits { get; init; }
}
public sealed record ConfigureOfficerAppearanceOperation(int OfficerId, ManualOfficerAppearance Plan) : SessionOperation;
public sealed record ConfigureOfficerAppearancesOperation(ImmutableArray<int> OfficerIds, ManualOfficerAppearance Plan) : SessionOperation;
public sealed record OfficerAppearanceAssignment(int OfficerId, ManualOfficerAppearance Plan);
public sealed record ConfigureIndividualAppearancesOperation(ImmutableArray<OfficerAppearanceAssignment> Assignments) : SessionOperation;
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

public sealed record SessionSnapshot(WorldSnapshot World, ImmutableArray<SessionRecord> Records,
    ImmutableArray<ChronicleEntry> Chronicle, long NextOperationSequence)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? PersonalPeerageInitialized { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? InitialEquipmentIssued { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ArmamentMigrationInitialized { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<string>? InitializedSearchEquipment { get; init; }
    [JsonRequired] public San9.Content.Scenarios.SessionDefinitions Definitions { get; init; } = San9.Content.Scenarios.SessionDefinitions.Empty;
    [JsonRequired] public ImmutableArray<DeploymentPreset> DeploymentPresets { get; init; } = [];
    [JsonRequired] public ImmutableList<ChronicleFact> History { get; init; } = [];
    [JsonRequired] public EndingSelection? Ending { get; init; }
    [JsonRequired] public ImmutableDictionary<int, Presentation.StrategicContextState> StrategicContexts { get; init; } = ImmutableDictionary<int, Presentation.StrategicContextState>.Empty;
    [JsonRequired] public ImmutableArray<int> InitialPlayerForceIds { get; init; } = [];
    [JsonRequired] public ControllerKind PlayerForceController { get; init; } = ControllerKind.Player;
    [JsonRequired] public AI.OriginalAiState OriginalAi { get; init; } = new();
}
