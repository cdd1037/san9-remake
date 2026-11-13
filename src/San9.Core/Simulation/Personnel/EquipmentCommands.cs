using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Equip one held item, or clear a slot when EquipmentId is null.</summary>
public sealed record ConfigureEquipmentCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId,
    EquipmentSlot Slot, long? EquipmentId) : GameCommand(CommandId, IssuerId, Sequence);

public sealed record EquipmentConfiguredEvent(GameClock Clock, int ForceId, int OfficerId,
    EquipmentSlot Slot, long? EquipmentId) : DomainEvent;
public sealed record TransferEquipmentCommand(Guid CommandId, int IssuerId, long Sequence, long EquipmentId,
    int? OfficerId) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record EquipmentTransferredEvent(GameClock Clock, int ForceId, long EquipmentId,
    int? PreviousOfficerId, int? OfficerId) : DomainEvent;

internal static class EquipmentCommands
{
    internal static CommandRejection Transfer(WorldMutation world, TransferEquipmentCommand command, EquipmentRulebook rules,
        List<DomainEvent> events)
    {
        if (!world.EquipmentInitialized || !world.AllEquipment.TryGetValue(command.EquipmentId, out var item) || item.PendingArmamentCredit is not null ||
            !rules.Items.ContainsKey(item.DefinitionId)) return CommandRejection.InvalidTask;
        if (item.LegacyTreasureId is not null && item.InventoryForceId != command.IssuerId) return CommandRejection.InvalidTask;
        if (item.InventoryForceId != command.IssuerId && (item.OfficerId is not int previous ||
            !world.Officers.TryGetValue(previous, out var owner) || !owner.IsServing || owner.OwnerId != command.IssuerId || world.IsTraveling(previous)))
            return CommandRejection.PersonUnavailable;
        if (command.OfficerId is int target && (!world.Officers.TryGetValue(target, out var person) || !person.IsServing ||
            person.OwnerId != command.IssuerId || world.IsTraveling(target))) return CommandRejection.PersonUnavailable;
        var updated = command.OfficerId is int recipient ? EquipmentRules.MoveToOfficer(item, recipient) : EquipmentRules.MoveToInventory(item, command.IssuerId);
        if (item.LegacyTreasureId is int treasure && command.OfficerId is int holder)
            world.Replace(world.AllTreasures[treasure] with { HolderId = holder, Discovery = TreasureDiscovery.Discovered });
        world.SetEquipment(updated);
        events.Add(new EquipmentTransferredEvent(world.Clock, command.IssuerId, item.Id, item.OfficerId, command.OfficerId));
        return CommandRejection.None;
    }

    internal static CommandRejection Configure(WorldMutation world, ConfigureEquipmentCommand command,
        EquipmentRulebook rules, List<DomainEvent> events)
    {
        if (!Enum.IsDefined(command.Slot) || !world.Officers.TryGetValue(command.OfficerId, out var person) ||
            !person.IsServing || person.OwnerId != command.IssuerId ||
            !EquipmentRules.ValidInventory(world.AllEquipment.Values, rules.Items))
            return CommandRejection.InvalidTask;
        if (command.EquipmentId is long itemId)
        {
            if (!world.AllEquipment.TryGetValue(itemId, out var item) ||
                (item.OfficerId != person.Id && item.InventoryForceId != command.IssuerId) ||
                rules.Items[item.DefinitionId].Slot != command.Slot ||
                !EquipmentRules.MeetsRequirements(rules.Items[item.DefinitionId], person.BaseAbilities))
                return CommandRejection.InvalidTask;
            var proposed = item.OfficerId == person.Id ? world.AllEquipment.Values : world.AllEquipment.Values.Select(value =>
                value.Id == itemId ? EquipmentRules.MoveToOfficer(value, person.Id) : value);
            var updated = EquipmentRules.Equip(proposed, rules.Items, person.Id, itemId);
            if (updated is null) return CommandRejection.InvalidTask;
            if (item.LegacyTreasureId is int treasure && item.OfficerId != person.Id)
                world.Replace(world.AllTreasures[treasure] with { HolderId = person.Id, Discovery = TreasureDiscovery.Discovered });
            foreach (var changed in updated.Value)
                if (changed != world.AllEquipment[changed.Id]) world.SetEquipment(changed);
        }
        else
        {
            var existing = world.AllEquipment.Values.SingleOrDefault(item => item.OfficerId == person.Id && item.Equipped &&
                rules.Items[item.DefinitionId].Slot == command.Slot);
            if (existing is not null) world.SetEquipment(existing with { Equipped = false });
        }
        events.Add(new EquipmentConfiguredEvent(world.Clock, command.IssuerId, person.Id, command.Slot, command.EquipmentId));
        return CommandRejection.None;
    }
}
