using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

internal static class EndingSession
{
    internal static EndingSnapshot? Create(ScenarioInitialState content, WorldSnapshot world)
    {
        if (world.Simulation.Boundary != SimulationBoundary.Terminal || content.Ending is null) return null;
        var terminal = TerminalEvaluation.Evaluate(world, content.Terminal);
        if (terminal.Route != TerminalRoute.PlayerVictoryEnding || terminal.WinningForceId is not int winner) return null;
        var treasures = new TreasureRulebook(content.Information.Treasures.Select(t => t.ToRule()));
        var governance = new GovernanceRulebook(content.Information.Offices.Select(o => new GovernorOfficeRule(o.Id, o.CommandCapacity, o.GovernorPriority)
            { AbilityBonuses = o.AbilityBonuses, Salary = o.Salary, RequiredRankId = o.RequiredRankId }),
            content.Information.Ranks.ToDictionary(r => r.Id, r => r.RequiredTrust));
        return EndingSnapshotFactory.Create(world, winner, content.Ending, new(treasures, governance));
    }

    internal static EndingResult Resolve(ScenarioInitialState content, WorldSnapshot world, EndingSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var snapshot = Create(content, world) ?? throw new ArgumentException("No player victory ending is available.");
        return EndingResolution.Resolve(snapshot, selection.Officeholders, selection.Policy);
    }
}
