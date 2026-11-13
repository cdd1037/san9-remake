using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SetCorpsControlCommand(Guid CommandId, int IssuerId, long Sequence, int CorpsId, CorpsControlMode Mode)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record CorpsControlChangedEvent(GameClock Clock, int ForceId, int CorpsId, CorpsControlMode Mode) : DomainEvent;

public static class CorpsControl
{
    public static string ControllerId(int corpsId) => $"corps:{corpsId}";

    /// <summary>Control scope only; action eligibility and the active turn still require command validation.</summary>
    public static bool CanControlSettlement(WorldSnapshot world, ControlSeat seat, CommandContext context, int settlementId)
    {
        var automatic = AutomaticCorps(world, seat.ForceId, context);
        if (automatic is null && (seat.ControllerId != context.ControllerId || PlayerRootDelegated(world, seat))) return false;
        return OwnPlace(world, seat, automatic, settlementId);
    }

    /// <summary>Control scope only; it does not grant an extra opportunity to issue orders.</summary>
    public static bool CanControlUnit(WorldSnapshot world, ControlSeat seat, CommandContext context, int unitId)
    {
        var automatic = AutomaticCorps(world, seat.ForceId, context);
        if (automatic is null && (seat.ControllerId != context.ControllerId || PlayerRootDelegated(world, seat))) return false;
        return OwnUnit(world, seat, automatic, unitId);
    }

    private static CorpsState? AutomaticCorps(WorldSnapshot world, int forceId, CommandContext context) =>
        world.Corps.FirstOrDefault(c => c.OwnerId == forceId && c.ControlMode == CorpsControlMode.Automatic &&
            ControllerId(c.Id) == context.ControllerId);

    private static bool PlayerRootDelegated(WorldSnapshot world, ControlSeat seat) =>
        seat.Kind == ControllerKind.Player && world.Corps.Any(c => c.OwnerId == seat.ForceId && c.IsRoot &&
            c.ControlMode == CorpsControlMode.Automatic);

    private static bool OwnPlace(WorldSnapshot world, ControlSeat seat, CorpsState? automatic, int id)
    {
        var place = world.Settlements.FirstOrDefault(s => s.Id == id);
        if (place?.OwnerId != seat.ForceId) return false;
        return OwnCorps(world, seat, automatic, place.CorpsId);
    }

    private static bool OwnUnit(WorldSnapshot world, ControlSeat seat, CorpsState? automatic, int id)
    {
        var unit = world.Units.FirstOrDefault(u => u.Id == id);
        return unit?.OwnerId == seat.ForceId && OwnCorps(world, seat, automatic, unit.CorpsId);
    }

