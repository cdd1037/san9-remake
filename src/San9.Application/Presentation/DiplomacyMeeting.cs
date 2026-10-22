namespace San9.Application.Presentation;

/// <summary>The occurrence's participants; no later world state is used to resolve a meeting.</summary>
public sealed record DiplomacyMeeting(Guid TaskId, int HostForceId, int EnvoyId);
