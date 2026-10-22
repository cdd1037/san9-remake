using San9.Content.Text;

namespace San9.Application.Presentation;

public sealed record ExecutionCommandRow(int OfficerId, string Officer, string Command, string Target, long RemainingDays,
    bool FinishesThisXun);

public static class ExecutionCommands
{
    public static IReadOnlyList<ExecutionCommandRow> Read(StrategySnapshot view, TextCatalog texts)
    {
        // Execution has no active issuing seat. Each listed player's force owns its command visibility.
        var players = view.World.Simulation.Controls.Where(c => c.Kind == San9.Core.Simulation.ControllerKind.Player)
            .Select(c => c.ForceId).ToHashSet();
        var commands = new OfficerCommands(view, texts);
        var people = view.World.Officers.ToDictionary(p => p.Id);
        // Daily commands resolve on the displayed date before the clock advances.
        int daysInXun = 10 - (view.World.Clock.Day - 1) % 10;
        return view.World.Simulation.Tasks.Where(t => players.Contains(t.ForceId)).OrderBy(t => t.Sequence).Select(task =>
        {
            var person = people[task.OfficerId];
            var (order, target) = commands.ReadProgress(task);
            long days = OfficerArrival.Read(view, person, task, task.ForceId).Number
                ?? Math.Max(0, task.Due.DayNumber - view.World.Clock.DayNumber);
            return new ExecutionCommandRow(person.Id, texts.Resolve(view.MessageSpeaker(person.Id).NameKey), order,
                target, days, days <= daysInXun);
        }).ToArray();
    }
}
