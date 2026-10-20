using System.Collections.Immutable;

namespace San9.Core.Simulation;

public sealed partial class SimulationEngine
{
    /// <summary>Changes control without reopening completed seats or changing orders and corps delegation.</summary>
    public void SetForceControl(int forceId, ControllerKind kind)
    {
        if (_world.IsExecuting || _world.Simulation.Boundary != SimulationBoundary.Stable)
            throw new InvalidOperationException("Control changes require a stable strategy boundary.");
        var state = Snapshot;
        if (!Enum.IsDefined(kind) || !state.Forces.Any(f => f.Id == forceId && !f.IsDormant && !f.IsEliminated && !f.IsBarbarian) ||
            !state.Simulation.Controls.Any(c => c.ForceId == forceId && c.Kind != kind))
            throw new ArgumentException("Invalid force control transition.");
        _world.EnterExecution();
        try
        {
            var mutation = _world.BeginMutation(_rules.Abilities, _rules.Economy);
            var controls = mutation.Simulation.Controls.Select(c =>
                c.ForceId == forceId ? new ControlSeat(kind == ControllerKind.Player ? "player" : $"ai:{c.ForceId}", c.ForceId, kind) :
                c).ToArray();
            var ordered = controls.Take(state.Simulation.ActiveControl).Concat(controls.Skip(state.Simulation.ActiveControl)
                .OrderBy(c => c.Kind == ControllerKind.Player ? 0 : 1));
            mutation.SetSimulation(mutation.Simulation with { Controls = ordered.ToImmutableArray() });
            _world.Commit(mutation);
        }
        finally { _world.ExitExecution(); }
    }
}
