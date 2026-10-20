using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

internal static class EndingSession
{
    internal static EndingSnapshot? Create(ScenarioSessionDefinition definition, WorldSnapshot world)
    {
        var content = definition.Content;
        if (world.Simulation.Boundary != SimulationBoundary.Terminal || content.Ending is null) return null;
        var terminal = TerminalEvaluation.Evaluate(world, content.Terminal);
        if (terminal.Route != TerminalRoute.PlayerVictoryEnding || terminal.WinningForceId is not int winner) return null;
        return EndingSnapshotFactory.Create(world, winner, content.Ending, definition.Rules.Abilities, definition.Rules.Treasures);
    }

    internal static EndingResult Resolve(ScenarioSessionDefinition definition, WorldSnapshot world, EndingSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var snapshot = Create(definition, world) ?? throw new ArgumentException("No player victory ending is available.");
        return EndingResolution.Resolve(snapshot, selection.Officeholders, selection.Policy);
    }
}
