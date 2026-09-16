using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SetCorpsControlCommand(Guid CommandId, int IssuerId, long Sequence, int CorpsId, CorpsControlMode Mode)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record CorpsControlChangedEvent(GameClock Clock, int ForceId, int CorpsId, CorpsControlMode Mode) : DomainEvent;

public static class CorpsControl
{
    public static string ControllerId(int corpsId) => $"corps:{corpsId}";

    internal static CommandRejection Set(WorldMutation world, SetCorpsControlCommand command, List<DomainEvent> events)
    {
        if (!Enum.IsDefined(command.Mode) || !world.Corps.TryGetValue(command.CorpsId, out var corps) || corps.OwnerId != command.IssuerId)
            return CommandRejection.InvalidCorps;
        world.Replace(corps with { ControlMode = command.Mode });
        events.Add(new CorpsControlChangedEvent(world.Clock, command.IssuerId, corps.Id, command.Mode));
        return CommandRejection.None;
    }

    internal static CommandRejection Authorize(WorldSnapshot world, ControlSeat seat, CommandContext context, GameCommand command)
    {
        if (seat.ForceId != command.IssuerId) return CommandRejection.NotActiveController;
        var automatic = world.Corps.FirstOrDefault(c => c.OwnerId == command.IssuerId &&
            c.ControlMode == CorpsControlMode.Automatic && ControllerId(c.Id) == context.ControllerId);
        if (automatic is null && seat.ControllerId != context.ControllerId) return CommandRejection.NotActiveController;
        if (automatic is null && !world.Corps.Any(c => c.OwnerId == command.IssuerId)) return CommandRejection.None;
        if (command is SetCorpsControlCommand or SetCorpsDelegationCommand)
            return automatic is null ? CommandRejection.None : CommandRejection.NotActiveController;
        if (automatic is null && seat.Kind == ControllerKind.Player &&
            world.Corps.Any(c => c.OwnerId == seat.ForceId && c.Treasury is null && c.ControlMode == CorpsControlMode.Automatic))
            return CommandRejection.NotActiveController;
        bool OwnPlace(int id)
        {
            var place = world.Settlements.FirstOrDefault(s => s.Id == id);
            if (place?.OwnerId != command.IssuerId) return false;
            if (automatic is not null) return place.CorpsId == automatic.Id;
            return place.CorpsId is not int corpsId || world.Corps.Any(c => c.Id == corpsId &&
                (c.ControlMode == CorpsControlMode.Direct || seat.Kind == ControllerKind.Automatic && c.Treasury is null));
        }
        bool OwnPerson(int id) => world.Officers.FirstOrDefault(p => p.Id == id)?.SettlementId is int home && OwnPlace(home);
        bool OwnUnit(int id)
        {
            var unit = world.Units.FirstOrDefault(u => u.Id == id);
            if (unit?.OwnerId != command.IssuerId) return false;
            if (automatic is not null) return unit.CorpsId == automatic.Id;
            return unit.CorpsId is not int corpsId || world.Corps.Any(c => c.Id == corpsId &&
                (c.ControlMode == CorpsControlMode.Direct || seat.Kind == ControllerKind.Automatic && c.Treasury is null));
        }
        bool permitted = command switch
        {
            WaitCommand => true,
            SelectTacticCommand tactic => world.Officers.FirstOrDefault(p => p.Id == tactic.OfficerId)?.UnitId is int unit
                ? OwnUnit(unit) : OwnPerson(tactic.OfficerId),
            DeployUnitCommand deploy => OwnPlace(deploy.SettlementId),
            SetUnitPoliciesCommand policies => OwnUnit(policies.UnitId),
            DemolishFacilityCommand demolish => OwnPlace(demolish.SettlementId),
            ConstructFacilityCommand construct => OwnPlace(construct.SettlementId),
            OrderUnitCommand order => OwnUnit(order.UnitId),
            ReformUnitCommand reform => OwnUnit(reform.UnitId),
            DevelopCommand develop => develop.Development is not null && OwnPlace(develop.Development.SettlementId),
            RecruitOfficerCommand recruit => OwnPerson(recruit.OfficerId),
            BurnCommand burn => OwnPerson(burn.OfficerId),
            AlienateCommand alienate => OwnPerson(alienate.OfficerId),
            SeizeCommand seize => OwnPerson(seize.OfficerId),
            GossipCommand gossip => OwnPerson(gossip.OfficerId),
            GiftCommand gift => OwnPerson(gift.OfficerId),
            ReturnPrisonerCommand prisoner => OwnPerson(prisoner.OfficerId),
            RequestAttackCommand request => OwnPerson(request.OfficerId),
            DemandSurrenderCommand surrender => OwnPerson(surrender.OfficerId),
            UnitStrategyCommand strategy => OwnPerson(strategy.OfficerId),
            SearchCommand search => OwnPerson(search.OfficerId),
            MoveOfficersCommand move => !move.OfficerIds.IsDefault && move.OfficerIds.All(OwnPerson) && OwnPlace(move.DestinationId),
            _ => automatic is null || automatic.Treasury is null
        };
        return permitted ? CommandRejection.None : CommandRejection.NotActiveController;
    }
}
