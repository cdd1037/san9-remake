using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Defines territorial survival and closes force identities after their personnel, assets and corps are settled.</summary>
internal static class ForceLifecycle
{
    internal static bool HasCity(WorldMutation world, int forceId) => world.Settlements.Values.Any(s =>
        s.OwnerId == forceId && s.Kind == SettlementKind.City && !s.IsDestroyed && !s.IsDormant);

    internal static void Retire(WorldMutation world, int forceId, List<DomainEvent> events)
    {
        AllianceDiplomacy.ForceEliminated(world, forceId, events);
        foreach (var relation in world.Relations.Values.Where(r => r.FirstId == forceId || r.SecondId == forceId).ToArray()) world.Replace(relation with { Value = 0 });
        world.Replace(world.Forces[forceId] with { IsEliminated = true, LeaderId = null, AdviserId = null, CountryTitleId = null });
        var controls = world.Simulation.Controls.Where(c => c.ForceId != forceId).ToImmutableArray();
        bool playerControlled = world.Simulation.Controls.Any(c => c.ForceId == forceId && c.Kind == ControllerKind.Player);
        foreach (var seat in world.Simulation.Controls.Where(c => c.ForceId == forceId))
            events.Add(new ControlEndedEvent(seat.ControllerId, forceId));
        ControlRotation.Replace(world, controls);
        events.Add(new ForceEliminatedEvent(world.Clock, forceId, playerControlled));
        AttackRequests.Cleanup(world, events);
    }
}
