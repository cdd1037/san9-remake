using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

internal static class EndingSession
{
    // Older terminal documents did not record a winner. Prefer facts already persisted in
    // that archive, and use the old evaluator only as a last resort for such documents.
    internal static WorldSnapshot RestoreTerminalWorld(ScenarioSessionDefinition definition, SessionSnapshot snapshot)
    {
        var world = snapshot.World;
        if (world.Simulation.Boundary != SimulationBoundary.Terminal ||
            world.Simulation.TerminalReason != "conquest" || world.Simulation.TerminalWinnerId is not null) return world;
        var active = world.Forces.Where(f => !f.IsDormant && !f.IsEliminated && !f.IsBarbarian).Select(f => f.Id).ToHashSet();
        var winners = snapshot.History.Where(f => f is not null && f.Role == "unified" &&
            f.Clock == world.Clock && active.Contains(f.ForceId)).Select(f => f.ForceId).Distinct().ToArray();
        int? winner = winners.Length == 1 ? winners[0] : null;
        if (winner is null && snapshot.Ending?.Officeholders is { } officeholders)
        {
            var ids = new[] { officeholders.ChancellorId, officeholders.MarshalId, officeholders.ProtectorId, officeholders.SuccessorId };
            var owners = world.Officers.Where(p => ids.Contains(p.Id)).Select(p => p.OwnerId).Distinct().ToArray();
            if (owners.Length == 1 && owners[0] is int owner && active.Contains(owner)) winner = owner;
        }
        winner ??= TerminalEvaluation.Evaluate(world, definition.Content.Terminal).WinningForceId;
        return winner is null ? world : world with { Simulation = world.Simulation with { TerminalWinnerId = winner } };
    }

    internal static EndingSelection? RestoreSelection(ScenarioSessionDefinition definition, WorldSnapshot world, EndingSelection? selection)
    {
        if (selection is null) return null;
        if (world.Simulation.Boundary != SimulationBoundary.Terminal || world.Simulation.TerminalReason != "conquest" ||
            !Enum.IsDefined(selection.Policy))
            throw new ArgumentException("Invalid saved ending selection.");
        if (world.Simulation.TerminalWinnerId is int winner)
        {
            if (!world.Simulation.Controls.Any(c => c.ForceId == winner && c.Kind == ControllerKind.Player))
                throw new ArgumentException("Saved ending requires a player victory.");
            var members = world.Officers.Where(p => !p.IsDormant && p.IsServing && p.OwnerId == winner).Select(p => p.Id).ToHashSet();
            var ruler = world.Forces.Single(f => f.Id == winner).LeaderId;
            if ((members.Count >= 5) != (selection.Officeholders is not null))
                throw new ArgumentException("Invalid saved ending court.");
            if (selection.Officeholders is { } officeholders)
            {
                int[] ids = [officeholders.ChancellorId, officeholders.MarshalId, officeholders.ProtectorId, officeholders.SuccessorId];
                if (ids.Distinct().Count() != 4 || ids.Any(id => id == ruler || !members.Contains(id)))
                    throw new ArgumentException("Invalid saved ending officeholders.");
            }
        }
        if (selection.Result is { } result)
        {
            if (!Enum.IsDefined(result.Outcome) || result.DecisiveStage is < 1 or > 4)
                throw new ArgumentException("Invalid saved ending result.");
            return selection;
        }
        // Legacy terminal selections can be resolved once if their required content remains.
        return Create(definition, world) is { } prompt
            ? selection with { Result = EndingResolution.Resolve(prompt, selection.Officeholders, selection.Policy) }
            : selection;
    }

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
