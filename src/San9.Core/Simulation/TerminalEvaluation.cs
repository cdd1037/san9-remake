using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum TerminalRoute { Continue, PlayerVictoryEnding, ReturnToTitle }
public sealed record TerminalResult(TerminalRoute Route, string? Reason, int? WinningForceId);

public static class TerminalEvaluation
{
    public static bool IsKnownReason(string? reason) => reason is "conquest" or "time-limit" or "player-defeated";

    public static TerminalResult Evaluate(WorldSnapshot world, TerminalDefinition? definition) =>
        Evaluate(world.Clock, world.Simulation, world.Forces, world.Settlements, definition);

    internal static TerminalResult Evaluate(GameClock clock, SimulationState simulation, IEnumerable<ForceState> forces,
        IEnumerable<SettlementState> settlements, TerminalDefinition? definition)
    {
        if (simulation.TerminalReason is not null && !IsKnownReason(simulation.TerminalReason))
            throw new ArgumentException("Unknown terminal reason.");
        // Once a non-victory reason is set, neither victory nor the year limit can replace it.
        if (simulation.TerminalReason is string reason && reason != "conquest")
            return new(TerminalRoute.ReturnToTitle, reason, null);
        var ordinary = forces.Where(f => !f.IsDormant && !f.IsEliminated && !f.IsBarbarian).OrderBy(f => f.Id).ToArray();
        ForceState? winner = ordinary.Length == 1 ? ordinary[0] : null;
        if (winner is null && definition is { CoreCityIds.IsEmpty: false })
        {
            var cities = settlements.Where(s => !s.IsDormant && s.Kind == SettlementKind.City && !s.IsDestroyed).ToDictionary(s => s.Id);
            winner = ordinary.FirstOrDefault(f => definition.CoreCityIds.All(id => cities.TryGetValue(id, out var city) && city.OwnerId == f.Id));
        }
        if (winner is not null)
            return new(simulation.Controls.Any(c => c.ForceId == winner.Id && c.Kind == ControllerKind.Player)
                ? TerminalRoute.PlayerVictoryEnding : TerminalRoute.ReturnToTitle, "conquest", winner.Id);
        if (simulation.TerminalReason == "conquest")
            throw new ArgumentException("A conquest terminal state must have a winning force.");
        if (definition?.EndYear is int endYear && clock.Year >= endYear)
            return new(TerminalRoute.ReturnToTitle, "time-limit", null);
        return new(TerminalRoute.Continue, null, null);
    }
}
