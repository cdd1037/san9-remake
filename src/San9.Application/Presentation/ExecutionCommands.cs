using San9.Content.Text;

namespace San9.Application.Presentation;

public sealed record ExecutionCommandRow(int OfficerId, string Officer, string Command, string Target, long RemainingDays,
    bool FinishesThisXun);

public static class ExecutionCommands
{
    public static IReadOnlyList<ExecutionCommandRow> Read(StrategySnapshot view, TextCatalog texts)
    {
        var commands = new OfficerCommands(view, texts);
        var people = view.World.Officers.ToDictionary(p => p.Id);
        // Daily commands resolve on the displayed date before the clock advances.
        int daysInXun = 10 - (view.World.Clock.Day - 1) % 10;
        return view.World.Simulation.Tasks.Where(t => t.ForceId == view.PlayerForceId).OrderBy(t => t.Sequence).Select(task =>
        {
            var person = people[task.OfficerId]; var cells = commands.Read(person);
            long days = cells.Arrival.Number ?? Math.Max(0, task.Due.DayNumber - view.World.Clock.DayNumber);
            return new ExecutionCommandRow(person.Id, texts.Resolve(view.MessageSpeaker(person.Id).NameKey), cells.Order.Text,
                cells.Target.IsUnavailable ? cells.Object.Text : cells.Target.Text, days, days <= daysInXun);
        }).ToArray();
    }
}
