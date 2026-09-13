using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ReformUnitCommand(Guid CommandId, int IssuerId, long Sequence, int UnitId,
    int FormationId, ImmutableArray<int> OfficerIds, MilitaryOrder? Order) : GameCommand(CommandId, IssuerId, Sequence)
{
    [System.Text.Json.Serialization.JsonRequired] public bool ConfirmAttack { get; init; }
}
public sealed record UnitReformedEvent(GameClock Clock, int UnitId, int PreviousFormationId, int FormationId,
    ImmutableArray<int> OfficerIds) : DomainEvent;

internal static class UnitReformation
{
    internal static CommandRejection Apply(WorldMutation world, ReformUnitCommand command, MilitaryRulebook? rules,
        TreasureRulebook treasures, GovernanceRulebook governance, List<DomainEvent> events)
    {
        if (rules is null || !rules.Formations.TryGetValue(command.FormationId, out var formation))
            return CommandRejection.UnsupportedCommand;
        if (!world.Units.TryGetValue(command.UnitId, out var unit) || unit.OwnerId != command.IssuerId || unit.FormationId is not int previous)
            return CommandRejection.InvalidTask;
        if (unit.Condition is UnitCondition.Confused or UnitCondition.Routed || unit.IsEmbedded || unit.IsPersonnel)
            return CommandRejection.PersonUnavailable;
        var members = world.Officers.Values.Where(p => p.IsServing && p.UnitId == unit.Id).Select(p => p.Id).Order().ToArray();
        if (command.OfficerIds.IsDefaultOrEmpty || command.OfficerIds.Length > formation.OfficerCapacity ||
            !command.OfficerIds.Order().SequenceEqual(members) || unit.Soldiers > formation.SoldierCapacity)
            return CommandRejection.InvalidTask;
        if (!FormationEligibility.CanUse(world, command.IssuerId, command.OfficerIds, formation, treasures))
            return CommandRejection.MissingFormationTactic;
        if (rules.Map.PassageCost(unit.Position, formation.Footprint, formation.TravelMode,
            MilitaryMovement.Occupancy(world, rules, unit.Id, null).ToArray()) is null) return CommandRejection.NoDestination;
        if (command.Order is not null && !MilitaryMovement.ValidOrder(world, unit.OwnerId, command.Order, formation, rules))
            return CommandRejection.InvalidTask;
        if (command.Order?.Kind == MilitaryOrderKind.Construct) return CommandRejection.InvalidTask;
        if (!command.ConfirmAttack && MilitaryDiplomacy.Warning(world, command.IssuerId, command.Order, rules) != AttackWarning.None)
            return CommandRejection.AttackConfirmationRequired;
        int cost = previous == formation.Id ? 0 : formation.DeploymentCost;
        var account = ResourceAccounts.Resolve(world, unit.OwnerId, unit.CorpsId);
        if (ResourceAccounts.Balance(world, account, ResourceKind.Money) < cost) return CommandRejection.InsufficientFunds;
        ResourceAccounts.Change(world, account, ResourceKind.Money, -cost, "unit-reformation", unit.Id, events, true);
        FacilityConstruction.Cancel(world, unit.Id, events);
        var memberOrder = UnitLeadership.Order(world, command.OfficerIds, governance);
        world.Replace(unit with { FormationId = formation.Id, Order = command.Order, CommanderId = memberOrder[0], MemberOrder = memberOrder,
            EngagementTargetId = null, AwaitingPostAction = false,
            MemberRanks = formation.CaptureRanks.IsEmpty ? ImmutableDictionary<int, BattleRank>.Empty : command.OfficerIds
                .Select((id, index) => (id, rank: formation.CaptureRanks[index])).ToImmutableDictionary(p => p.id, p => p.rank) });
        events.Add(new UnitReformedEvent(world.Clock, unit.Id, previous, formation.Id, command.OfficerIds));
        events.Add(new UnitOrderChangedEvent(world.Clock, unit.Id, command.Order, "reformed"));
        return CommandRejection.None;
    }
}
