using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ForceActivatedBySearchEvent(GameClock Clock, string EventId, int SearchingForceId,
    int SearchingOfficerId, int ActivatedForceId, int CityId, ImmutableArray<int> SettlementIds,
    ImmutableArray<int> OfficerIds) : DomainEvent;

internal static class ForceActivationSearch
{
    // A restored campaign can outlive its original activation configuration. Check the
    // group at the actual trigger before modifying any of its records.
    internal static bool HasDormantGroup(WorldMutation world, ForceActivationSearchEventDefinition d,
        ForceState force, SettlementState city) =>
        force.IsDormant && city.IsDormant && city.OwnerId == force.Id && city.SoldierCapacity >= d.CitySoldiers &&
        force.LeaderId is int leader && d.OfficerIds.Contains(leader) &&
        (force.AdviserId is not int adviser || d.OfficerIds.Contains(adviser)) &&
        d.AttachedSettlementIds.All(id => world.AllSettlements.TryGetValue(id, out var place) && place.IsDormant) &&
        d.OfficerIds.All(id => world.AllOfficers.TryGetValue(id, out var officer) && officer.IsDormant &&
            officer.OwnerId == force.Id && officer.SettlementId == city.Id);

    internal static bool TryResolve(WorldMutation world, PersistentTask task, ForceActivationSearchEventDefinition d,
        SimulationRules rules, List<DomainEvent> events)
    {
        if (world.Simulation.ConsumedEventKeys.Contains(d.ConsumptionKey) || task.Journey?.DestinationPlaceId != d.PlaceId ||
            !world.Forces.TryGetValue(task.ForceId, out var searching) || searching.IsEliminated || searching.Trust < d.MinimumTrust ||
            !world.Simulation.Controls.Any(c => c.ForceId == task.ForceId && c.Kind == ControllerKind.Player) ||
            !world.AllForces.TryGetValue(d.ForceId, out var force) ||
            !world.AllSettlements.TryGetValue(d.CityId, out var city) || !HasDormantGroup(world, d, force, city) ||
            PoliticalEvents.ControlledProvinces(world, rules.Personnel, task.ForceId).Length < d.MinimumProvinces ||
            rules.Military is null || world.Units.Values.Any(u => u.EffectiveOrderClass == UnitOrderClass.Military &&
                rules.Military.RegionAt(u.Position) == d.CityId)) return false;

        world.Replace(force with { IsDormant = false });
        world.Replace(city with { IsDormant = false, Soldiers = d.CitySoldiers, OwnerId = force.Id });
        foreach (int id in d.AttachedSettlementIds)
            world.Replace(world.AllSettlements[id] with { IsDormant = false, OwnerId = force.Id, CorpsId = city.CorpsId });
        foreach (int id in d.OfficerIds)
            world.Replace(world.AllOfficers[id] with { IsDormant = false });

        var simulation = world.Simulation;
        var controls = simulation.Controls.Add(new($"ai:{force.Id}", force.Id, ControllerKind.Automatic))
            .OrderBy(c => c.Kind == ControllerKind.Player ? 0 : 1).ThenBy(c => c.ForceId).ToImmutableArray();
        ControlRotation.Replace(world, controls);
        world.Replace(searching with { BaseTrust = (int)Math.Min(1000L, (long)searching.BaseTrust + d.BaseTrustReward) });
        world.ConsumeEvent(d.Candidate(d.ConsumptionKey), new(d.Id, EventOutcomeKind.Completed, false, EventResult.Succeeded));
        events.Add(new ForceActivatedBySearchEvent(world.Clock, d.Id, task.ForceId, task.OfficerId, force.Id, city.Id,
            d.AttachedSettlementIds.Prepend(city.Id).Order().ToImmutableArray(), d.OfficerIds.Order().ToImmutableArray()));
        return true;
    }
}
