using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ExileOfficerCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record OfficerExiledEvent(GameClock Clock, int ForceId, int OfficerId, int DestinationId) : DomainEvent;

internal static class PersonnelExile
{
    internal static CommandRejection Apply(WorldMutation world, ExileOfficerCommand command, PersonnelRulebook places,
        EconomyRulebook economy, GovernanceRulebook governance, List<DomainEvent> events)
    {
        if (!world.Officers.TryGetValue(command.OfficerId, out var person)) return CommandRejection.UnknownPerson;
        if (person.Status == OfficerStatus.Captive)
            return PrisonerRelease.Apply(world, command, person.Id, places, economy, events);
        if (!person.IsServing || person.Status == OfficerStatus.Ruler || person.OwnerId != command.IssuerId ||
            world.Forces[command.IssuerId].LeaderId == person.Id || world.Forces[command.IssuerId].IsBarbarian ||
            world.IsTraveling(person.Id))
            return CommandRejection.PersonUnavailable;
        if (person.SettlementId is not int origin || !places.RegionCities.ContainsKey(origin))
            return CommandRejection.InvalidTask;
        var candidates = PersonnelRelocation.Destinations(world, origin, places);
        if (candidates.Length == 0) return CommandRejection.NoDestination;
        int destinationId = candidates[world.NextRandom("personnel-exile-placement") % (ulong)candidates.Length];
        PersonnelTransitions.Exile(world, person.Id, destinationId, events, places, false);
        if (world.Settlements[origin].CorpsId is int corpsId)
        {
            var rejection = GovernanceMaintenance.RepairCorps(world, corpsId, governance, events);
            if (rejection != CommandRejection.None) return rejection;
        }
        events.Add(new OfficerExiledEvent(world.Clock, command.IssuerId, person.Id, destinationId));
        return CommandRejection.None;
    }
}
