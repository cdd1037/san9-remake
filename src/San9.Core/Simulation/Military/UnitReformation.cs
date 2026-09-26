using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ReformUnitCommand(Guid CommandId, int IssuerId, long Sequence, int UnitId,
    int FormationId, ImmutableArray<int> OfficerIds, MilitaryOrder? Order) : GameCommand(CommandId, IssuerId, Sequence)
{
    [JsonRequired] public int? NavalFormationId { get; init; }
    [JsonRequired] public bool ConfirmAttack { get; init; }
}
public sealed record UnitReformedEvent(GameClock Clock, int UnitId, int PreviousFormationId, int FormationId,
    ImmutableArray<int> OfficerIds, int? PreviousNavalFormationId, int? NavalFormationId) : DomainEvent;

internal static class UnitReformation
{
    internal static CommandRejection Apply(WorldMutation world, ReformUnitCommand command, MilitaryRulebook? rules,
        TreasureRulebook treasures, GovernanceRulebook governance, List<DomainEvent> events)
    {
        if (rules is null || !rules.Formations.TryGetValue(command.FormationId, out var formation))
            return CommandRejection.UnsupportedCommand;
        if (!world.Units.TryGetValue(command.UnitId, out var unit) || unit.OwnerId != command.IssuerId || unit.FormationId is not int previous)
            return CommandRejection.InvalidTask;
        if (rules.Purpose(previous) != UnitPurpose.Combat) return CommandRejection.FormationUnavailable;
        if (unit.Condition is UnitCondition.Confused or UnitCondition.Routed || unit.IsEmbedded || unit.IsPersonnel)
            return CommandRejection.PersonUnavailable;
        int? navalId = command.NavalFormationId ?? (formation.TravelMode == TravelMode.Land ? unit.NavalFormationId : null);
        if (!rules.ValidUnitFormations(formation.Id, navalId)) return CommandRejection.InvalidTask;
        var naval = navalId is int selected ? rules.Formations[selected] : null;
        if (!rules.CanSelectFormation(world.Forces[command.IssuerId], formation.Id) ||
            naval is not null && !rules.CanSelectFormation(world.Forces[command.IssuerId], naval.Id))
            return CommandRejection.FormationUnavailable;
        var travel = rules.Travel(formation.Id, navalId);
        var members = world.Officers.Values.Where(p => p.IsServing && p.UnitId == unit.Id).Select(p => p.Id).Order().ToArray();
        if (command.OfficerIds.IsDefaultOrEmpty || command.OfficerIds.Length > formation.OfficerCapacity ||
            !command.OfficerIds.Order().SequenceEqual(members) || unit.Soldiers > formation.SoldierCapacity)
            return CommandRejection.InvalidTask;
        if (naval is not null && (unit.Soldiers > naval.SoldierCapacity || command.OfficerIds.Length > naval.OfficerCapacity)) return CommandRejection.InvalidTask;
        if (!FormationEligibility.CanUse(world, command.IssuerId, command.OfficerIds, formation, treasures) ||
            naval is not null && !FormationEligibility.CanUse(world, command.IssuerId, command.OfficerIds, naval, treasures))
            return CommandRejection.MissingFormationTactic;
        if (travel.Map.PassageCost(unit.Position, travel.Shape, travel.Mode,
            MilitaryMovement.Occupancy(world, rules, unit.Id, null).ToArray()) is null) return CommandRejection.NoDestination;
        if (command.Order is not null && !MilitaryMovement.ValidOrder(world, unit.OwnerId, command.Order, formation, rules, navalId))
            return CommandRejection.InvalidTask;
        if (command.Order?.Kind == MilitaryOrderKind.Construct) return CommandRejection.InvalidTask;
        if (!command.ConfirmAttack && MilitaryDiplomacy.Warning(world, command.IssuerId, command.Order, rules) != AttackWarning.None)
            return CommandRejection.AttackConfirmationRequired;
        long cost = (long)(previous == formation.Id ? 0 : formation.DeploymentCost) +
            (navalId == unit.NavalFormationId ? 0 : naval?.DeploymentCost ?? 0);
        var account = ResourceAccounts.Resolve(world, unit.OwnerId, unit.CorpsId);
        if (ResourceAccounts.Balance(world, account, ResourceKind.Money) < cost) return CommandRejection.InsufficientFunds;
        ResourceAccounts.Change(world, account, ResourceKind.Money, -cost, "unit-reformation", unit.Id, events, true);
        FacilityConstruction.Cancel(world, unit.Id, events);
        int? commander = LeadershipMaintenance.SelectUnitCommander(world, command.OfficerIds, governance);
        world.Replace(unit with { FormationId = formation.Id, NavalFormationId = navalId, Order = command.Order, CommanderId = commander,
            EngagementTargetId = null, AwaitingPostAction = false, CommandedThisXun = true,
            MemberRanks = formation.CaptureRanks.IsEmpty ? ImmutableDictionary<int, BattleRank>.Empty : command.OfficerIds
                .Select((id, index) => (id, rank: formation.CaptureRanks[index])).ToImmutableDictionary(p => p.id, p => p.rank) });
        events.Add(new UnitReformedEvent(world.Clock, unit.Id, previous, formation.Id, command.OfficerIds, unit.NavalFormationId, navalId));
        events.Add(new UnitOrderChangedEvent(world.Clock, unit.Id, command.Order, "reformed"));
        return CommandRejection.None;
    }
}
