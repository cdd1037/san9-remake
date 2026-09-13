using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class PersonnelReturns
{
    internal const string Kind = "personnel-return";

    internal static void Repatriate(WorldMutation world, int personId, int origin, PersistentTask cause,
        PersonnelRulebook places, List<DomainEvent> events)
    {
        var person = world.Officers[personId];
        var journey = world.Simulation.Tasks.FirstOrDefault(t => t.OfficerId == personId && t.Journey is not null);
        PersonnelTransitions.CancelMissions(world, personId, "service-place-lost", events, places, true);
        if (journey is not null)
        {
            Start(world, journey, places, events, true);
            return;
        }
        var home = world.Settlements.Values.Where(s => s.OwnerId == person.OwnerId && s.CanHostPersons && s.Durability > 0 &&
                places.Places.ContainsKey(s.Id)).OrderBy(s => places.JourneyDays(origin, s.Id, PersonnelJourneyKind.Move)).ThenBy(s => s.Id).FirstOrDefault();
        if (home is null) { PersonnelTransitions.DiscoverDetached(world, personId, origin, events); return; }
        var previous = cause with { ForceId = person.OwnerId!.Value, ControllerId = "system" };
        Schedule(world, previous, personId, origin, home.Id, places.JourneyDays(origin, home.Id, PersonnelJourneyKind.Move), true, events);
    }

    internal static void Start(WorldMutation world, PersistentTask previous, PersonnelRulebook places,
        List<DomainEvent> events, bool afterDailyCommands, int? companionId = null)
    {
        int officerId = companionId ?? previous.OfficerId;
        var person = world.Officers[officerId];
        if (!person.IsServing || person.OwnerId != previous.ForceId) return;
        int from = previous.Journey!.DestinationPlaceId;
        bool ValidHome(SettlementState home) => home.OwnerId == person.OwnerId && home.CanHostPersons &&
            home.Durability > 0 && places.Places.ContainsKey(home.Id);
        var home = person.SettlementId is int id && world.Settlements.TryGetValue(id, out var current) && ValidHome(current)
            ? current : world.Settlements.Values.Where(ValidHome)
                .OrderBy(s => places.JourneyDays(from, s.Id, PersonnelJourneyKind.Move)).ThenBy(s => s.Id).FirstOrDefault();
        if (home is null)
        {
            // No friendly hosting object remains. Preserve a single geographic location, not an invalid service attachment.
            PersonnelTransitions.DiscoverDetached(world, person.Id, from, events);
            return;
        }
        int days = places.JourneyDays(from, home.Id, PersonnelJourneyKind.Move);
        if (companionId is null)
        {
            int remaining = Math.Max(0, (previous.Journey.ReturnLeg?.CountdownDays ?? places.JourneyDays(previous.OriginSettlementId, from,
                PersonnelMissions.JourneyKind(previous.Kind))) -
                previous.ProgressDays - (afterDailyCommands ? 1 : 0));
            if (previous.Kind == PersonnelMissions.SearchKind)
            {
                int rounded = Math.Max(10, (days + 9) / 10 * 10);
                if (rounded >= remaining) days = Math.Max(0, rounded - Math.Max(remaining, rounded - days));
            }
            else if (days >= remaining) days -= remaining;
        }
        Schedule(world, previous, person.Id, from, home.Id, days, afterDailyCommands, events);
    }

    internal static void Schedule(WorldMutation world, PersistentTask previous, int officerId, int from, int destination,
        int days, bool afterDailyCommands, List<DomainEvent> events, WorldPosition? originPosition = null, int? corpsId = null)
    {
        var due = world.Clock;
        int offset = afterDailyCommands ? days : Math.Max(0, days - 1);
        for (int day = 0; day < offset; day++) due = due.NextDay();
        Guid id = new(Convert.FromHexString(CanonicalJson.Hash(new { previous.Id, OfficerId = officerId,
            ReturnStarted = world.Clock, Destination = destination })).AsSpan(0, 16));
        var task = new PersistentTask(id, Kind, previous.ControllerId, previous.ForceId, officerId, destination,
            world.Clock, due, 0, previous.Sequence)
        {
            Journey = new(destination, null, null) { ReturnLeg = new(from, days, afterDailyCommands) { OriginPosition = originPosition, CorpsId = corpsId } }
        };
        world.Replace(world.Officers[officerId] with { SettlementId = destination, RegionId = null, Acted = true, IsCommander = false });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
    }
}

internal sealed class PersonnelReturnRule(PersonnelRulebook places, GovernanceRulebook governance, MilitaryRulebook? military) : TaskRule
{
    internal override bool HasValidPayload(PersistentTask task) => task.Journey is
        { TargetOfficerId: null, SuccessRoll: null, AcceptRecruitment: null, ReturnLeg: { } leg } journey &&
        journey.DestinationPlaceId == task.OriginSettlementId && leg.CountdownDays >= 0 && leg.CorpsId is not < 0 &&
        (leg.OriginPosition is { X: >= 0, Y: >= 0 } ? military is not null : leg.OriginPosition is null &&
            places.Places.ContainsKey(leg.OriginPlaceId) && places.Places.ContainsKey(journey.DestinationPlaceId) &&
            leg.CountdownDays <= places.JourneyDays(leg.OriginPlaceId, journey.DestinationPlaceId, PersonnelJourneyKind.Move));
    internal override int DurationDays(PersistentTask task) => task.Journey?.ReturnLeg is { } leg
        ? leg.AfterDailyCommands ? leg.CountdownDays : Math.Max(0, leg.CountdownDays - 1) : -1;
    internal override bool Validate(WorldMutation world, PersistentTask task) => HasValidPayload(task) &&
        (task.Journey!.ReturnLeg!.OriginPosition is not { } origin ||
            military!.TryPlace(world.Settlements[task.OriginSettlementId], out var home) &&
            task.Journey.ReturnLeg.CountdownDays <= BattleRetreat.Days(origin, home.Position));
    internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events)
    {
        GovernanceMaintenance.RepairCommander(world, task.OriginSettlementId, governance, events);
        events.Add(new PersonnelReturnedEvent(world.Clock, task.OfficerId, task.OriginSettlementId));
    }
}

public sealed record PersonnelReturnedEvent(GameClock Clock, int OfficerId, int DestinationId) : DomainEvent;
