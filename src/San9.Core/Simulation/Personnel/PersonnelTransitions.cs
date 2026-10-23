using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Owns personnel changes and the cancellation of missions that refer to the changed person.</summary>
internal static class PersonnelTransitions
{
    internal static OfficerState BeginStatus(OfficerState person, OfficerStatus status, int year) => person with
    {
        Status = status, StatusStartYear = year, OfficeId = null, TacticCooldown = 0,
        IsCommander = false, Acted = true
    };

    internal static void RelocateAllegiance(WorldMutation world, int personId, int recipient, int loyalty,
        int? settlementId, int? unitId, SimulationRules rules, List<DomainEvent> events, string reason, bool afterDailyCommands)
    {
        if ((settlementId is null) == (unitId is null)) throw new ArgumentException("Expected one personnel destination.");
        var person = world.Officers[personId];
        int geographicPlace;
        if (settlementId is int destination)
        {
            var place = world.Settlements[destination];
            if (place.OwnerId != recipient || place.IsDestroyed || !place.CanHostPersons || place.Durability <= 0)
                throw new ArgumentException("Invalid personnel destination.");
            geographicPlace = destination;
        }
        else
        {
            var unit = world.Units[unitId!.Value];
            if (unit.OwnerId != recipient) throw new ArgumentException("Invalid personnel destination.");
            geographicPlace = rules.Military?.RegionAt(unit.Position) ?? rules.Personnel.Places.Values
                .OrderBy(p => MapCoordinates.Distance(unit.Position, p.Position)).ThenBy(p => p.Id).First().Id;
        }
        int? previousCorps = PersonnelLocations.Corps(world, person);
        TransferAllegiance(world, personId, recipient, loyalty, geographicPlace, reason, rules.Personnel, events, afterDailyCommands);
        world.Replace(world.Officers[personId] with { SettlementId = settlementId, UnitId = unitId, RegionId = null });
        if (person.UnitId is int previousUnit && world.Units.ContainsKey(previousUnit) &&
            !world.Officers.Values.Any(p => p.UnitId == previousUnit && p.IsServing))
            BattleAftermath.TerminateUnit(world, previousUnit, null, rules, events);
        if (person.Status == OfficerStatus.Governor && previousCorps is int corpsId &&
            world.Corps.TryGetValue(corpsId, out var corps) && !corps.IsRoot)
            GovernanceMaintenance.DissolveForced(world, corpsId, reason, events);
        if (person.OwnerId is int previousForce) GovernanceLifecycle.Reconcile(world, new HashSet<int> { previousForce }, rules, events, afterDailyCommands, reason);
        world.MaintainLeadership(events);
        ResolutionPipeline.CheckConquest(world, events);
    }

