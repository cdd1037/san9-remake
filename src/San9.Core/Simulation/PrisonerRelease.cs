using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ReleasePrisonerCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record PrisonerReleasedEvent(GameClock Clock, int CaptorId, int OfficerId, int? ForceId, int DestinationId) : DomainEvent;

internal static class PrisonerRelease
{
    internal static CommandRejection Apply(WorldMutation world, GameCommand command, int personId,
        PersonnelRulebook places, EconomyRulebook economy, List<DomainEvent> events)
    {
        if (!world.Officers.TryGetValue(personId, out var person)) return CommandRejection.UnknownPerson;
        if (person.Status != OfficerStatus.Captive || person.SettlementId is not int origin ||
            world.Settlements[origin].OwnerId != command.IssuerId || !places.Places.ContainsKey(origin))
            return CommandRejection.PersonUnavailable;
        var home = person.OwnerId is int forceId ? world.Settlements.Values.Where(s => s.OwnerId == forceId &&
            s.CanHostPersons && s.Durability > 0 && places.Places.ContainsKey(s.Id))
            .OrderBy(s => places.JourneyDays(origin, s.Id, PersonnelJourneyKind.Move)).ThenBy(s => s.Id).FirstOrDefault() : null;
        if (home is null && world.Forces.Values.Any(f => f.LeaderId == personId)) return CommandRejection.NoDestination;
        var destinations = home is null ? PersonnelExile.Destinations(world, origin, places) : [];
        if (home is null && destinations.Length == 0) return CommandRejection.NoDestination;
        var control = home is null ? null : world.Simulation.Controls.FirstOrDefault(c => c.ForceId == person.OwnerId);
        PersonnelTransitions.CancelMissions(world, personId, "prisoner-released", events, places, false);
        RecruitmentRules.SetLoyalty(world, personId, command.IssuerId,
            Math.Clamp(person.LoyaltyTo(command.IssuerId) + person.Righteousness / 5, 0, 255), "prisoner-released", events);
        int destinationId = home?.Id ?? destinations[world.NextRandom("personnel-exile-placement") % (ulong)destinations.Length];
        if (home is null)
        {
            PersonnelTransitions.DiscoverDetached(world, personId, destinationId, events);
        }
        else
        {
            world.Replace(world.Officers[personId] with { Status = world.Forces[person.OwnerId!.Value].LeaderId == personId
                ? OfficerStatus.Ruler : OfficerStatus.Serving, SettlementId = home.Id, RegionId = null,
                OfficeId = null, IsCommander = false, Acted = true, StatusStartYear = world.Clock.Year });
            var previous = new PersistentTask(command.CommandId, PersonnelReturns.Kind, control?.ControllerId ?? "system",
                person.OwnerId.Value, personId, origin, world.Clock, world.Clock, 0, command.Sequence);
            PersonnelReturns.Schedule(world, previous, personId, origin, home.Id,
                places.JourneyDays(origin, home.Id, PersonnelJourneyKind.Move), false, events);
        }
        EconomicCycle.UpdateTrust(world, economy, events);
        events.Add(new PrisonerReleasedEvent(world.Clock, command.IssuerId, personId, home?.OwnerId, destinationId));
        return CommandRejection.None;
    }
}
