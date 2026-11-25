namespace San9.Core.Simulation;

internal static class DiplomacyJourney
{
    internal static bool IsDiplomatic(PersistentTask task) => task.Gift is not null || task.PrisonerReturn is not null ||
        task.AttackRequest is not null || task.Surrender is not null;

    internal static int Duration(PersistentTask task) =>
        task.Started.IsXunBoundary && task.Journey is { Route: { } route } ? route.XunRoundedDays : -1;
}
