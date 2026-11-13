using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record EquipmentOwnershipResolvedEvent(GameClock Clock, long EquipmentId,
    int? PreviousForceId, int? PreviousOfficerId, int? RecipientForceId) : DomainEvent;

internal static class EquipmentOwnership
{
    internal static void OfficerDied(WorldMutation world, OfficerState previous, List<DomainEvent> events)
    {
        int? recipient = previous.Status == OfficerStatus.Captive
            ? previous.UnitId is int unit ? world.Units.GetValueOrDefault(unit)?.OwnerId :
                previous.SettlementId is int place ? world.Settlements.GetValueOrDefault(place)?.OwnerId : null
            : previous.OwnerId;
        if (recipient is int id && (!world.Forces.TryGetValue(id, out var force) || force.IsEliminated)) recipient = null;
        foreach (var item in world.AllEquipment.Values.Where(e => e.OfficerId == previous.Id && e.LegacyTreasureId is null).OrderBy(e => e.Id).ToArray())
        {
            if (item.PendingArmamentCredit is not null && recipient is int creditOwner)
            {
                world.ConvertPendingArmament(item, creditOwner);
                events.Add(new EquipmentOwnershipResolvedEvent(world.Clock, item.Id, previous.OwnerId, previous.Id, recipient));
                continue;
            }
            world.SetEquipment(recipient is int forceId ? EquipmentRules.MoveToInventory(item, forceId) :
                item with { OfficerId = null, InventoryForceId = null, Equipped = false, IsLost = true, EquipOnService = null });
            events.Add(new EquipmentOwnershipResolvedEvent(world.Clock, item.Id, previous.OwnerId, previous.Id, recipient));
        }
    }

    internal static void ForceRetired(WorldMutation world, int forceId, int? successor, List<DomainEvent> events)
    {
        if (successor == forceId || successor is int target && (!world.Forces.TryGetValue(target, out var force) || force.IsEliminated))
            successor = null;
        foreach (var item in world.AllEquipment.Values.Where(e => e.InventoryForceId == forceId).OrderBy(e => e.Id).ToArray())
        {
            world.SetEquipment(successor is int id ? EquipmentRules.MoveToInventory(item, id) :
                item with { OfficerId = null, InventoryForceId = null, Equipped = false, IsLost = item.LegacyTreasureId is null, EquipOnService = null });
            events.Add(new EquipmentOwnershipResolvedEvent(world.Clock, item.Id, forceId, null, successor));
        }
    }
}
