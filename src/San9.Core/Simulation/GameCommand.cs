using System.Text.Json.Serialization;

namespace San9.Core.Simulation;

public sealed record RespondDailyTaskCommand(Guid CommandId, int IssuerId, long Sequence, Guid TaskId,
    [property: JsonRequired] int Step, bool Accept)
    : GameCommand(CommandId, IssuerId, Sequence)
{
    [JsonRequired] public CommunitySearchChoice? CommunitySearch { get; init; }
    [JsonRequired] public SearchConflictChoice? SearchConflict { get; init; }
    [JsonRequired] public MerchantInvestmentResponse? MerchantInvestment { get; init; }
    [JsonRequired] public CompensationOffer? Offer { get; init; }
    [JsonRequired] public int? CountryTitleId { get; init; }
}

[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(WaitCommand), "wait")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(RespondDailyTaskCommand), "respond-daily-task")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(GiftCommand), "gift")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(RequestAttackCommand), "request-attack")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(DemandSurrenderCommand), "demand-surrender")]
[JsonDerivedType(typeof(RespondEmperorTransferCommand), "respond-emperor-transfer")]
[JsonDerivedType(typeof(ChooseCountryTitleCommand), "choose-country-title")]
[JsonDerivedType(typeof(RespondOfficeRequestCommand), "respond-office-request")]
[JsonDerivedType(typeof(ChooseSnipingSuccessorCommand), "choose-sniping-successor")]
[JsonDerivedType(typeof(ChooseCapturedRulerDispositionCommand), "choose-captured-ruler-disposition")]
[JsonDerivedType(typeof(ReturnPrisonerCommand), "return-prisoner")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AllianceParticipationCommand), "alliance-participation")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BurnCommand), "strategy-burn")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AlienateCommand), "strategy-alienate")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SeizeCommand), "strategy-seize")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(GossipCommand), "strategy-gossip")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(UnitStrategyCommand), "unit-strategy")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SelectTacticCommand), "select-tactic")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(DeployUnitCommand), "deploy-unit")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SetUnitPoliciesCommand), "set-unit-policies")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(OrderUnitCommand), "order-unit")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ReformUnitCommand), "reform-unit")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(DemolishFacilityCommand), "demolish-facility")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ConstructFacilityCommand), "construct-facility")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AppointAdviserCommand), "appoint-adviser")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SearchCommand), "search")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(RecruitOfficerCommand), "recruit-officer")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(MoveOfficersCommand), "move-officers")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(RewardOfficerCommand), "reward-officer")]
[JsonDerivedType(typeof(FoodTradeCommand), "food-trade")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(DevelopCommand), "develop")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(StartTaskCommand), "start-task")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(GrantTreasureCommand), "grant-treasure")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ConfiscateTreasureCommand), "confiscate-treasure")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ExileOfficerCommand), "exile-officer")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(DissolveCorpsCommand), "dissolve-corps")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CreateCorpsCommand), "create-corps")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ChangeCorpsCommand), "change-corps")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ReleasePrisonerCommand), "release-prisoner")]
[JsonDerivedType(typeof(ChooseTacticStudentCommand), "choose-tactic-student")]
[JsonDerivedType(typeof(ExecutePrisonerCommand), "execute-prisoner")]
[JsonDerivedType(typeof(ChooseDuelSuccessorCommand), "choose-duel-successor")]
[JsonDerivedType(typeof(ChooseExecutionSuccessorCommand), "choose-execution-successor")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AssignOfficesCommand), "assign-offices")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SetCorpsControlCommand), "set-corps-control")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SetCorpsDelegationCommand), "set-corps-delegation")]
public abstract record GameCommand(
    Guid CommandId,
    int IssuerId,
    long Sequence);

public sealed record WaitCommand(
    Guid CommandId,
    int IssuerId,
    long Sequence)
    : GameCommand(CommandId, IssuerId, Sequence);

public sealed record DevelopCommand(Guid CommandId, int IssuerId, long Sequence,
    San9.Core.Domain.DevelopmentCommand Development) : GameCommand(CommandId, IssuerId, Sequence);

internal sealed record StartTaskCommand(Guid CommandId, int IssuerId, long Sequence,
    string Kind, int OfficerId, int TargetId) : GameCommand(CommandId, IssuerId, Sequence);

public enum CommandRejection
{
    None = 0,
    NegativeIssuerId,
    DuplicateCommandId,
    NegativeSequence,
    InvalidCommandId,
    WrongSequence,
    NotActiveController,
    NotStable,
    UnsupportedCommand,
    InvalidTask,
    DevelopmentFailed,
    FoodTradeFailed,
    UnknownPerson,
    PersonUnavailable,
    AlreadyRewarded,
    LoyaltyAtMaximum,
    InsufficientFunds,
    UnknownTreasure,
    TreasureUnavailable,
    NoDestination,
    InvalidCorps,
    ResourceOverflow,
    InvalidOffice,
    MissingFormationTactic,
    TacticUnavailable,
    AttackConfirmationRequired,
    InvalidDecision,
    DecisionPending,
    SettlementConfused,
    FormationUnavailable,
}

public readonly record struct CommandSubmissionResult(
    bool Accepted,
    CommandRejection Rejection)
{
    public static CommandSubmissionResult Success { get; } = new(true, CommandRejection.None);

    public static CommandSubmissionResult Reject(CommandRejection reason) => new(false, reason);
}

public sealed record CommandExecutionResult(CommandSubmissionResult Submission,
    IReadOnlyList<DomainEvent> Events, WorldSnapshot Snapshot,
    San9.Core.Domain.DevelopmentOutcome? Development = null);
