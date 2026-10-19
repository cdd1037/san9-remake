using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Current origin state required by domestic work and civilian missions.</summary>
internal static class SettlementOperations
{
    internal static bool Available(WorldMutation world, SettlementState home, MilitaryRulebook? military) =>
        home.ConfusionDaysRemaining == 0 &&
        (military is null || !world.Engagements(military).Engaged(new(home.Id, true)));
}

public sealed partial class SimulationEngine
{
    /// <summary>Current origin state for civilian work; does not check ownership, personnel or resources.</summary>
    public bool CanStartSettlementWork(int settlementId)
    {
        var world = PreparationWorld();
        return world.Settlements.TryGetValue(settlementId, out var home) && !home.IsDestroyed &&
            home.Facility?.BuilderUnitId is null && SettlementOperations.Available(world, home, _rules.Military);
    }
}
