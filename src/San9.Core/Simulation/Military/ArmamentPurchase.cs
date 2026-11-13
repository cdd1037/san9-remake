using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record PurchaseArmamentCommand(Guid CommandId, int IssuerId, long Sequence,
    int SettlementId, string DefinitionId, uint Batches) : GameCommand(CommandId, IssuerId, Sequence);

public sealed record ArmamentPurchasedEvent(GameClock Clock, int ForceId, int SettlementId,
    string DefinitionId, uint Batches, long Price) : DomainEvent
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? MigrationCreditUsed { get; init; }
}

internal static class ArmamentPurchase
{
    internal static CommandRejection Execute(WorldMutation world, PurchaseArmamentCommand command,
        MilitaryRulebook? rules, List<DomainEvent> events)
    {
        if (!world.Simulation.NewGame.MixedTroopsEnabled || rules is null || command.Batches == 0 ||
            string.IsNullOrWhiteSpace(command.DefinitionId) || !rules.Armaments.TryGetValue(command.DefinitionId, out var item) ||
            !world.Forces.TryGetValue(command.IssuerId, out var force) || force.IsDormant || force.IsEliminated ||
            item.ResearchProjectId is string project && !force.CompletedResearch.Contains(project) ||
            !world.Settlements.TryGetValue(command.SettlementId, out var place) || place.OwnerId != force.Id ||
            place.IsDormant || place.IsDestroyed || !place.CanHostPersons)
            return CommandRejection.InvalidTask;

        ImmutableDictionary<string, uint> inventory;
        try
        {
            inventory = ArmamentInventory.AddBatches(force.ArmamentInventory ?? ImmutableDictionary<string, uint>.Empty,
                item.Id, command.Batches);
        }
        catch (OverflowException)
        {
            return CommandRejection.InvalidTask;
        }
        if (!ArmamentCapacity.CanAdd(world, force, item.Id, (ulong)command.Batches * ArmamentInventory.SetsPerBatch))
            return CommandRejection.ResourceOverflow;
        long price = (long)item.PurchaseCostPerThousand * command.Batches;
        long creditUsed = Math.Min(force.ArmamentMigrationCredit ?? 0, price);
        long money = price - creditUsed;
        var account = ResourceAccounts.Resolve(world, force.Id, place.CorpsId);
        if (ResourceAccounts.Balance(world, account, ResourceKind.Money) < money) return CommandRejection.InsufficientFunds;
        if (money > 0) ResourceAccounts.Change(world, account, ResourceKind.Money, -money, "armament-purchase", place.Id, events, true);
        world.Replace(force with { ArmamentInventory = inventory,
            ArmamentMigrationCredit = force.ArmamentMigrationCredit is long credit ? credit - creditUsed : null });
        events.Add(new ArmamentPurchasedEvent(world.Clock, force.Id, place.Id, item.Id, command.Batches, price)
            { MigrationCreditUsed = creditUsed == 0 ? null : creditUsed });
        return CommandRejection.None;
    }
}

internal static class ArmamentCapacity
{
    // Reserve room for every set already issued and every batch being manufactured.
    internal static bool CanAdd(WorldMutation world, ForceState force, string definitionId, ulong additional)
    {
        if ((force.ArmamentReserve?.GetValueOrDefault(definitionId) ?? 0) > 0) return false;
        ulong total = (force.ArmamentInventory?.GetValueOrDefault(definitionId) ?? 0) + additional;
        if (force.ArmamentProduction is { } progress && progress.DefinitionId == definitionId)
            total += (ulong)progress.Batches * ArmamentInventory.SetsPerBatch;
        foreach (var unit in world.Units.Values.Where(unit => unit.OwnerId == force.Id))
        foreach (var detachment in unit.Detachments.Values)
        {
            if (detachment.Weapon is { } weapon && weapon.DefinitionId == definitionId) total += weapon.RemainingSets;
            if (detachment.Armor is { } armor && armor.DefinitionId == definitionId) total += armor.RemainingSets;
            if (total > uint.MaxValue) return false;
        }
        return total <= uint.MaxValue;
    }
}