    internal static void TransferAllegiance(WorldMutation world, int personId, int recipient, int loyalty, int place,
        string reason, PersonnelRulebook places, List<DomainEvent> events, bool afterDailyCommands = true)
    {
        var person = world.Officers[personId];
        CancelMissions(world, personId, reason, events, places, afterDailyCommands);
        if (person.OwnerId is int previous && world.Forces[previous].AdviserId == personId)
            world.Replace(world.Forces[previous] with { AdviserId = null });
        RecruitmentRules.SetLoyalty(world, personId, recipient, loyalty, reason, events);
        world.Replace(BeginStatus(world.Officers[personId], person.IsServing ? OfficerStatus.Serving : person.Status,
            world.Clock.Year) with { OwnerId = recipient });
        events.Add(new PersonAffiliationChangedEvent(world.Clock, personId, person.OwnerId, recipient, place));
    }

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
        CancelMissions(world, personId, "person-captured", events, places, world.CurrentDateCommandsConsumed);
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
        world.Replace(BeginStatus(person, OfficerStatus.Free, world.Clock.Year) with { OwnerId = null, UnitId = null,
            SettlementId = world.Settlements.ContainsKey(placeId) ? placeId : null,
            RegionId = world.Settlements.ContainsKey(placeId) ? null : placeId });
        events.Add(new PersonAffiliationChangedEvent(world.Clock, personId, person.OwnerId, null, placeId));
    }
    internal static void Exile(WorldMutation world, int personId, int placeId, List<DomainEvent> events, PersonnelRulebook places, bool afterDailyCommands)
    {
        var person = world.Officers[personId];
        int forceId = person.OwnerId!.Value;
        CancelMissions(world, personId, "person-exiled", events, places, afterDailyCommands);
        RecruitmentRules.SetLoyalty(world, personId, forceId, 45, "exiled", events);
        DiscoverDetached(world, personId, placeId, events);
    }

    internal static void Discover(WorldMutation world, int personId, int placeId, List<DomainEvent> events, PersonnelRulebook places, bool afterDailyCommands)
    {
        var person = world.Officers[personId];
        if (person.Status != OfficerStatus.Undiscovered) throw new InvalidOperationException("Person was already discovered.");
        CancelMissions(world, personId, "person-discovered", events, places, afterDailyCommands);
        DiscoverDetached(world, personId, placeId, events);
    }

    internal static void Recruit(WorldMutation world, int personId, int forceId, int destinationId, List<DomainEvent> events, PersonnelRulebook places, bool afterDailyCommands, int? recruitmentLoyalty = null)
    {
        var person = world.Officers[personId];
        var destination = world.Settlements[destinationId];
        if (destination.OwnerId != forceId || !destination.CanHostPersons) throw new InvalidOperationException("Invalid recruitment destination.");
        CancelMissions(world, personId, "person-affiliation-changed", events, places, afterDailyCommands);
        if (person.OwnerId is int previous && world.Forces[previous].AdviserId == personId)
            world.Replace(world.Forces[previous] with { AdviserId = null });
        if (person.OwnerId is int oldForce) RecruitmentRules.SetLoyalty(world, personId, oldForce, 45, "recruited-away", events);
        int loyalty = recruitmentLoyalty ?? 75 + (50 - PersonnelRulebook.AffinityDistance(person, world.Officers[world.Forces[forceId].LeaderId!.Value])) / 2;
        RecruitmentRules.SetLoyalty(world, personId, forceId, loyalty, "recruited", events);
        world.Replace(BeginStatus(world.Officers[personId], OfficerStatus.Serving, world.Clock.Year) with
        {
            OwnerId = forceId, SettlementId = destinationId, RegionId = null, UnitId = null
        });
        events.Add(new PersonAffiliationChangedEvent(world.Clock, personId, person.OwnerId, forceId, destinationId));
    }

    internal static void Move(WorldMutation world, PersistentTask task, List<DomainEvent> events, PersonnelRulebook places, bool afterDailyCommands)
    {
        int personId = task.OfficerId, destinationId = task.Journey!.DestinationPlaceId;
        var person = world.Officers[personId];
        var destination = world.Settlements[destinationId];
        if (destination.OwnerId != person.OwnerId || !destination.CanHostPersons) throw new InvalidOperationException("Invalid move destination.");
        CancelMissions(world, personId, "person-location-changed", events, places, afterDailyCommands);
        world.Replace(person with { SettlementId = destinationId, RegionId = null, Acted = true, IsCommander = false });
        events.Add(new OfficerMovedEvent(world.Clock, personId, person.SettlementId!.Value, destinationId,
            new(task.ForceId, world.Forces[task.ForceId].LeaderId, task.Sequence, true, PersonnelLocations.Corps(world, person))));
    }

    internal static void CancelMissions(WorldMutation world, int personId, string reason, List<DomainEvent> events, PersonnelRulebook places, bool afterDailyCommands,
        bool preserveReinforcements = false)
    {
        foreach (var task in world.Simulation.Tasks.Where(t => t.OfficerId == personId || t.Journey?.TargetOfficerId == personId ||
            t.PrisonerReturn?.PrisonerId == personId || t.PrisonerReturn?.Offer?.PrisonerId == personId || t.AttackRequest?.Offer?.PrisonerId == personId)
            .OrderBy(t => t.Sequence).ThenBy(t => t.OriginSettlementId).ThenBy(t => t.OfficerId).ThenBy(t => t.Id).ToArray())
        {
            world.Replace(world.Officers[task.OfficerId] with { Acted = true });
            TaskCancellation.Apply(world, task, reason, events,
                preserveCargo: preserveReinforcements || task.OfficerId != personId && task.Journey is not null);
            if (task.OfficerId != personId && task.Journey is not null)
                PersonnelReturns.Start(world, task, places, events, afterDailyCommands, interrupted: true);
            else if (!preserveReinforcements)
                ReinforcementTransport.Disband(world, task, reason, events);
        }
    }
}
