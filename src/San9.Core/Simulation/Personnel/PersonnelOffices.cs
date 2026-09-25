using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record OfficeAssignment(int OfficerId, int? OfficeId);
public sealed record AssignOfficesCommand(Guid CommandId, int IssuerId, long Sequence, ImmutableArray<OfficeAssignment> Assignments)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record OfficeAssignedEvent(GameClock Clock, int ForceId, int OfficerId, int? PreviousId, int? OfficeId) : DomainEvent;

internal static class PersonnelOffices
{
    internal static CommandRejection Apply(WorldMutation world, AssignOfficesCommand command, GovernanceRulebook rules,
        List<DomainEvent> events)
    {
        if (command.Assignments.IsDefaultOrEmpty || command.Assignments.Any(a => a is null) ||
            command.Assignments.Select(a => a.OfficerId).Distinct().Count() != command.Assignments.Length)
            return CommandRejection.InvalidTask;
        foreach (var assignment in command.Assignments)
        {
            if (!world.Officers.TryGetValue(assignment.OfficerId, out var person)) return CommandRejection.UnknownPerson;
            if (!person.IsServing || person.OwnerId != command.IssuerId || world.Forces[command.IssuerId].LeaderId == person.Id)
                return CommandRejection.PersonUnavailable;
            if (assignment.OfficeId is int office && (!rules.Offices.TryGetValue(office, out var definition) ||
                !rules.CanAppoint(world.Forces[command.IssuerId].RankId, definition))) return CommandRejection.InvalidOffice;
        }
        var assignments = command.Assignments.ToDictionary(a => a.OfficerId, a => a.OfficeId);
        var finalOffices = world.Officers.Values.Where(p => p.OwnerId == command.IssuerId && p.IsServing)
            .Select(p => assignments.TryGetValue(p.Id, out var office) ? office : p.OfficeId).Where(id => id is not null).ToArray();
        if (finalOffices.Distinct().Count() != finalOffices.Length) return CommandRejection.InvalidOffice;
        foreach (var assignment in command.Assignments.OrderBy(a => a.OfficerId))
        {
            var person = world.Officers[assignment.OfficerId];
            var office = assignment.OfficeId is int id ? rules.Offices[id] : null;
            int oldBonus = person.OfficeId is int oldId ? rules.Offices[oldId].LoyaltyBonus : 0;
            world.Replace(person with { OfficeId = assignment.OfficeId });
            RecruitmentRules.SetLoyalty(world, person.Id, command.IssuerId,
                Math.Clamp(person.Loyalty + (office?.LoyaltyBonus ?? 0) - oldBonus, 0, 255), "office-assigned", events);
            events.Add(new OfficeAssignedEvent(world.Clock, command.IssuerId, person.Id, person.OfficeId, assignment.OfficeId));
        }
        return CommandRejection.None;
    }
}
