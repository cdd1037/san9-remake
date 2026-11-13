using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal static class OriginalAiFormationTeaching
{
    internal static void TryStart(SimulationEngine engine, MilitaryRulebook? military, ControlSeat seat, List<DomainEvent> events)
    {
        var world = engine.Snapshot;
        if (seat.Kind != ControllerKind.Automatic || military is null || !world.Simulation.NewGame.FormationTrainingEnabled) return;
        var root = world.Corps.FirstOrDefault(c => c.OwnerId == seat.ForceId && c.IsRoot);
        if (root is null) return;
        var context = new CommandContext(root.ControlMode == CorpsControlMode.Automatic ? CorpsControl.ControllerId(root.Id) : seat.ControllerId);
        var busy = world.Officers.Where(p => p.FormationTeaching is not null)
            .SelectMany(p => new[] { p.Id, p.FormationTeaching!.TeacherId }).ToHashSet();
        foreach (var place in world.Settlements.Where(s => s.OwnerId == seat.ForceId && !s.IsDormant && !s.IsDestroyed &&
                     s.CanHostPersons && CorpsControl.CanControlSettlement(world, seat, context, s.Id)).OrderBy(s => s.Id))
        {
            var people = world.Officers.Where(p => p.OwnerId == seat.ForceId && p.IsServing && !p.IsDormant && !p.Acted &&
                p.SettlementId == place.Id && p.UnitId is null && !busy.Contains(p.Id) && p.FormationProficiencies is not null)
                .OrderByDescending(p => p.BaseAbilities.Leadership).ThenBy(p => p.Id).ToArray();
            foreach (var student in people)
            foreach (var teacher in people.Where(p => p.Id != student.Id))
            foreach (int formation in teacher.FormationProficiencies!.Keys.Where(id => !FormationKnowledge.Knows(student, id)).Order())
            {
                long sequence = engine.Snapshot.Simulation.NextSequence;
                var command = new StartFormationTeachingCommand(SimulationIdentity.Command(sequence, seat.ForceId), seat.ForceId,
                    sequence, place.Id, teacher.Id, student.Id, formation);
                var result = engine.Execute(context, command);
                if (!result.Submission.Accepted) continue;
                events.AddRange(result.Events); return;
            }
        }
    }
}
