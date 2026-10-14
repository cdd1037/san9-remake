using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Bounded personnel, reinforcement and economic work for automatic controllers.</summary>
internal static class AutomaticTurnRunner
{
    internal static void EndControl(SimulationEngine engine, CommandContext context, MilitaryRulebook? military, List<DomainEvent> events, ref AiSessionState ai,
        List<AllocationTrace>? traces = null, List<AllocationBatchTrace>? batches = null)
    {
        var state = engine.Snapshot.Simulation;
        if (state.Boundary != SimulationBoundary.Stable || state.ActiveControl >= state.Controls.Length ||
            state.Controls[state.ActiveControl].ControllerId != context.ControllerId)
            throw new InvalidOperationException("Not the active stable controller.");
        ai = AllocationTurnRunner.Run(engine, state.Controls[state.ActiveControl], military, ai, events, traces, batches);
        AutomaticResponseRunner.Run(engine, events);
        if (engine.Snapshot.Simulation.Boundary == SimulationBoundary.Stable)
            events.AddRange(engine.EndControl(context).Events);
        AutomaticResponseRunner.Run(engine, events);
    }
}
