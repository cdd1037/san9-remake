namespace San9.Application.Presentation;

public sealed record ExecutionOutputStep(IReadOnlyList<CommandMessage> Messages, IReadOnlyList<MapMessageRecord> Records);

/// <summary>Ordered occurrence feedback. Each step publishes its records after its narration.</summary>
public sealed record ExecutionOutput
{
    public IReadOnlyList<ExecutionOutputStep> Steps { get; }
    public IReadOnlyList<CommandMessage> Messages { get; }
    public IReadOnlyList<MapMessageRecord> Records { get; }

    public ExecutionOutput(IReadOnlyList<CommandMessage> messages, IReadOnlyList<MapMessageRecord> records)
        : this([new(messages, records)]) { }

    public ExecutionOutput(IReadOnlyList<ExecutionOutputStep> steps)
    {
        Steps = steps.ToArray();
        Messages = Steps.SelectMany(s => s.Messages).ToArray();
        Records = Steps.SelectMany(s => s.Records).ToArray();
    }

    public IReadOnlySet<string> FeedbackIds { get; init; } = System.Collections.Immutable.ImmutableHashSet<string>.Empty;
}
