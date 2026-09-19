using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record EmperorDeathEvent(GameClock Clock, int OfficerId, int? PreviousEmperorId, int? CurrentEmperorId) : DomainEvent;

internal static class ImperialCourt
{
    internal static int? City(WorldMutation world, EconomyRulebook rules)
    {
        var emperor = rules.ImperialCourt.Resolve(world.Simulation.ImperialCourtEnded, world.Officers.Values);
        return ImperialCourtDefinition.City(emperor, world.Settlements.Values, rules.RegionCityIds,
            emperor is not null && world.Simulation.Tasks.Any(t => t.OfficerId == emperor.Id && t.Journey is not null));
    }

    internal static void ApplyDeaths(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (world.Simulation.ImperialCourtEnded) return;
        var court = rules.Economy.ImperialCourt;
        foreach (var death in court.DeathEvents)
        {
            if (world.Simulation.Started.DayNumber >= death.ScenarioStartsBefore.DayNumber ||
                world.Clock.DayNumber < death.EarliestDate.DayNumber) continue;
            // Original eligibility consumes its survival draw before testing the person's death state.
            if (world.NextRandom("event-emperor-death") % 100 < (ulong)death.SurvivalChancePercent ||
                world.Officers[death.OfficerId].Status == OfficerStatus.Dead) continue;
            int? previous = court.Resolve(false, world.Officers.Values)?.Id;
            OfficerDeath.Resolve(world, death.OfficerId, OfficerDeathCause.HistoricalEvent, rules, events, afterDailyCommands: false);
            events.Add(new EmperorDeathEvent(world.Clock, death.OfficerId, previous, court.Resolve(false, world.Officers.Values)?.Id));
            if (world.Simulation.TerminalReason is not null) return;
        }
    }
}
