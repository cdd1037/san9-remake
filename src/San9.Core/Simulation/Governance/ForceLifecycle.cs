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
        // Loyalty belongs to a live force identity, not to its former ruler or historical record.
        // Settle transfers first: surrender and consolidation still need the old loyalty values.
        foreach (var person in world.AllOfficers.Values.Where(p => p.ForceLoyalties.ContainsKey(forceId)).OrderBy(p => p.Id).ToArray())
        {
            world.Replace(person with { ForceLoyalties = person.ForceLoyalties.Remove(forceId) });
            if (person.LoyaltyTo(forceId) != 0)
                events.Add(new OfficerLoyaltyChangedEvent(world.Clock, person.Id, forceId, person.LoyaltyTo(forceId), 0, "force-retired"));
        }
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
