using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Unique automatic control entry, including the original economic work cycle.</summary>
internal static class AutomaticTurnRunner
{
    internal static OriginalAiState EndControl(SimulationEngine engine, CommandContext context, List<DomainEvent> events,
        ScenarioSessionDefinition definition, OriginalAiState ai, List<OriginalAiWorkReport> reports)
    {
        var state = engine.Snapshot.Simulation;
        if (state.Boundary != SimulationBoundary.Stable || state.ActiveControl >= state.Controls.Length ||
            state.Controls[state.ActiveControl].ControllerId != context.ControllerId)
            throw new InvalidOperationException("Not the active stable controller.");
        ai = OriginalAiTurnRunner.Run(engine, definition, state.Controls[state.ActiveControl], ai, events, reports);
        events.AddRange(engine.EndControl(context).Events);
        AutomaticResponseRunner.Run(engine, events);
        return ai;
    }
}