    private static bool OwnCorps(WorldSnapshot world, ControlSeat seat, CorpsState? automatic, int? corpsId)
    {
        // An omitted corps reference denotes the force's root, as it does for resource accounts.
        if (automatic is not null) return corpsId is int assigned ? assigned == automatic.Id : automatic.IsRoot;
        return corpsId is null || world.Corps.Any(c => c.Id == corpsId &&
            (c.ControlMode == CorpsControlMode.Direct || seat.Kind == ControllerKind.Automatic && c.IsRoot));
    }

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
        var automatic = AutomaticCorps(world, command.IssuerId, context);
        if (automatic is null && seat.ControllerId != context.ControllerId) return CommandRejection.NotActiveController;
        if (automatic is null && !world.Corps.Any(c => c.OwnerId == command.IssuerId)) return CommandRejection.None;
        if (command is SetCorpsControlCommand or SetCorpsDelegationCommand)
            return automatic is null ? CommandRejection.None : CommandRejection.NotActiveController;
        if (automatic is null && PlayerRootDelegated(world, seat))
            return CommandRejection.NotActiveController;
        bool ControlsPlace(int id) => OwnPlace(world, seat, automatic, id);
        bool OwnPerson(int id) => world.Officers.FirstOrDefault(p => p.Id == id)?.SettlementId is int home && ControlsPlace(home);
        bool ControlsUnit(int id) => OwnUnit(world, seat, automatic, id);
        bool permitted = command switch
        {
            WaitCommand => true,
            StartResearchCommand research => ControlsPlace(research.SettlementId),
            StartFormationTeachingCommand teaching => ControlsPlace(teaching.SettlementId),
            StartEquipmentProductionCommand production => ControlsPlace(production.SettlementId),
            PurchaseEquipmentCommand purchase => ControlsPlace(purchase.SettlementId),
            PurchaseArmamentCommand armamentPurchase => ControlsPlace(armamentPurchase.SettlementId),
            StartArmamentProductionCommand armamentProduction => ControlsPlace(armamentProduction.SettlementId),
            ResupplyUnitArmamentsCommand resupply => ControlsUnit(resupply.UnitId),
            ConfigureUnitArmamentCommand armament => ControlsUnit(armament.UnitId),
            ConfigureEquipmentCommand equipment => world.Officers.FirstOrDefault(p => p.Id == equipment.OfficerId)?.UnitId is int assignedUnit
                ? ControlsUnit(assignedUnit) : OwnPerson(equipment.OfficerId),
            TransferEquipmentCommand transfer => (transfer.OfficerId is null || OwnPerson(transfer.OfficerId.Value)) &&
                (world.Equipment.FirstOrDefault(e => e.Id == transfer.EquipmentId)?.OfficerId is not int previous ||
                 (world.Officers.FirstOrDefault(p => p.Id == previous)?.UnitId is int previousUnit ? ControlsUnit(previousUnit) : OwnPerson(previous))),
            ExecutePrisonerCommand execute => world.Officers.FirstOrDefault(p => p.Id == execute.OfficerId)?.UnitId is int detention
                ? ControlsUnit(detention) : OwnPerson(execute.OfficerId),
            SelectTacticCommand tactic => world.Officers.FirstOrDefault(p => p.Id == tactic.OfficerId)?.UnitId is int unit
                ? ControlsUnit(unit) : OwnPerson(tactic.OfficerId),
            DeployUnitCommand deploy => ControlsPlace(deploy.SettlementId),
            TransportUnitCommand transport => ControlsPlace(transport.SettlementId),
            SetUnitPoliciesCommand policies => ControlsUnit(policies.UnitId),
            DemolishFacilityCommand demolish => ControlsPlace(demolish.SettlementId),
            ConstructFacilityCommand construct => ControlsPlace(construct.SettlementId),
            OrderUnitsCommand orders => !orders.Units.IsDefaultOrEmpty && orders.Units.All(u => u is not null && ControlsUnit(u.UnitId)),
            OrderUnitCommand order => ControlsUnit(order.UnitId),
            ReformUnitCommand reform => ControlsUnit(reform.UnitId),
            FoodTradeCommand trade => ControlsPlace(trade.SettlementId),
            DevelopCommand develop => develop.Development is not null && ControlsPlace(develop.Development.SettlementId),
            RecruitOfficersCommand recruit => !recruit.OfficerIds.IsDefault && recruit.OfficerIds.All(OwnPerson),
            BurnCommand burn => OwnPerson(burn.OfficerId),
            AlienateCommand alienate => OwnPerson(alienate.OfficerId),
            SeizeCommand seize => OwnPerson(seize.OfficerId),
            GossipCommand gossip => OwnPerson(gossip.OfficerId),
            GiftCommand gift => OwnPerson(gift.OfficerId),
            ReturnPrisonerCommand prisoner => OwnPerson(prisoner.OfficerId),
            RequestAttackCommand request => OwnPerson(request.OfficerId),
            DemandSurrenderCommand surrender => OwnPerson(surrender.OfficerId),
            UnitStrategyCommand strategy => OwnPerson(strategy.OfficerId),
            SearchCommand search => !search.OfficerIds.IsDefault && search.OfficerIds.All(OwnPerson),
            MoveOfficersCommand move => !move.OfficerIds.IsDefault && move.OfficerIds.All(OwnPerson) && ControlsPlace(move.DestinationId),
            _ => automatic is null || automatic.IsRoot
        };
        return permitted ? CommandRejection.None : CommandRejection.NotActiveController;
    }
}
