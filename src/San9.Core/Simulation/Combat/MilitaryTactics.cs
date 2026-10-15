using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SelectTacticCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId, int? TacticId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record TacticSelectedEvent(GameClock Clock, int OfficerId, int? TacticId) : DomainEvent;

internal static class MilitaryTactics
{
    internal static bool HasTactic(IWorldQuery world, int owner, IEnumerable<int> members, int required, TreasureRulebook treasures)
        => world.Forces[owner].ProvidedTactics.Contains(required) || members.Any(id =>
            world.Officers[id].LearnedTactics.Contains(required) || world.HasTreasureTactic(id, required, treasures));

    internal static CommandRejection Eligibility(WorldMutation world, SelectTacticCommand command, SimulationRules rules)
    {
        if (rules.Military is not { } military) return CommandRejection.UnsupportedCommand;
        if (!world.Officers.TryGetValue(command.OfficerId, out var officer) || !officer.IsServing || officer.OwnerId != command.IssuerId ||
            world.IsBusy(officer.Id)) return CommandRejection.PersonUnavailable;
        if (command.TacticId is int id && (!military.Tactics.TryGetValue(id, out var tactic) || !tactic.Selectable ||
            !HasTactic(world, command.IssuerId, [officer.Id], id, rules.Treasures))) return CommandRejection.TacticUnavailable;
        return CommandRejection.None;
    }

    internal static CommandRejection Select(WorldMutation world, SelectTacticCommand command, SimulationRules rules, List<DomainEvent> events)
    {
        var rejection = Eligibility(world, command, rules);
        if (rejection != CommandRejection.None) return rejection;
        world.Replace(world.Officers[command.OfficerId] with { EquippedTacticId = command.TacticId });
        events.Add(new TacticSelectedEvent(world.Clock, command.OfficerId, command.TacticId));
        return CommandRejection.None;
    }

    internal static void TickCooldowns(WorldMutation world)
    {
        foreach (var officer in world.Officers.Values.Where(p => p.TacticCooldown > 0).OrderBy(p => p.Id).ToArray())
            world.Replace(officer with { TacticCooldown = officer.TacticCooldown - 1 });
    }
}
