using San9.Core.Domain;

namespace San9.Core.Simulation;

public static class NaturalMortality
{
    public static bool Enabled(SimulationState simulation, CompanionSettings companion) =>
        simulation.NaturalDeathsEnabled && !companion.InfiniteLifespan;

    public static int Chance(int year, OfficerState person)
    {
        long age = (long)year + 1 - person.BirthYear;
        return age >= 100 ? 100 : (int)Math.Clamp(3 * (age - 6L * person.LifespanTier - 35), 0, 100);
    }

    internal static void Apply(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (!Enabled(world.Simulation, world.Companion) || world.Clock.DayNumber - world.Simulation.Started.DayNumber < 360) return;
        foreach (int id in world.Officers.Keys.Order().ToArray())
        {
            var person = world.Officers[id];
            if (person.Status is OfficerStatus.Dead or OfficerStatus.Special || person.ManualAppearance is not null || person.BirthYear == 0 ||
                person.OwnerId is int owner && (world.Forces[owner].IsBarbarian || world.Forces[owner].LeaderId == id &&
                    world.Simulation.Controls.Any(c => c.ForceId == owner && c.Kind == ControllerKind.Player))) continue;
            if (world.NextRandom("personnel-natural-death") % 100 >= (ulong)Chance(world.Clock.Year, person)) continue;
            OfficerDeath.Resolve(world, id, OfficerDeathCause.Natural, rules, events, afterDailyCommands: false);
            if (!ResolutionPipeline.CheckTerminal(world, events)) return;
        }
    }
}
