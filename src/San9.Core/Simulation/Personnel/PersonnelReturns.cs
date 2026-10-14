using San9.Core.Domain;

namespace San9.Core.Simulation;

internal sealed record PersonnelReturnContext(string ControllerId, int ForceId, long Sequence,
    bool DiplomaticEnvoy = false, ReinforcementTroops? Reinforcements = null)
{
    internal static PersonnelReturnContext FromTask(PersistentTask task, int officerId) => new(
        task.ControllerId, task.ForceId, task.Sequence, officerId == task.OfficerId &&
            (DiplomacyJourney.IsDiplomatic(task) || task.Journey?.ReturnLeg?.DiplomaticEnvoy == true), task.Reinforcements);
}

internal static class PersonnelReturns
{
    internal const string Kind = "personnel-return";

    internal static SettlementState TransportDestination(WorldMutation world, UnitState unit, SettlementState arrival,
        MilitaryRulebook military)
    {
        var person = world.UnitMembers(unit.Id).FirstOrDefault(p => p.IsServing);
        if (person is null) return arrival;
        return unit.ServicePlaceId is int id && world.Settlements.TryGetValue(id, out var source) &&
            source.OwnerId == unit.OwnerId && source.CanHostPersons && !source.IsDestroyed && source.Durability > 0 &&
            source.Facility?.BuilderUnitId is null && military.TryPlace(source, out _) ? source :
            BattleRetreat.Destination(world, person, unit.Position, null, military, preferredCorps: unit.CorpsId) ?? arrival;
    }

    internal static void FromTransport(WorldMutation world, int personId, UnitState unit, int arrivalId,
        SettlementState home, MilitaryRulebook military, List<DomainEvent> events)
    {
        long sequence = world.Simulation.NextSequence;
        var context = new PersonnelReturnContext("system", unit.OwnerId, sequence);
        world.SetSimulation(world.Simulation with { NextSequence = checked(sequence + 1) });
        var route = new PersonnelRoute(unit.Position, military.Place(home).Position, false);
        // Military map updates run after the date advances. The displayed date's
        // daily commands have not run yet, so they must consume the first return day.
        Schedule(world, context, personId, arrivalId, home.Id, route.Days, world.CurrentDateCommandsConsumed, events, route, true, home.CorpsId);
    }

    internal static void FromField(WorldMutation world, int personId, WorldPosition origin, SimulationRules rules,
        List<DomainEvent> events, bool afterDailyCommands = true)
    {
        var person = world.Officers[personId];
        var home = BattleRetreat.Destination(world, person, origin, null, rules.Military!);
        if (home is null)
        {
            BattlePersonnel.Displace(world, personId, origin, rules.Personnel, rules.Military!, events);
            return;
        }
        PersonnelTransitions.CancelMissions(world, personId, "unit-defected", events, rules.Personnel, afterDailyCommands);
        world.Replace(world.Officers[personId] with { UnitId = null, SettlementId = home.Id, RegionId = null, TacticCooldown = 0 });
        long sequence = world.Simulation.NextSequence;
        var context = new PersonnelReturnContext("system", person.OwnerId!.Value, sequence);
        world.SetSimulation(world.Simulation with { NextSequence = checked(sequence + 1) });
        Schedule(world, context, personId, home.Id, home.Id, BattleRetreat.Days(origin, rules.Military!.Place(home).Position),
            afterDailyCommands, events, new(origin, rules.Military.Place(home).Position, false), true, home.CorpsId);
    }

    internal static void Repatriate(WorldMutation world, int personId, int origin, PersonnelReturnContext context,
        PersonnelRulebook places, List<DomainEvent> events, bool afterDailyCommands = true)
    {
        var person = world.Officers[personId];
        var journey = world.TasksFor(personId).FirstOrDefault(t => t.Journey is not null);
        PersonnelTransitions.CancelMissions(world, personId, "service-place-lost", events, places, afterDailyCommands, preserveReinforcements: true);
        if (journey is not null)
        {
            Start(world, journey, places, events, afterDailyCommands);
            return;
        }
        var home = world.Settlements.Values.Where(s => s.OwnerId == person.OwnerId && s.CanHostPersons && s.Durability > 0 &&
                places.Places.ContainsKey(s.Id)).OrderBy(s => PersonnelLocations.Route(world, places, origin, s.Id).Days).ThenBy(s => s.Id).FirstOrDefault();
        if (home is null) { PersonnelTransitions.DiscoverDetached(world, personId, origin, events); return; }
        context = context with { ForceId = person.OwnerId!.Value, ControllerId = "system" };
        var route = PersonnelLocations.Route(world, places, origin, home.Id);
        Schedule(world, context, personId, origin, home.Id, route.Days, afterDailyCommands, events, route);
    }

    internal static void Start(WorldMutation world, PersistentTask previous, PersonnelRulebook places,
        List<DomainEvent> events, bool afterDailyCommands, int? companionId = null, bool arrived = false)
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
        // A completed visit to a moving target has no remaining outbound leg to deduct from the return.
        if (companionId is null && !arrived)
        {
            bool roundedOutbound = previous.Kind == PersonnelMissions.SearchKind || DiplomacyJourney.IsDiplomatic(previous);
            int outbound = previous.Journey.ReturnLeg?.CountdownDays ?? (roundedOutbound
                ? previous.Journey.Route.XunRoundedDays : previous.Journey.Route.Days);
            int remaining = Math.Max(0, outbound -
                PersonnelJourneyTiming.ElapsedDays(previous, world.Clock, afterDailyCommands));
            if (roundedOutbound)
            {
                int rounded = Math.Max(10, (days + 9) / 10 * 10);
                if (rounded >= remaining) days = Math.Max(0, rounded - Math.Max(remaining, rounded - days));
            }
            else if (days >= remaining) days -= remaining;
        }
        Schedule(world, PersonnelReturnContext.FromTask(previous, person.Id), person.Id, from, home.Id, days, afterDailyCommands, events,
            new(origin, PersonnelLocations.Position(world, places, home.Id), false),
            previous.Journey.Route.FieldDestination || previous.Journey.ReturnLeg?.FieldTravel == true);
    }

    internal static void Schedule(WorldMutation world, PersonnelReturnContext context, int officerId, int from, int destination,
        int days, bool afterDailyCommands, List<DomainEvent> events, PersonnelRoute route, bool fieldTravel = false, int? corpsId = null)
    {
        var due = world.Clock;
        int offset = afterDailyCommands ? days : Math.Max(0, days - 1);
        for (int day = 0; day < offset; day++) due = due.NextDay();
        Guid id = world.AllocateTaskId();
        var task = new PersistentTask(id, Kind, context.ControllerId, context.ForceId, officerId, destination,
            world.Clock, due, 0, context.Sequence)
        {
            Journey = new(destination, null, null)
            {
                Route = route,
                ReturnLeg = new(from, days, afterDailyCommands)
                {
                    FieldTravel = fieldTravel, CorpsId = corpsId,
                    DiplomaticEnvoy = context.DiplomaticEnvoy
                }
            },
            Reinforcements = context.Reinforcements
        };
        world.Replace(world.Officers[officerId] with { SettlementId = destination, RegionId = null, Acted = true, IsCommander = false });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
    }
}

internal sealed class PersonnelReturnRule(PersonnelRulebook places, MilitaryRulebook? military) : TaskRule
{
    internal override bool HasValidPayload(PersistentTask task) => task.Journey is
        { TargetOfficerId: null, SuccessRoll: null, Route: { FieldDestination: false } route,
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
