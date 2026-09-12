using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record AppointAdviserCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record AdviserAppointedEvent(GameClock Clock, int ForceId, int? PreviousOfficerId, int OfficerId) : DomainEvent;

internal static class PersonnelAppointments
{
    internal static CommandRejection Apply(WorldMutation world, AppointAdviserCommand command, List<DomainEvent> events)
    {
        if (!world.Officers.TryGetValue(command.OfficerId, out var candidate)) return CommandRejection.UnknownPerson;
        if (candidate.OwnerId != command.IssuerId || candidate.Status != OfficerStatus.Serving || candidate.Abilities.Intelligence < 70 ||
            world.Forces[command.IssuerId].LeaderId == candidate.Id || world.Corps.Values.Any(c => c.LeaderId == candidate.Id))
            return CommandRejection.PersonUnavailable;
        var force = world.Forces[command.IssuerId];
        if (force.AdviserId is int oldId)
        {
            var old = world.Officers[oldId];
            if (candidate.Abilities.Intelligence <= old.Abilities.Intelligence)
            {
                int loss = (int)(world.NextRandom("personnel-adviser-replacement") % 3);
                RecruitmentRules.SetLoyalty(world, oldId, force.Id, Math.Max(0, old.Loyalty - loss), "adviser-replaced", events);
            }
            world.Replace(world.Officers[oldId] with { Status = OfficerStatus.Serving });
        }
        world.Replace(candidate with { Status = OfficerStatus.Strategist });
        world.Replace(force with { AdviserId = candidate.Id });
        events.Add(new AdviserAppointedEvent(world.Clock, force.Id, force.AdviserId, candidate.Id));
        return CommandRejection.None;
    }
}
