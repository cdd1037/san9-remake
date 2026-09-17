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
        PersonnelTransitions.CancelMissions(world, personId, "service-place-lost", events, places, true, preserveReinforcements: true);
        if (journey is not null)
        {
            Start(world, journey, places, events, true);
            return;
        }
        var home = world.Settlements.Values.Where(s => s.OwnerId == person.OwnerId && s.CanHostPersons && s.Durability > 0 &&
                places.Places.ContainsKey(s.Id)).OrderBy(s => PersonnelLocations.Route(world, places, origin, s.Id).Days).ThenBy(s => s.Id).FirstOrDefault();
        if (home is null) { PersonnelTransitions.DiscoverDetached(world, personId, origin, events); return; }
        var previous = cause with { ForceId = person.OwnerId!.Value, ControllerId = "system" };
        var route = PersonnelLocations.Route(world, places, origin, home.Id);
        Schedule(world, previous, personId, origin, home.Id, route.Days, true, events, route);
    }

    internal static void Start(WorldMutation world, PersistentTask previous, PersonnelRulebook places,
        List<DomainEvent> events, bool afterDailyCommands, int? companionId = null)
    {
        int officerId = companionId ?? previous.OfficerId;
        var person = world.Officers[officerId];
        if (!person.IsServing || person.OwnerId != previous.ForceId)
        {
            ReinforcementTransport.Disband(world, previous, "reinforcement-executor-lost", events);
            return;
        }
        int from = previous.Journey!.DestinationPlaceId;
        var origin = previous.Journey.Route.Destination;
        int DistanceTo(int destination) => PersonnelRulebook.JourneyDays(origin, PersonnelLocations.Position(world, places, destination));
        bool ValidHome(SettlementState home) => home.OwnerId == person.OwnerId && home.CanHostPersons &&
            home.Durability > 0 && places.Places.ContainsKey(home.Id);
        var home = person.SettlementId is int id && world.Settlements.TryGetValue(id, out var current) && ValidHome(current)
            ? current : world.Settlements.Values.Where(ValidHome)
                .OrderBy(s => DistanceTo(s.Id)).ThenBy(s => s.Id).FirstOrDefault();
        if (home is null)
        {
            ReinforcementTransport.Disband(world, previous, "reinforcement-no-home", events);
            // No friendly hosting object remains. Preserve a single geographic location, not an invalid service attachment.
            PersonnelTransitions.DiscoverDetached(world, person.Id, from, events);
            return;
        }
        int days = DistanceTo(home.Id);
        if (companionId is null)
        {
            bool roundedOutbound = previous.Kind == PersonnelMissions.SearchKind || DiplomacyJourney.IsDiplomatic(previous);
            int outbound = previous.Journey.ReturnLeg?.CountdownDays ?? (roundedOutbound
                ? previous.Journey.Route.XunRoundedDays : previous.Journey.Route.Days);
            int remaining = Math.Max(0, outbound -
                previous.ProgressDays - (afterDailyCommands ? 1 : 0));
            if (roundedOutbound)
            {
                int rounded = Math.Max(10, (days + 9) / 10 * 10);
                if (rounded >= remaining) days = Math.Max(0, rounded - Math.Max(remaining, rounded - days));
            }
            else if (days >= remaining) days -= remaining;
        }
        Schedule(world, previous, person.Id, from, home.Id, days, afterDailyCommands, events,
            new(origin, PersonnelLocations.Position(world, places, home.Id), false),
            previous.Journey.Route.FieldDestination || previous.Journey.ReturnLeg?.FieldTravel == true);
    }

    internal static void Schedule(WorldMutation world, PersistentTask previous, int officerId, int from, int destination,
        int days, bool afterDailyCommands, List<DomainEvent> events, PersonnelRoute route, bool fieldTravel = false, int? corpsId = null)
    {
        var due = world.Clock;
        int offset = afterDailyCommands ? days : Math.Max(0, days - 1);
        for (int day = 0; day < offset; day++) due = due.NextDay();
        Guid id = new(Convert.FromHexString(CanonicalJson.Hash(new { previous.Id, OfficerId = officerId,
            ReturnStarted = world.Clock, Destination = destination })).AsSpan(0, 16));
        var task = new PersistentTask(id, Kind, previous.ControllerId, previous.ForceId, officerId, destination,
            world.Clock, due, 0, previous.Sequence)
        {
            Journey = new(destination, null, null)
            {
                Route = route,
                ReturnLeg = new(from, days, afterDailyCommands)
                {
                    FieldTravel = fieldTravel, CorpsId = corpsId,
                    DiplomaticEnvoy = officerId == previous.OfficerId &&
                        (DiplomacyJourney.IsDiplomatic(previous) || previous.Journey?.ReturnLeg?.DiplomaticEnvoy == true)
                }
            },
            Reinforcements = previous.Reinforcements
        };
        world.Replace(world.Officers[officerId] with { SettlementId = destination, RegionId = null, Acted = true, IsCommander = false });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
    }
}

internal sealed class PersonnelReturnRule(PersonnelRulebook places, MilitaryRulebook? military) : TaskRule
{
    internal override bool HasValidPayload(PersistentTask task) => task.Journey is
        { TargetOfficerId: null, SuccessRoll: null, AcceptRecruitment: null, Route: { FieldDestination: false } route,
          UnitStrategy: null, Burn: null, Seize: null, Gossip: null, ReturnLeg: { } leg } journey &&
        journey.DestinationPlaceId == task.OriginSettlementId && leg.CountdownDays >= 0 && leg.CorpsId is not < 0 &&
        (leg.FieldTravel ? military is not null :
            places.Places.ContainsKey(leg.OriginPlaceId) && places.Places.ContainsKey(journey.DestinationPlaceId)) &&
        leg.CountdownDays <= (leg.FieldTravel ? BattleRetreat.Days(route.Origin, route.Destination) : route.Days);
    internal override int DurationDays(PersistentTask task) => task.Journey?.ReturnLeg is { } leg
        ? leg.AfterDailyCommands ? leg.CountdownDays : Math.Max(0, leg.CountdownDays - 1) : -1;
    internal override bool Validate(WorldMutation world, PersistentTask task) => HasValidPayload(task);
    internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events)
    {
        ReinforcementTransport.Arrive(world, task, places, events);
        events.Add(new PersonnelReturnedEvent(world.Clock, task.OfficerId, task.OriginSettlementId));
    }
}

public sealed record PersonnelReturnedEvent(GameClock Clock, int OfficerId, int DestinationId) : DomainEvent;
