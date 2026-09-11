namespace San9.Core.Simulation;

public abstract record GameCommand(
    Guid CommandId,
    int IssuerId,
    long Sequence);

public sealed record WaitCommand(
    Guid CommandId,
    int IssuerId,
    long Sequence)
    : GameCommand(CommandId, IssuerId, Sequence);

public enum CommandRejection
{
    None = 0,
    NegativeIssuerId,
    DuplicateCommandId,
    NegativeSequence,
}

public readonly record struct CommandSubmissionResult(
    bool Accepted,
    CommandRejection Rejection)
{
    public static CommandSubmissionResult Success { get; } = new(true, CommandRejection.None);

    public static CommandSubmissionResult Reject(CommandRejection reason) => new(false, reason);
}
