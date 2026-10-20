using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ReleasePrisonerCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record PrisonerReleasedEvent(GameClock Clock, int CaptorId, int OfficerId, int? ForceId, int DestinationId) : DomainEvent;
public sealed record PrisonerRecoveredEvent(GameClock Clock, int OfficerId, int ForceId, int DestinationId) : DomainEvent;
public sealed record PrisonerDisplacedEvent(GameClock Clock, int OfficerId, int? ForceId, int OriginId, int DestinationId) : DomainEvent;
internal enum PrisonerDepartureKind { Released, Escaped, Recovered, Displaced }

internal static class PrisonerRelease
{
    internal static CommandRejection Apply(WorldMutation world, GameCommand command, int personId,
        PersonnelRulebook places, EconomyRulebook economy, List<DomainEvent> events)
    {
        if (!world.Officers.TryGetValue(personId, out var person)) return CommandRejection.UnknownPerson;
        if (person.Status != OfficerStatus.Captive || person.SettlementId is not int origin ||
            world.Settlements[origin].OwnerId != command.IssuerId || !places.Places.ContainsKey(origin))
            return CommandRejection.PersonUnavailable;
        return Depart(world, personId, origin, command.Sequence, PrisonerDepartureKind.Released,
            command.IssuerId, places, economy, events);
    }

    internal static CommandRejection Recover(WorldMutation world, int personId, int origin,
        long sequence, PersonnelRulebook places, EconomyRulebook economy, List<DomainEvent> events) =>
        Depart(world, personId, origin, sequence, PrisonerDepartureKind.Recovered, null,
            places, economy, events);

    internal static CommandRejection Depart(WorldMutation world, int personId, int origin, long sequence,
        PrisonerDepartureKind departure, int? releasingCaptor, PersonnelRulebook places, EconomyRulebook economy,
        List<DomainEvent> events, WorldPosition? originPosition = null, int? excludedHomeId = null)
    {
        var person = world.Officers[personId];
        var position = originPosition ?? PersonnelLocations.Position(world, places, origin);
        var home = person.OwnerId is int forceId ? world.Settlements.Values.Where(s => s.Id != excludedHomeId && s.OwnerId == forceId &&
            s.CanHostPersons && s.Durability > 0 && places.Places.ContainsKey(s.Id))
            .OrderBy(s => PersonnelRulebook.JourneyDays(position, PersonnelLocations.Position(world, places, s.Id)))
            .ThenBy(s => s.Id).FirstOrDefault() : null;
        if (home is null && world.Forces.Values.Any(f => f.LeaderId == personId)) return CommandRejection.NoDestination;
        var destinations = home is null ? PersonnelRelocation.Destinations(world, origin, places) : [];
        if (home is null && destinations.Length == 0) return CommandRejection.NoDestination;
        var control = home is null ? null : world.Simulation.Controls.FirstOrDefault(c => c.ForceId == person.OwnerId);
        string reason = departure switch
        {
            PrisonerDepartureKind.Released => "prisoner-released",
            PrisonerDepartureKind.Escaped => "prisoner-escaped",
            PrisonerDepartureKind.Recovered => "prisoner-recovered",
            PrisonerDepartureKind.Displaced => "prisoner-displaced",
            _ => throw new ArgumentOutOfRangeException(nameof(departure))
        };
        PersonnelTransitions.CancelMissions(world, personId, reason, events, places, world.CurrentDateCommandsConsumed);
        if (departure == PrisonerDepartureKind.Released && releasingCaptor is int captor)
            RecruitmentRules.SetLoyalty(world, personId, captor,
                Math.Clamp(person.LoyaltyTo(captor) + person.Righteousness / 5, 0, 255), "prisoner-released", events);
        int destinationId = home?.Id ?? destinations[world.NextRandom("personnel-exile-placement") % (ulong)destinations.Length];
        if (home is null)
        {
            PersonnelTransitions.DiscoverDetached(world, personId, destinationId, events);
        }
        else
        {
            var status = world.Forces[person.OwnerId!.Value].LeaderId == personId ? OfficerStatus.Ruler : OfficerStatus.Serving;
            world.Replace(PersonnelTransitions.BeginStatus(world.Officers[personId], status, world.Clock.Year) with
                { SettlementId = home.Id, RegionId = null, UnitId = null });
            var context = new PersonnelReturnContext(control?.ControllerId ?? "system", person.OwnerId.Value, sequence);
            var route = new PersonnelRoute(position, PersonnelLocations.Position(world, places, home.Id), false);
            PersonnelReturns.Schedule(world, context, personId, origin, home.Id,
                route.Days, world.CurrentDateCommandsConsumed, events, route, originPosition is not null);
        }
        events.Add(departure switch
        {
            PrisonerDepartureKind.Released when releasingCaptor is int releasedBy =>
                new PrisonerReleasedEvent(world.Clock, releasedBy, personId, home?.OwnerId, destinationId),
            PrisonerDepartureKind.Escaped =>
                new PrisonerEscapedEvent(world.Clock, personId, person.SettlementId, person.UnitId, destinationId)
                { Occurrence = new(person.OwnerId, origin, person.UnitId is int field ? world.Units[field].Position : null),
                    CaptorForceId = person.UnitId is int detentionUnit
                    ? world.Units[detentionUnit].OwnerId : world.Settlements[person.SettlementId!.Value].OwnerId!.Value },
            PrisonerDepartureKind.Displaced =>
                new PrisonerDisplacedEvent(world.Clock, personId, home?.OwnerId, origin, destinationId),
            PrisonerDepartureKind.Recovered =>
                new PrisonerRecoveredEvent(world.Clock, personId, person.OwnerId!.Value, destinationId),
            _ => throw new InvalidOperationException("Invalid prisoner departure context.")
        });
        return CommandRejection.None;
    }
}
