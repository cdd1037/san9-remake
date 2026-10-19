using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Current origin state required by domestic work and civilian missions.</summary>
internal static class SettlementOperations
{
    internal static bool Available(WorldMutation world, SettlementState home, MilitaryRulebook? military) =>
        home.ConfusionDaysRemaining == 0 &&
        (military is null || !world.Engagements(military).Engaged(new(home.Id, true)));
}
