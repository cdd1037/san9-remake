namespace San9.Core.Simulation;

public enum ResolutionPhase { Daily, XunClosing, XunOpening }

/// <summary>A transient boundary within one calculation, independent of domain facts and persistence.</summary>
public sealed record ResolutionTransition(ResolutionPhase Phase, int EventIndex, WorldSnapshot Snapshot);
