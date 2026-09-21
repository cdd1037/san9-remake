using System.Collections.Immutable;

namespace San9.Core.Simulation;

/// <summary>Preserves the next unfinished seat, or the completed round, when force identities change.</summary>
internal static class ControlRotation
{
    internal static void Replace(WorldMutation world, ImmutableArray<ControlSeat> controls)
    {
        var simulation = world.Simulation;
        var remaining = simulation.Controls.Skip(simulation.ActiveControl)
            .FirstOrDefault(seat => controls.Any(c => c.ForceId == seat.ForceId));
        int next = remaining is null ? controls.Length :
            controls.IndexOf(controls.Single(c => c.ForceId == remaining.ForceId));
        world.SetSimulation(simulation with { Controls = controls, ActiveControl = next });
    }
}
