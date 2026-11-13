using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record StartEquipmentProductionCommand(Guid CommandId, int IssuerId, long Sequence,
    int SettlementId, string DefinitionId) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record PurchaseEquipmentCommand(Guid CommandId, int IssuerId, long Sequence,
    int SettlementId, string DefinitionId) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record EquipmentPurchasedEvent(GameClock Clock, int ForceId, int SettlementId, string DefinitionId, long InstanceId, int Price) : DomainEvent;
public sealed record EquipmentProductionStartedEvent(GameClock Clock, int ForceId, int SettlementId, string DefinitionId, int Days) : DomainEvent;
public sealed record EquipmentProductionCompletedEvent(GameClock Clock, int ForceId, int SettlementId, string DefinitionId, long InstanceId) : DomainEvent;
public sealed record EquipmentProductionCancelledEvent(GameClock Clock, int ForceId, int SettlementId, string DefinitionId) : DomainEvent;

internal static class EquipmentProduction
{
    internal static CommandRejection Purchase(WorldMutation world, PurchaseEquipmentCommand command, EquipmentRulebook rules,
        List<DomainEvent> events)
    {
        if (world.Simulation.NewGame.EquipmentDevelopmentEnabled == false || !world.EquipmentInitialized || string.IsNullOrWhiteSpace(command.DefinitionId) ||
            !rules.Items.TryGetValue(command.DefinitionId, out var item) || item.IsOrdinaryArmament || item.PurchaseCost is not int cost ||
            item.LegacyTreasureId is not null || item.Rarity != EquipmentRarity.Ordinary ||
            !world.Forces.TryGetValue(command.IssuerId, out var force) || force.IsDormant || force.IsEliminated ||
            item.PurchaseResearchProjectId is string project && !force.CompletedResearch.Contains(project) ||
            !world.Settlements.TryGetValue(command.SettlementId, out var place) || place.OwnerId != force.Id ||
            place.IsDormant || place.IsDestroyed || !place.CanHostPersons || world.AllEquipment.ContainsKey(long.MaxValue))
            return CommandRejection.InvalidTask;
        var account = ResourceAccounts.Resolve(world, force.Id, place.CorpsId);
        if (ResourceAccounts.Balance(world, account, ResourceKind.Money) < cost) return CommandRejection.InsufficientFunds;
        if (cost > 0) ResourceAccounts.Change(world, account, ResourceKind.Money, -cost, "equipment-purchase", place.Id, events, true);
        long id = checked(world.AllEquipment.Keys.DefaultIfEmpty(-1).Max() + 1);
        world.SetEquipment(new(id, item.Id, null, force.Id, false));
        events.Add(new EquipmentPurchasedEvent(world.Clock, force.Id, place.Id, item.Id, id, cost));
        return CommandRejection.None;
    }
    internal static CommandRejection Start(WorldMutation world, StartEquipmentProductionCommand command, EquipmentRulebook rules,
        List<DomainEvent> events)
    {
        if (world.Simulation.NewGame.EquipmentDevelopmentEnabled == false || !world.EquipmentInitialized || string.IsNullOrWhiteSpace(command.DefinitionId) ||
            !rules.Items.TryGetValue(command.DefinitionId, out var item) || item.IsOrdinaryArmament || item.ResearchProjectId is not string project ||
            !world.Forces.TryGetValue(command.IssuerId, out var force) || force.IsDormant || force.IsEliminated ||
            force.EquipmentProduction is not null || !force.CompletedResearch.Contains(project) ||
            !world.Settlements.TryGetValue(command.SettlementId, out var place) || place.OwnerId != force.Id ||
            place.IsDormant || place.IsDestroyed || !place.CanHostPersons || world.AllEquipment.ContainsKey(long.MaxValue))
            return CommandRejection.InvalidTask;
        var account = ResourceAccounts.Resolve(world, force.Id, place.CorpsId);
        if (ResourceAccounts.Balance(world, account, ResourceKind.Money) < item.ProductionCost) return CommandRejection.InsufficientFunds;
        if (item.ProductionCost > 0)
            ResourceAccounts.Change(world, account, ResourceKind.Money, -item.ProductionCost, "equipment-production", place.Id, events, true);
        world.Replace(force with { EquipmentProduction = new(item.Id, place.Id, item.ProductionDays) });
        events.Add(new EquipmentProductionStartedEvent(world.Clock, force.Id, place.Id, item.Id, item.ProductionDays));
        return CommandRejection.None;
    }

    internal static void CancelAt(WorldMutation world, int settlementId, List<DomainEvent> events)
    {
        foreach (var force in world.Forces.Values.Where(f => f.EquipmentProduction?.SettlementId == settlementId).OrderBy(f => f.Id).ToArray())
            Cancel(world, force, events);
    }

    internal static void CancelForce(WorldMutation world, int forceId, List<DomainEvent> events)
    {
        if (world.Forces.TryGetValue(forceId, out var force) && force.EquipmentProduction is not null)
            Cancel(world, force, events);
    }

    private static void Cancel(WorldMutation world, ForceState force, List<DomainEvent> events)
    {
        var progress = force.EquipmentProduction!;
        world.Replace(force with { EquipmentProduction = null });
        events.Add(new EquipmentProductionCancelledEvent(world.Clock, force.Id, progress.SettlementId, progress.DefinitionId));
    }

    internal static void AdvanceDay(WorldMutation world, EquipmentRulebook rules, List<DomainEvent> events)
    {
        foreach (var force in world.Forces.Values.Where(f => f.EquipmentProduction is not null).OrderBy(f => f.Id).ToArray())
        {
            var progress = force.EquipmentProduction!;
            if (force.IsEliminated || !world.Settlements.TryGetValue(progress.SettlementId, out var place) ||
                place.OwnerId != force.Id || place.IsDestroyed || place.IsDormant || !place.CanHostPersons ||
                !rules.Items.TryGetValue(progress.DefinitionId, out var definition) || definition.IsOrdinaryArmament || definition.ResearchProjectId is null ||
                !force.CompletedResearch.Contains(definition.ResearchProjectId) || world.AllEquipment.ContainsKey(long.MaxValue))
            {
                Cancel(world, force, events);
                continue;
            }
            if (progress.DaysRemaining > 1)
                world.Replace(force with { EquipmentProduction = progress with { DaysRemaining = progress.DaysRemaining - 1 } });
            else
            {
                long id = checked(world.AllEquipment.Keys.DefaultIfEmpty(-1).Max() + 1);
                world.SetEquipment(new(id, definition.Id, null, force.Id, false));
                world.Replace(force with { EquipmentProduction = null });
                events.Add(new EquipmentProductionCompletedEvent(world.Clock, force.Id, place.Id, definition.Id, id));
            }
        }
    }
}
