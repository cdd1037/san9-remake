using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Owns personnel changes and the cancellation of missions that refer to the changed person.</summary>
internal static class PersonnelTransitions
{
    internal static void Capture(WorldMutation world, int personId, int? settlementId, int? unitId,
        PersonnelRulebook places, List<DomainEvent> events)
    {
        var person = world.Officers[personId];
        if (!person.IsServing || person.OwnerId is null || (settlementId is null) == (unitId is null))
            throw new ArgumentException("Invalid capture participant or detention location.");
        int? captor = settlementId is int home
            ? world.Settlements.TryGetValue(home, out var settlement) && settlement.CanHostPersons && settlement.Durability > 0
                ? settlement.OwnerId : null
            : world.Units.TryGetValue(unitId!.Value, out var unit) ? unit.OwnerId : null;
        if (captor is null || captor == person.OwnerId) throw new ArgumentException("Invalid captor.");
        CancelMissions(world, personId, "person-captured", events, places, true);
        var force = world.Forces[person.OwnerId.Value];
        if (force.AdviserId == personId) world.Replace(force with { AdviserId = null });
        world.Replace(world.Officers[personId] with
        {
            Status = OfficerStatus.Captive, SettlementId = settlementId, UnitId = unitId, RegionId = null,
            OfficeId = null, IsCommander = false, Acted = true, StatusStartYear = world.Clock.Year
        });
        events.Add(new OfficerCapturedEvent(world.Clock, personId, captor.Value, settlementId, unitId));
    }

    internal static void DiscoverDetached(WorldMutation world, int personId, int placeId, List<DomainEvent> events)
    {
        var person = world.Officers[personId];
        if (person.OwnerId is int owner && world.Forces[owner].AdviserId == personId)
            world.Replace(world.Forces[owner] with { AdviserId = null });
        world.Replace(person with { OwnerId = null, Status = OfficerStatus.Free, UnitId = null,
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
            OwnerId = null, Status = OfficerStatus.Free, StatusStartYear = world.Clock.Year, UnitId = null,
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
            OwnerId = null, Status = OfficerStatus.Free, UnitId = null, SettlementId = world.Settlements.ContainsKey(placeId) ? placeId : null,
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
            OwnerId = forceId, SettlementId = destinationId, RegionId = null, UnitId = null, Status = OfficerStatus.Serving,
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

    internal static void CancelMissions(WorldMutation world, int personId, string reason, List<DomainEvent> events, PersonnelRulebook places, bool afterDailyCommands,
        bool preserveReinforcements = false)
    {
        foreach (var task in world.Simulation.Tasks.Where(t => t.OfficerId == personId || t.Journey?.TargetOfficerId == personId ||
            t.PrisonerReturn?.PrisonerId == personId || t.PrisonerReturn?.Offer?.PrisonerId == personId || t.AttackRequest?.Offer?.PrisonerId == personId)
            .OrderBy(t => t.Sequence).ThenBy(t => t.OriginSettlementId).ThenBy(t => t.OfficerId).ThenBy(t => t.Id).ToArray())
        {
            world.Replace(world.Officers[task.OfficerId] with { Acted = true });
            TaskCancellation.Apply(world, task, reason, events);
            if (task.OfficerId != personId && task.Journey is not null)
                PersonnelReturns.Start(world, task, places, events, afterDailyCommands);
            else if (!preserveReinforcements)
                ReinforcementTransport.Disband(world, task, reason, events);
        }
    }
}
