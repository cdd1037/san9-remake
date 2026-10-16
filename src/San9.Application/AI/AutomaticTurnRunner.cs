using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Control completion and mandatory responses; proactive AI is not installed.</summary>
internal static class AutomaticTurnRunner
{
    internal static void EndControl(SimulationEngine engine, CommandContext context, List<DomainEvent> events)
    {
        var state = engine.Snapshot.Simulation;
        if (state.Boundary != SimulationBoundary.Stable || state.ActiveControl >= state.Controls.Length ||
            state.Controls[state.ActiveControl].ControllerId != context.ControllerId)
            throw new InvalidOperationException("Not the active stable controller.");
        events.AddRange(engine.EndControl(context).Events);
        AutomaticResponseRunner.Run(engine, events);
    }
}
