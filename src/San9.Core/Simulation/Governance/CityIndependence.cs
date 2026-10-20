using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record CityIndependenceEvent(GameClock Clock, int SettlementId, int PreviousForceId, int ForceId,
    int FounderId, ImmutableArray<int> MemberIds) : DomainEvent;
public sealed record OfficerDepartedEvent(GameClock Clock, int OfficerId, int PreviousForceId, int OriginId,
    int DestinationId, bool ServicePlaceLost) : DomainEvent;

internal static class CityIndependence
{
    private const string Reason = "city-independence";

    internal static void Apply(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (!world.Simulation.CityIndependenceEnabled || world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30) return;
        foreach (int id in world.Settlements.Keys.Order().ToArray())
        {
            var place = world.Settlements[id];
            if (place.IsDestroyed || !place.CanHostPersons || place.Durability <= 0 ||
                !rules.Personnel.Places.ContainsKey(id) || place.OwnerId is not int owner ||
                world.Forces[owner].IsBarbarian || world.Forces[owner].IsEliminated) continue;
            var traveling = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
            var present = world.Officers.Values.Where(p => p.OwnerId == owner && p.IsServing && p.SettlementId == id &&
                !traveling.Contains(p.Id)).OrderBy(p => p.Id).ToArray();
            var commander = present.FirstOrDefault(p => p.IsCommander);
            var marked = new HashSet<int>();
            foreach (var person in present.Where(p => p.Status != OfficerStatus.Ruler && p.Id != world.Forces[owner].LeaderId))
            {
                int threshold = 2 * (person.Ambition - person.Righteousness) - person.Loyalty + 69;
                if ((int)(world.NextRandom("event-city-independence") % 100) < threshold) marked.Add(person.Id);
            }
            if (marked.Count == 0) continue;
            bool changed = false;
            if (place.Kind == SettlementKind.City && commander is not null && marked.Contains(commander.Id))
            {
                foreach (var prisoner in world.Officers.Values.Where(p => p.Status == OfficerStatus.Captive && p.SettlementId == id)
                    .OrderBy(p => p.Id).ToArray())
                {
                    if (PrisonerRelease.Depart(world, prisoner.Id, id, world.Simulation.NextSequence,
                        PrisonerDepartureKind.Displaced, null, rules.Personnel, rules.Economy, events, excludedHomeId: id) == CommandRejection.None)
                        AdvanceSequence(world);
                }
                foreach (var person in present.Where(p => !marked.Contains(p.Id)))
                    ReturnOrDetach(world, person.Id, place, rules, events);
                int force = ForceCreation.Found(world, id, commander, present.Where(p => marked.Contains(p.Id)).ToArray(),
                    ForceCreationKind.CityIndependence, rules, events);
                SettlementTasks.LocationChanged(world, id, Reason, rules, events);
                events.Add(new CityIndependenceEvent(world.Clock, id, owner, force, commander.Id, marked.Order().ToImmutableArray()));
                changed = true;
            }
            else
                foreach (int person in marked.Order()) changed |= Detach(world, person, id, rules.Personnel, events, false);
            if (!changed) continue;
            GovernanceLifecycle.Reconcile(world, new HashSet<int> { owner }, rules, events, false, Reason);
            CorpsDelegations.Cleanup(world, rules.Military, events);
            world.MaintainLeadership(events);
            ResolutionPipeline.CheckConquest(world, events);
            if (world.Simulation.TerminalReason is not null) return;
        }
    }

    private static void ReturnOrDetach(WorldMutation world, int personId, SettlementState previous,
        SimulationRules rules, List<DomainEvent> events)
    {
        var home = world.Settlements.Values.Where(s => s.Id != previous.Id && s.OwnerId == previous.OwnerId &&
            s.CorpsId == previous.CorpsId && s.CanHostPersons && !s.IsDestroyed && s.Durability > 0 &&
            rules.Personnel.Places.ContainsKey(s.Id))
            .OrderBy(s => MapCoordinates.Distance(PersonnelLocations.Position(world, rules.Personnel, previous.Id),
                PersonnelLocations.Position(world, rules.Personnel, s.Id))).ThenBy(s => s.Id).FirstOrDefault();
        if (home is null) { Detach(world, personId, previous.Id, rules.Personnel, events, true); return; }
        PersonnelTransitions.CancelMissions(world, personId, Reason, events, rules.Personnel, false);
        var route = PersonnelLocations.Route(world, rules.Personnel, previous.Id, home.Id);
        var cause = ReturnContext(world, previous.OwnerId!.Value);
        world.Replace(world.Officers[personId] with { TacticCooldown = 0 });
        PersonnelReturns.Schedule(world, cause, personId, previous.Id, home.Id, route.Days, false, events, route);
        AdvanceSequence(world);
    }

    private static bool Detach(WorldMutation world, int personId, int origin, PersonnelRulebook places,
        List<DomainEvent> events, bool servicePlaceLost)
    {
        var destinations = PersonnelRelocation.Destinations(world, origin, places);
        if (destinations.Length == 0 && !servicePlaceLost) return false;
        int destination = destinations.Length == 0 ? origin :
            destinations[world.NextRandom("event-city-independence-placement") % (ulong)destinations.Length];
        var person = world.Officers[personId];
        int previous = person.OwnerId!.Value;
        PersonnelTransitions.CancelMissions(world, personId, Reason, events, places, false);
        RecruitmentRules.SetLoyalty(world, personId, previous, Math.Min(person.Loyalty, 45), Reason, events);
        PersonnelTransitions.DiscoverDetached(world, personId, destination, events);
        world.Replace(world.Officers[personId] with { TacticCooldown = 0 });
        events.Add(new OfficerDepartedEvent(world.Clock, personId, previous, origin, destination, servicePlaceLost));
        return true;
    }

    private static PersonnelReturnContext ReturnContext(WorldMutation world, int owner) =>
        new(world.Simulation.Controls.FirstOrDefault(c => c.ForceId == owner)?.ControllerId ?? "system",
            owner, world.Simulation.NextSequence);

    private static void AdvanceSequence(WorldMutation world) =>
        world.SetSimulation(world.Simulation with { NextSequence = checked(world.Simulation.NextSequence + 1) });
}
