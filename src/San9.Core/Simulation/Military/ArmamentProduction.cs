using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record StartArmamentProductionCommand(Guid CommandId, int IssuerId, long Sequence,
    int SettlementId, string DefinitionId, uint Batches) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record ArmamentProductionStartedEvent(GameClock Clock, int ForceId, int SettlementId,
    string DefinitionId, uint Batches, int Days) : DomainEvent;
public sealed record ArmamentProductionCompletedEvent(GameClock Clock, int ForceId, int SettlementId,
    string DefinitionId, uint Batches) : DomainEvent;
public sealed record ArmamentProductionCancelledEvent(GameClock Clock, int ForceId, int SettlementId,
    string DefinitionId, uint Batches) : DomainEvent;

internal static class ArmamentProduction
{
    internal static CommandRejection Start(WorldMutation world, StartArmamentProductionCommand command,
        MilitaryRulebook? rules, List<DomainEvent> events)
    {
        if (!world.Simulation.NewGame.MixedTroopsEnabled || rules is null || command.Batches == 0 ||
            string.IsNullOrWhiteSpace(command.DefinitionId) || !rules.Armaments.TryGetValue(command.DefinitionId, out var item) ||
            item.ProductionCostPerThousand is not int unitCost || item.ProductionDays is not int days ||
            !world.Forces.TryGetValue(command.IssuerId, out var force) || force.IsDormant || force.IsEliminated ||
            force.ArmamentProduction is not null ||
            item.ResearchProjectId is string project && !force.CompletedResearch.Contains(project) ||
            !world.Settlements.TryGetValue(command.SettlementId, out var place) || place.OwnerId != force.Id ||
            place.IsDormant || place.IsDestroyed || !place.CanHostPersons)
            return CommandRejection.InvalidTask;
        try
        {
            _ = ArmamentInventory.AddBatches(force.ArmamentInventory ?? ImmutableDictionary<string, uint>.Empty,
                item.Id, command.Batches);
        }
        catch (OverflowException) { return CommandRejection.InvalidTask; }
        if (!ArmamentCapacity.CanAdd(world, force, item.Id, (ulong)command.Batches * ArmamentInventory.SetsPerBatch))
            return CommandRejection.ResourceOverflow;
        long price = (long)unitCost * command.Batches;
        var account = ResourceAccounts.Resolve(world, force.Id, place.CorpsId);
        if (ResourceAccounts.Balance(world, account, ResourceKind.Money) < price) return CommandRejection.InsufficientFunds;
        if (price > 0) ResourceAccounts.Change(world, account, ResourceKind.Money, -price, "armament-production", place.Id, events, true);
        world.Replace(force with { ArmamentProduction = new(item.Id, place.Id, command.Batches, days) });
        events.Add(new ArmamentProductionStartedEvent(world.Clock, force.Id, place.Id, item.Id, command.Batches, days));
        return CommandRejection.None;
    }

    internal static void AdvanceDay(WorldMutation world, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        foreach (var force in world.Forces.Values.Where(f => f.ArmamentProduction is not null).OrderBy(f => f.Id).ToArray())
        {
            var progress = force.ArmamentProduction!;
            if (!world.Simulation.NewGame.MixedTroopsEnabled || force.IsEliminated || rules is null ||
                !rules.Armaments.TryGetValue(progress.DefinitionId, out var item) || item.ProductionDays is null ||
                item.ResearchProjectId is string project && !force.CompletedResearch.Contains(project) ||
                !world.Settlements.TryGetValue(progress.SettlementId, out var place) || place.OwnerId != force.Id ||
                place.IsDestroyed || place.IsDormant || !place.CanHostPersons)
            { Cancel(world, force, events); continue; }
            if (progress.DaysRemaining > 1)
            { world.Replace(force with { ArmamentProduction = progress with { DaysRemaining = progress.DaysRemaining - 1 } }); continue; }
            ImmutableDictionary<string, uint> inventory;
            ImmutableDictionary<string, ulong> reserve;
            try
            {
                uint sets = checked(progress.Batches * ArmamentInventory.SetsPerBatch);
                (inventory, reserve) = ArmamentInventory.ReturnWithReserve(force.ArmamentInventory ?? ImmutableDictionary<string, uint>.Empty,
                    force.ArmamentReserve ?? ImmutableDictionary<string, ulong>.Empty, new(item.Id, sets, sets, 0));
            }
            catch (OverflowException) { Cancel(world, force, events); continue; }
            world.Replace(force with { ArmamentProduction = null, ArmamentInventory = inventory, ArmamentReserve = reserve.IsEmpty ? null : reserve });
            events.Add(new ArmamentProductionCompletedEvent(world.Clock, force.Id, place.Id, item.Id, progress.Batches));
        }
    }

    internal static void CancelAt(WorldMutation world, int settlementId, List<DomainEvent> events)
    {
        foreach (var force in world.Forces.Values.Where(f => f.ArmamentProduction?.SettlementId == settlementId).OrderBy(f => f.Id).ToArray())
            Cancel(world, force, events);
    }

    internal static void CancelForce(WorldMutation world, int forceId, List<DomainEvent> events)
    {
        if (world.Forces.TryGetValue(forceId, out var force) && force.ArmamentProduction is not null) Cancel(world, force, events);
    }

    private static void Cancel(WorldMutation world, ForceState force, List<DomainEvent> events)
    {
        var progress = force.ArmamentProduction!;
        world.Replace(force with { ArmamentProduction = null });
        events.Add(new ArmamentProductionCancelledEvent(world.Clock, force.Id, progress.SettlementId, progress.DefinitionId, progress.Batches));
    }
}
