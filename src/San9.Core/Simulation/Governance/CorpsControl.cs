using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Authority follows force ownership and root/delegated membership; assistance is host configuration.</summary>
public static class CorpsControl
{
    public static string ControllerId(int corpsId) => $"corps:{corpsId}";

    public static bool PlayerOperates(CorpsState corps, CompanionSettings companion) =>
        corps.IsRoot || companion.AllowDelegatedCorpsOrders;

    public static bool CanControlSettlement(WorldSnapshot world, ControlSeat seat, CommandContext context, int settlementId,
        CompanionSettings? companion = null)
    {
        var settings = companion ?? CompanionSettings.Default;
        var automatic = AutomaticCorps(world, seat, context, settings);
        if (automatic is null && seat.ControllerId != context.ControllerId) return false;
        return OwnPlace(world, seat, automatic, settlementId, settings);
    }

    public static bool CanControlUnit(WorldSnapshot world, ControlSeat seat, CommandContext context, int unitId,
        CompanionSettings? companion = null)
    {
        var settings = companion ?? CompanionSettings.Default;
        var automatic = AutomaticCorps(world, seat, context, settings);
        if (automatic is null && seat.ControllerId != context.ControllerId) return false;
        return OwnUnit(world, seat, automatic, unitId, settings);
    }

    private static CorpsState? AutomaticCorps(WorldSnapshot world, ControlSeat seat, CommandContext context, CompanionSettings companion) =>
        world.Corps.FirstOrDefault(c => c.OwnerId == seat.ForceId && ControllerId(c.Id) == context.ControllerId &&
            (!c.IsRoot || seat.Kind == ControllerKind.Automatic || companion.PlayerCorpsAutomation == PlayerCorpsAutomationMode.Full));

    private static bool OwnPlace(WorldSnapshot world, ControlSeat seat, CorpsState? automatic, int id, CompanionSettings companion)
    {
        var place = world.Settlements.FirstOrDefault(s => s.Id == id);
        return place?.OwnerId == seat.ForceId && OwnCorps(world, seat, automatic, place.CorpsId, companion);
    }

    private static bool OwnUnit(WorldSnapshot world, ControlSeat seat, CorpsState? automatic, int id, CompanionSettings companion)
    {
        var unit = world.Units.FirstOrDefault(u => u.Id == id);
        return unit?.OwnerId == seat.ForceId && OwnCorps(world, seat, automatic, unit.CorpsId, companion);
    }

    private static bool OwnCorps(WorldSnapshot world, ControlSeat seat, CorpsState? automatic, int? corpsId, CompanionSettings companion)
    {
        // An omitted reference denotes the force's root.
        if (automatic is not null) return corpsId is int assigned ? assigned == automatic.Id : automatic.IsRoot;
        return corpsId is null || world.Corps.Any(c => c.Id == corpsId && c.OwnerId == seat.ForceId &&
            (c.IsRoot || seat.Kind == ControllerKind.Player && companion.AllowDelegatedCorpsOrders));
    }

    internal static CommandRejection Authorize(WorldSnapshot world, ControlSeat seat, CommandContext context, GameCommand command,
        CompanionSettings? companion = null)
    {
        var settings = companion ?? CompanionSettings.Default;
        if (seat.ForceId != command.IssuerId) return CommandRejection.NotActiveController;
        var automatic = AutomaticCorps(world, seat, context, settings);
        if (automatic is null && seat.ControllerId != context.ControllerId) return CommandRejection.NotActiveController;
        if (automatic is null && !world.Corps.Any(c => c.OwnerId == command.IssuerId)) return CommandRejection.None;
        if (command is SetCorpsDelegationCommand)
            return automatic is null ? CommandRejection.None : CommandRejection.NotActiveController;
        bool ControlsPlace(int id) => OwnPlace(world, seat, automatic, id, settings);
        bool OwnPerson(int id) => world.Officers.FirstOrDefault(p => p.Id == id)?.SettlementId is int home && ControlsPlace(home);
        // The ruler retains force-wide personnel transfers across delegated corps.
        bool TransferPlace(int id) => automatic is null && seat.Kind == ControllerKind.Player
            ? world.Settlements.FirstOrDefault(s => s.Id == id)?.OwnerId == command.IssuerId : ControlsPlace(id);
        bool TransferPerson(int id) => world.Officers.FirstOrDefault(p => p.Id == id) is { } person &&
            person.OwnerId == command.IssuerId && person.SettlementId is int home && TransferPlace(home);
        bool ControlsUnit(int id) => OwnUnit(world, seat, automatic, id, settings) &&
            (automatic is null || seat.Kind != ControllerKind.Player || !world.Units.Single(u => u.Id == id).CommandedThisXun);
        bool permitted = command switch
        {
            WaitCommand => true,
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
            MoveOfficersCommand move => !move.OfficerIds.IsDefault && move.OfficerIds.All(TransferPerson) && TransferPlace(move.DestinationId),
            _ => automatic is null || automatic.IsRoot
        };
        return permitted ? CommandRejection.None : CommandRejection.NotActiveController;
    }
}
