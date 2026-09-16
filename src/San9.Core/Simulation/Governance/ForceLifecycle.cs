using System.Collections.Immutable;

namespace San9.Core.Simulation;

/// <summary>Closes a force identity after its personnel, assets and corps have been settled.</summary>
internal static class ForceLifecycle
{
    internal static void Retire(WorldMutation world, int forceId, List<DomainEvent> events)
    {
        AllianceDiplomacy.ForceEliminated(world, forceId, events);
        foreach (var relation in world.Relations.Values.Where(r => r.FirstId == forceId || r.SecondId == forceId).ToArray()) world.Replace(relation with { Value = 0 });
        foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
            ResourceAccounts.Change(world, new(ResourceOwnerKind.Force, forceId), resource,
                -(long)ResourceAccounts.Balance(world, new(ResourceOwnerKind.Force, forceId), resource), "force-eliminated", null, events);
        world.Replace(world.Forces[forceId] with { IsEliminated = true, LeaderId = null, AdviserId = null });
        var controls = world.Simulation.Controls.Where(c => c.ForceId != forceId).ToImmutableArray();
        bool playerControlled = world.Simulation.Controls.Any(c => c.ForceId == forceId && c.Kind == ControllerKind.Player);
        foreach (var seat in world.Simulation.Controls.Where(c => c.ForceId == forceId))
            events.Add(new ControlEndedEvent(seat.ControllerId, forceId));
        world.SetSimulation(world.Simulation with { Controls = controls, ActiveControl = Math.Min(controls.Length, world.Simulation.ActiveControl) });
        events.Add(new ForceEliminatedEvent(world.Clock, forceId, playerControlled));
        AttackRequests.Cleanup(world, events);
    }
}
