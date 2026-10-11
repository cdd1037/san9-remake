using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class OfficeRequests
{
    internal static void Begin(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (!world.Simulation.OfficeRequestsEnabled || world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30) return;
        Continue(world, rules, world.Officers.Keys.Order().ToImmutableArray(), events);
    }

    private static bool Eligible(WorldMutation world, int id, out int forceId, out int placeId)
    {
        forceId = placeId = -1;
        if (!world.Officers.TryGetValue(id, out var person) || person.Status != OfficerStatus.Free ||
            person.SettlementId is not int home || !world.Settlements.TryGetValue(home, out var place) ||
            !place.CanHostPersons || place.IsDestroyed || place.Durability <= 0 || place.Facility?.BuilderUnitId is not null ||
            place.OwnerId is not int owner || !world.Forces.TryGetValue(owner, out var force) ||
            force.IsEliminated || force.IsBarbarian || force.LeaderId is not int ruler ||
            !world.Officers.TryGetValue(ruler, out var leader) || !leader.IsServing || person.LoyaltyTo(owner) < 50) return false;
        forceId = owner;
        placeId = home;
        return true;
    }

    private static void Continue(WorldMutation world, SimulationRules rules, ImmutableArray<int> queue, List<DomainEvent> events)
    {
        for (int index = 0; index < queue.Length; index++)
        {
            int id = queue[index];
            if (!Eligible(world, id, out int owner, out int home)) continue;
            var person = world.Officers[id];
            var force = world.Forces[owner];
            int distance = PersonnelRulebook.AffinityDistance(person, world.Officers[force.LeaderId!.Value]);
            int threshold = person.LoyaltyTo(owner) + force.Trust / 25 - 2 * distance - 20;
            if ((int)(world.NextRandom("event-office-request") % 100) >= threshold) continue;
            var session = new OfficeRequestEventSession(id, owner, home, queue.Skip(index + 1).ToImmutableArray());
            events.Add(new OfficeRequestOfferedEvent(world.Clock, id, owner, home));
            world.SetSimulation(world.Simulation with { PendingEvent = session, Boundary = SimulationBoundary.AwaitingDecision });
            return;
        }
    }

    internal static CommandRejection Respond(WorldMutation world, SimulationRules rules, RespondOfficeRequestCommand command,
        List<DomainEvent> events)
    {
        if (world.Simulation.PendingEvent is not OfficeRequestEventSession session || session.OfficerId != command.OfficerId ||
            session.ForceId != command.IssuerId || !Enum.IsDefined(command.Choice)) return CommandRejection.InvalidDecision;
        world.SetSimulation(world.Simulation with { PendingEvent = null, Boundary = SimulationBoundary.Stable });
        Commit(world, rules, session, command.Choice, events);
        if (ResolutionPipeline.CheckTerminal(world, events))
            Continue(world, rules, session.RemainingOfficerIds, events);
        return CommandRejection.None;
    }

    private static void Commit(WorldMutation world, SimulationRules rules, OfficeRequestEventSession session,
        EventChoice choice, List<DomainEvent> events)
    {
        bool valid = Eligible(world, session.OfficerId, out int owner, out int home) && owner == session.ForceId && home == session.SettlementId;
        if (valid && choice == EventChoice.Accept)
            PersonnelTransitions.Recruit(world, session.OfficerId, owner, home, events, rules.Personnel, false);
        else if (valid && choice == EventChoice.Reject)
        {
            int destination = home;
            var neighbors = (rules.Military?.TerritoryConnections(world.Settlements.Values) ?? [])
                .Where(c => c.FromRegionId == home && rules.Personnel.Places.ContainsKey(c.ToRegionId) &&
                    (!world.Settlements.TryGetValue(c.ToRegionId, out var place) ||
                        place.CanHostPersons && !place.IsDestroyed && place.Durability > 0 && place.Facility?.BuilderUnitId is null))
                .Select(c => c.ToRegionId).Distinct().Order().ToArray();
            if (neighbors.Length > 0)
                destination = neighbors[world.NextRandom("event-office-request-placement") % (ulong)neighbors.Length];
            PersonnelTransitions.CancelMissions(world, session.OfficerId, "office-request-rejected", events, rules.Personnel, false);
            RecruitmentRules.SetLoyalty(world, session.OfficerId, owner, 45, "office-request-rejected", events);
            var person = world.Officers[session.OfficerId];
            world.Replace(person with { SettlementId = world.Settlements.ContainsKey(destination) ? destination : null,
                RegionId = world.Settlements.ContainsKey(destination) ? null : destination,
                StatusStartYear = world.Clock.Year, Acted = true });
            if (destination != home) events.Add(new FreeOfficerMigratedEvent(world.Clock, person.Id, home, destination));
        }
        events.Add(new OfficeRequestResolvedEvent(world.Clock, session.OfficerId, session.ForceId, choice, !valid));
    }

    internal static void ValidatePending(WorldMutation world)
    {
        if (world.Simulation.PendingEvent is not OfficeRequestEventSession session) return;
        if (!world.Simulation.OfficeRequestsEnabled || world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30 ||
            world.Simulation.ActiveControl != world.Simulation.Controls.Length ||
            !world.Simulation.Controls.Any(c => c.ForceId == session.ForceId) ||
            session.RemainingOfficerIds.IsDefault || !session.RemainingOfficerIds.SequenceEqual(session.RemainingOfficerIds.Distinct().Order()) ||
            session.RemainingOfficerIds.Any(id => id <= session.OfficerId || !world.Officers.ContainsKey(id)) ||
            !Eligible(world, session.OfficerId, out int owner, out int home) || owner != session.ForceId || home != session.SettlementId)
            throw new ArgumentException("Invalid office request event session.");
    }
}
