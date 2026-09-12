using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Owns personnel changes and the cancellation of missions that refer to the changed person.</summary>
internal static class PersonnelTransitions
{
    internal static void DiscoverDetached(WorldMutation world, int personId, int placeId, List<DomainEvent> events)
    {
        var person = world.Officers[personId];
        if (person.OwnerId is int owner && world.Forces[owner].AdviserId == personId)
            world.Replace(world.Forces[owner] with { AdviserId = null });
        world.Replace(person with { OwnerId = null, Status = OfficerStatus.Free,
            SettlementId = world.Settlements.ContainsKey(placeId) ? placeId : null,
            RegionId = world.Settlements.ContainsKey(placeId) ? null : placeId, OfficeId = null,
            IsCommander = false, Acted = true, StatusStartYear = world.Clock.Year });
        events.Add(new PersonAffiliationChangedEvent(world.Clock, personId, person.OwnerId, null, placeId));
    }
    internal static void Exile(WorldMutation world, int personId, int placeId, List<DomainEvent> events, PersonnelRulebook places, bool afterDailyCommands)
    {
        var person = world.Officers[personId];
        int forceId = person.OwnerId!.Value;
        CancelMissions(world, personId, "person-exiled", events, places, afterDailyCommands);
        if (world.Forces[forceId].AdviserId == personId)
            world.Replace(world.Forces[forceId] with { AdviserId = null });
        RecruitmentRules.SetLoyalty(world, personId, forceId, 45, "exiled", events);
        world.Replace(world.Officers[personId] with
        {
            OwnerId = null, Status = OfficerStatus.Free, StatusStartYear = world.Clock.Year,
            SettlementId = world.Settlements.ContainsKey(placeId) ? placeId : null,
            RegionId = world.Settlements.ContainsKey(placeId) ? null : placeId,
            OfficeId = null, IsCommander = false, Acted = true
        });
        events.Add(new PersonAffiliationChangedEvent(world.Clock, personId, forceId, null, placeId));
    }

    internal static void Discover(WorldMutation world, int personId, int placeId, List<DomainEvent> events, PersonnelRulebook places, bool afterDailyCommands)
    {
        var person = world.Officers[personId];
        if (person.Status != OfficerStatus.Undiscovered) throw new InvalidOperationException("Person was already discovered.");
        CancelMissions(world, personId, "person-discovered", events, places, afterDailyCommands);
        world.Replace(person with
        {
            OwnerId = null, Status = OfficerStatus.Free, SettlementId = world.Settlements.ContainsKey(placeId) ? placeId : null,
            RegionId = world.Settlements.ContainsKey(placeId) ? null : placeId, StatusStartYear = world.Clock.Year,
            OfficeId = null, IsCommander = false, Acted = true
        });
        events.Add(new PersonAffiliationChangedEvent(world.Clock, personId, person.OwnerId, null, placeId));
    }

    internal static void Recruit(WorldMutation world, int personId, int forceId, int destinationId, List<DomainEvent> events, PersonnelRulebook places, bool afterDailyCommands)
    {
        var person = world.Officers[personId];
        var destination = world.Settlements[destinationId];
        if (destination.OwnerId != forceId || !destination.CanHostPersons) throw new InvalidOperationException("Invalid recruitment destination.");
        CancelMissions(world, personId, "person-affiliation-changed", events, places, afterDailyCommands);
        if (person.OwnerId is int previous && world.Forces[previous].AdviserId == personId)
            world.Replace(world.Forces[previous] with { AdviserId = null });
        if (person.OwnerId is int oldForce) RecruitmentRules.SetLoyalty(world, personId, oldForce, 45, "recruited-away", events);
        int loyalty = 75 + (50 - PersonnelRulebook.AffinityDistance(person, world.Officers[world.Forces[forceId].LeaderId!.Value])) / 2;
        RecruitmentRules.SetLoyalty(world, personId, forceId, loyalty, "recruited", events);
        world.Replace(world.Officers[personId] with
        {
            OwnerId = forceId, SettlementId = destinationId, RegionId = null, Status = OfficerStatus.Serving,
            StatusStartYear = world.Clock.Year, OfficeId = null, IsCommander = false, Acted = true
        });
        events.Add(new PersonAffiliationChangedEvent(world.Clock, personId, person.OwnerId, forceId, destinationId));
    }

    internal static void Move(WorldMutation world, int personId, int destinationId, List<DomainEvent> events, PersonnelRulebook places, bool afterDailyCommands)
    {
        var person = world.Officers[personId];
        var destination = world.Settlements[destinationId];
        if (destination.OwnerId != person.OwnerId || !destination.CanHostPersons) throw new InvalidOperationException("Invalid move destination.");
        CancelMissions(world, personId, "person-location-changed", events, places, afterDailyCommands);
        world.Replace(person with { SettlementId = destinationId, RegionId = null, Acted = true, IsCommander = false });
        events.Add(new OfficerMovedEvent(world.Clock, personId, person.SettlementId!.Value, destinationId));
    }

    internal static void CancelMissions(WorldMutation world, int personId, string reason, List<DomainEvent> events, PersonnelRulebook places, bool afterDailyCommands)
    {
        foreach (var task in world.Simulation.Tasks.Where(t => t.OfficerId == personId || t.Journey?.TargetOfficerId == personId)
            .OrderBy(t => t.Sequence).ThenBy(t => t.OriginSettlementId).ThenBy(t => t.OfficerId).ThenBy(t => t.Id).ToArray())
        {
            world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Remove(task) });
            world.Replace(world.Officers[task.OfficerId] with { Acted = true });
            events.Add(new TaskCancelledEvent(task.Id, world.Clock, reason));
            if (task.OfficerId != personId && task.Journey is not null)
                PersonnelReturns.Start(world, task, places, events, afterDailyCommands);
        }
    }
}
