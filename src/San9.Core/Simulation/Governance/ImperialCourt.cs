using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record EmperorDeathEvent(GameClock Clock, int OfficerId, int? PreviousEmperorId, int? CurrentEmperorId) : DomainEvent;

internal static class ImperialCourt
{
    internal static int? CurrentId(WorldMutation world, EconomyRulebook rules)
    {
        if (world.Simulation.ImperialCourtEnded) return null;
        foreach (int id in rules.ImperialCourt.SuccessionOrder)
            if (world.Officers.TryGetValue(id, out var person) && person.Status != OfficerStatus.Dead) return id;
        return null;
    }

    internal static int? City(WorldMutation world, EconomyRulebook rules)
    {
        if (CurrentId(world, rules) is not int id) return null;
        var emperor = world.Officers[id];
        if (emperor.UnitId is not null || world.Simulation.Tasks.Any(t => t.OfficerId == id && t.Journey is not null) ||
            (emperor.SettlementId ?? emperor.RegionId) is not int place) return null;
        bool City(int location) => world.Settlements.TryGetValue(location, out var city) && city.Kind == SettlementKind.City && !city.IsDestroyed;
        return City(place) ? place : rules.RegionCityIds.TryGetValue(place, out int parent) && City(parent) ? parent : null;
    }

    internal static void ApplyDeaths(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (world.Simulation.ImperialCourtEnded) return;
        var court = rules.Economy.ImperialCourt;
        foreach (var death in court.DeathEvents)
        {
            if (world.Simulation.Started.DayNumber >= death.ScenarioStartsBefore.DayNumber ||
                world.Clock.DayNumber < death.EarliestDate.DayNumber || !world.Officers.ContainsKey(death.OfficerId)) continue;
            // Original eligibility consumes its survival draw before testing the person's death state.
            if (world.NextRandom("event-emperor-death") % 100 < (ulong)death.SurvivalChancePercent ||
                world.Officers[death.OfficerId].Status == OfficerStatus.Dead) continue;
            int? previous = court.Resolve(false, world.Officers.Values)?.Id;
            OfficerDeath.Resolve(world, death.OfficerId, OfficerDeathCause.HistoricalEvent, rules, events, afterDailyCommands: false);
            events.Add(new EmperorDeathEvent(world.Clock, death.OfficerId, previous, court.Resolve(false, world.Officers.Values)?.Id));
            // The quarterly prelude selects its first successful event, rather than
            // applying the entire death catalog. Later candidates retry next quarter.
            return;
        }
    }
}
