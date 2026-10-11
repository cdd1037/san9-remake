namespace San9.Application.Presentation;

/// <summary>Independent projections of committed facts, published without narration callbacks.</summary>
public sealed record ExecutionOutput(IReadOnlyList<CommandMessage> Messages, IReadOnlyList<MapMessageRecord> Records);
