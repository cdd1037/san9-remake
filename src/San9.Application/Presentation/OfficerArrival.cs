using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal static class OfficerArrival
{
    public static CatalogCell Read(StrategySnapshot view, OfficerState person, PersistentTask? task)
    {
        // Corps retain their root force as OwnerId. Visibility precedes applicability.
        if (person.OwnerId != view.PlayerForceId) return new("**", IsUnavailable: true);
        if (person.UnitId is not null || task?.Journey is not { } journey) return CatalogCell.Unavailable;

        long days = journey.ReturnLeg is { } leg ? leg.CountdownDays
            : task.Kind == "tactic-study" ? task.Due.DayNumber - task.Started.DayNumber
            : task.Kind == "personnel-search" || task.Gift is not null || task.PrisonerReturn is not null ||
                task.AttackRequest is not null || task.Surrender is not null ? journey.Route.XunRoundedDays
            : journey.Route.Days;
        // Returns created after daily commands skip that day's decrement, even though
        // the subsequent date advance increments their general task progress counter.
        int elapsed = Math.Max(0, task.ProgressDays - (journey.ReturnLeg?.AfterDailyCommands == true ? 1 : 0));
        // A decision can expose the world after the current day's pre-command decrement.
        if (view.World.Simulation.PendingDailyTask is { Phase: TaskResolutionPhase.DailyCommands } &&
            !(journey.ReturnLeg?.AfterDailyCommands == true && task.Started == view.World.Clock)) elapsed++;
        return Math.Max(0, days - elapsed);
    }
}
