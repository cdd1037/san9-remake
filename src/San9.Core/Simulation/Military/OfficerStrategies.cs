using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public abstract record OfficerStrategyCommand(Guid CommandId, int IssuerId, long Sequence, ImmutableArray<int> OfficerIds)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record AlienateOfficersCommand(Guid CommandId, int IssuerId, long Sequence,
    ImmutableArray<int> OfficerIds, int TargetOfficerId) : OfficerStrategyCommand(CommandId, IssuerId, Sequence, OfficerIds);
public sealed record BurnOfficersCommand(Guid CommandId, int IssuerId, long Sequence,
    ImmutableArray<int> OfficerIds, int TargetSettlementId) : OfficerStrategyCommand(CommandId, IssuerId, Sequence, OfficerIds);
public sealed record SeizeOfficersCommand(Guid CommandId, int IssuerId, long Sequence,
    ImmutableArray<int> OfficerIds, int TargetSettlementId) : OfficerStrategyCommand(CommandId, IssuerId, Sequence, OfficerIds);

/// <summary>Validates one assignment atomically and starts independent missions in stable officer order.</summary>
internal static class OfficerStrategies
{
    internal static GameCommand ForOfficer(OfficerStrategyCommand command, int officer, Guid taskId) => command switch
    {
        AlienateOfficersCommand c => new AlienateCommand(taskId, c.IssuerId, c.Sequence, officer, c.TargetOfficerId),
        BurnOfficersCommand c => new BurnCommand(taskId, c.IssuerId, c.Sequence, officer, c.TargetSettlementId),
        SeizeOfficersCommand c => new SeizeCommand(taskId, c.IssuerId, c.Sequence, officer, c.TargetSettlementId),
        _ => throw new ArgumentException("Unknown officer strategy.", nameof(command))
    };

    internal static CommandRejection Eligibility(WorldMutation world, OfficerStrategyCommand command, SimulationRules rules)
    {
        if (command.OfficerIds.IsDefaultOrEmpty || command.OfficerIds.Distinct().Count() != command.OfficerIds.Length)
            return CommandRejection.InvalidTask;
        foreach (int officer in command.OfficerIds.Order())
        {
            var rejection = MissionPreparation.Eligibility(world, ForOfficer(command, officer, command.CommandId), rules);
            if (rejection != CommandRejection.None) return rejection;
        }
        return CommandRejection.None;
    }

    internal static CommandRejection Start(WorldMutation world, CommandContext context, OfficerStrategyCommand command,
        SimulationRules rules, List<DomainEvent> events)
    {
        var rejection = Eligibility(world, command, rules);
        if (rejection != CommandRejection.None) return rejection;
        bool first = true;
        foreach (int officer in command.OfficerIds.Order())
        {
            Guid taskId = first ? command.CommandId : world.AllocateTaskId();
            first = false;
            rejection = ForOfficer(command, officer, taskId) switch
            {
                AlienateCommand c => AlienateStrategies.Start(world, context, c, rules, events),
                BurnCommand c => BurnStrategies.Start(world, context, c, rules, events),
                SeizeCommand c => SeizeStrategies.Start(world, context, c, rules, events),
                _ => CommandRejection.UnsupportedCommand
            };
            if (rejection != CommandRejection.None) return rejection;
        }
        return CommandRejection.None;
    }
}
