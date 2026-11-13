using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record StartFormationTeachingCommand(Guid CommandId, int IssuerId, long Sequence,
    int SettlementId, int TeacherId, int StudentId, int FormationId) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record FormationTeachingStartedEvent(GameClock Clock, int ForceId, int SettlementId, int TeacherId, int StudentId, int FormationId) : DomainEvent;
public sealed record FormationTeachingCompletedEvent(GameClock Clock, int ForceId, int SettlementId, int TeacherId, int StudentId, int FormationId) : DomainEvent;
public sealed record FormationTeachingCancelledEvent(GameClock Clock, int ForceId, int SettlementId, int TeacherId, int StudentId, int FormationId) : DomainEvent;

internal static class FormationTeaching
{
    internal static CommandRejection Start(WorldMutation world, StartFormationTeachingCommand command, MilitaryRulebook? military, List<DomainEvent> events)
    {
        if (!world.Simulation.NewGame.FormationTrainingEnabled || military is null || !military.Formations.TryGetValue(command.FormationId, out var formation) ||
            formation.TravelMode != TravelMode.Land || military.IsFixedFormation(formation.Id) || formation.Id == military.UnformedFormationId ||
            military.SiegeEquipment.Values.Any(s => s.FormationId == formation.Id) || command.TeacherId == command.StudentId ||
            !world.Settlements.TryGetValue(command.SettlementId, out var place) || place.OwnerId != command.IssuerId ||
            place.IsDestroyed || place.IsDormant || !place.CanHostPersons || place.Durability <= 0 ||
            !world.Officers.TryGetValue(command.TeacherId, out var teacher) || !world.Officers.TryGetValue(command.StudentId, out var student) ||
            !AtPlace(teacher, command.IssuerId, place.Id) || !AtPlace(student, command.IssuerId, place.Id) || teacher.Acted || student.Acted ||
            !FormationKnowledge.Knows(teacher, formation.Id) || student.FormationProficiencies is null || FormationKnowledge.Knows(student, formation.Id) ||
            Busy(world, teacher.Id) || Busy(world, student.Id)) return CommandRejection.InvalidTask;
        world.Replace(student with { FormationTeaching = new(teacher.Id, command.IssuerId, place.Id, formation.Id, 30) });
        events.Add(new FormationTeachingStartedEvent(world.Clock, command.IssuerId, place.Id, teacher.Id, student.Id, formation.Id));
        return CommandRejection.None;
    }
    private static bool AtPlace(OfficerState person, int force, int place) => !person.IsDormant && person.IsServing && person.OwnerId == force &&
        person.SettlementId == place && person.UnitId is null;
    private static bool Busy(WorldMutation world, int id) => world.AllOfficers.Values.Any(p => p.FormationTeaching is { } t && (p.Id == id || t.TeacherId == id));

    internal static void Reconcile(WorldMutation world, List<DomainEvent> events)
    {
        foreach (var student in world.AllOfficers.Values.Where(p => p.FormationTeaching is not null).OrderBy(p => p.Id).ToArray())
        {
            var t = student.FormationTeaching!;
            if (world.Settlements.TryGetValue(t.SettlementId, out var place) && place.OwnerId == t.ForceId &&
                !place.IsDormant && !place.IsDestroyed && place.CanHostPersons && place.Durability > 0 &&
                world.Officers.TryGetValue(t.TeacherId, out var teacher) && AtPlace(teacher, t.ForceId, t.SettlementId) &&
                AtPlace(student, t.ForceId, t.SettlementId) && FormationKnowledge.Knows(teacher, t.FormationId) &&
                student.FormationProficiencies is not null && !FormationKnowledge.Knows(student, t.FormationId)) continue;
            world.Replace(student with { FormationTeaching = null });
            events.Add(new FormationTeachingCancelledEvent(world.Clock, t.ForceId, t.SettlementId, t.TeacherId, student.Id, t.FormationId));
        }
    }
    internal static void AdvanceDay(WorldMutation world, List<DomainEvent> events)
    {
        Reconcile(world, events);
        foreach (var student in world.Officers.Values.Where(p => p.FormationTeaching is not null).OrderBy(p => p.Id).ToArray())
        {
            var t = student.FormationTeaching!;
            if (t.DaysRemaining > 1) world.Replace(student with { FormationTeaching = t with { DaysRemaining = t.DaysRemaining - 1 } });
            else
            {
                var practice = student.FormationPractice?.Remove(t.FormationId);
                world.Replace(student with { FormationTeaching = null, FormationProficiencies = student.FormationProficiencies!.Add(t.FormationId, 0),
                    FormationPractice = practice is { Count: > 0 } ? practice : null });
                events.Add(new FormationTeachingCompletedEvent(world.Clock, t.ForceId, t.SettlementId, t.TeacherId, student.Id, t.FormationId));
            }
        }
    }
}
