namespace San9.Core.Simulation;

[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(WaitCommand), "wait")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AppointAdviserCommand), "appoint-adviser")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SearchPersonsCommand), "search-persons")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(RecruitOfficerCommand), "recruit-officer")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(MoveOfficersCommand), "move-officers")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(RewardOfficerCommand), "reward-officer")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(DevelopCommand), "develop")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(StartTaskCommand), "start-task")]
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
    UnknownPerson,
    PersonUnavailable,
    AlreadyRewarded,
    LoyaltyAtMaximum,
    InsufficientFunds,
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
