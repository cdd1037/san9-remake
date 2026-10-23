using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Only retained with the current prepared operation; never part of saved session state.</summary>
public sealed record ResolutionPresentationTransition(ResolutionPhase Phase, int EventIndex, StrategySnapshot View);
