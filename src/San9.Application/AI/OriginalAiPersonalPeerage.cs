using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Conservative deterministic peerage choices; all eligibility is checked by the domain command.</summary>
internal static class OriginalAiPersonalPeerage
{
    internal static void Manage(SimulationEngine engine, ControlSeat seat, List<DomainEvent> events)
    {
        var world = engine.Snapshot;
        if (seat.Kind != ControllerKind.Automatic || world.Simulation.NewGame.PersonalPeerageEnabled == false) return;
        var force = world.Forces.FirstOrDefault(f => f.Id == seat.ForceId);
        var root = world.Corps.FirstOrDefault(c => c.OwnerId == seat.ForceId && c.IsRoot);
        if (force?.PersonalPeerageRegimeId is null || root is null) return;
        var policy = engine.GetPersonalPeeragePolicy(seat.ForceId);
        var context = new CommandContext(root.ControlMode == CorpsControlMode.Automatic ? CorpsControl.ControllerId(root.Id) : seat.ControllerId);
        var candidates = world.Officers.Where(p => p.OwnerId == seat.ForceId && p.IsServing && !p.IsDormant && p.Id != force.LeaderId)
            .Select(p => new { Person = p, Merit = (long)p.BattleMeritFor(seat.ForceId) + p.ServiceMeritFor(seat.ForceId), Experience = p.TacticProficiencies.Sum(x => (long)x),
                Service = p.StatusStartYear is int year ? Math.Max(0L, (long)world.Clock.Year - year) : 0L })
            .OrderByDescending(c => c.Merit).ThenByDescending(c => c.Experience).ThenByDescending(c => c.Service).ThenBy(c => c.Person.Loyalty).ThenBy(c => c.Person.Id);
        foreach (var candidate in candidates)
        {
            var person = candidate.Person;
            int current = person.PersonalPeerage is { } title ? (int)title.Grade : -1;
            if (current >= (int)PersonalPeerageGrade.CountyMarquis || candidate.Merit == 0 && candidate.Experience == 0 && person.Loyalty >= 100) continue;
            int target = Math.Min((int)PersonalPeerageGrade.CountyMarquis, current + 1);
            if (target > 0 && candidate.Merit < target * 3 && candidate.Experience < target * 1000L) continue;
            var grade = (PersonalPeerageGrade)target;
            int? assigned = person.UnitId is int unit ? world.Units.FirstOrDefault(u => u.Id == unit)?.CorpsId :
                person.SettlementId is int place ? world.Settlements.FirstOrDefault(s => s.Id == place)?.CorpsId : null;
            var corps = assigned is int id ? world.Corps.FirstOrDefault(c => c.Id == id && c.OwnerId == seat.ForceId) : root;
            if (corps is null) continue;
            var outlook = engine.GetAccountOutlook(seat.ForceId, corps.Id);
            int additionalSalary = policy.Grades[grade].Salary - policy.Salary(person.PersonalPeerage);
            if (additionalSalary > 0 && outlook.Money.Balance - outlook.Money.Expenditure < additionalSalary * 9L) continue;
            long sequence = engine.Snapshot.Simulation.NextSequence;
            var command = new SetPersonalPeerageCommand(SimulationIdentity.Command(sequence, seat.ForceId), seat.ForceId, sequence, person.Id, new(grade, null));
            if (!engine.Validate(context, command).Accepted) continue;
            var result = engine.Execute(context, command);
            if (result.Submission.Accepted) events.AddRange(result.Events);
            return;
        }
    }
}
