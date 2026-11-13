using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ArmamentAssetsTransferredEvent(GameClock Clock, int ForceId, int? SuccessorId,
    bool Transferred) : DomainEvent;

/// <summary>Defines territorial survival and closes force identities after their personnel, assets and corps are settled.</summary>
internal static class ForceLifecycle
{
    internal static bool HasCity(WorldMutation world, int forceId) => world.Settlements.Values.Any(s =>
        s.OwnerId == forceId && s.Kind == SettlementKind.City && !s.IsDestroyed && !s.IsDormant);

    internal static void Retire(WorldMutation world, int forceId, List<DomainEvent> events, int? successor = null)
    {
        MilitaryResearch.CancelForce(world, forceId, events);
        ArmamentProduction.CancelForce(world, forceId, events);
        int? beneficiary = ValidSuccessor(world, forceId, successor) ? successor : null;
        foreach (var escrow in world.Forces.Values.Where(f => f.ArmamentEscrowSuccessorId == forceId).OrderBy(f => f.Id).ToArray())
            world.Replace(escrow with { ArmamentEscrowSuccessorId = beneficiary });
        TransferArmamentAssets(world, forceId, beneficiary, events, reportFailure: true);
        EquipmentOwnership.ForceRetired(world, forceId, successor, events);
        // Loyalty belongs to a live force identity, not to its former ruler or historical record.
        // Settle transfers first: surrender and consolidation still need the old loyalty values.
        foreach (var person in world.AllOfficers.Values.Where(p => p.ForceLoyalties.ContainsKey(forceId)).OrderBy(p => p.Id).ToArray())
        {
            world.Replace(person with { ForceLoyalties = person.ForceLoyalties.Remove(forceId) });
            if (person.LoyaltyTo(forceId) != world.DefaultLoyalty)
                events.Add(new OfficerLoyaltyChangedEvent(world.Clock, person.Id, forceId, person.LoyaltyTo(forceId), world.DefaultLoyalty, "force-retired"));
        }
        AllianceDiplomacy.ForceEliminated(world, forceId, events);
        foreach (var relation in world.Relations.Values.Where(r => r.FirstId == forceId || r.SecondId == forceId).ToArray()) world.Replace(relation with { Value = 0 });
        world.Replace(world.Forces[forceId] with { IsEliminated = true, LeaderId = null, AdviserId = null, CountryTitleId = null });
        var controls = world.Simulation.Controls.Where(c => c.ForceId != forceId).ToImmutableArray();
        bool playerControlled = world.Simulation.Controls.Any(c => c.ForceId == forceId && c.Kind == ControllerKind.Player);
        foreach (var seat in world.Simulation.Controls.Where(c => c.ForceId == forceId))
            events.Add(new ControlEndedEvent(seat.ControllerId, forceId));
        ControlRotation.Replace(world, controls);
        events.Add(new ForceEliminatedEvent(world.Clock, forceId, playerControlled));
        AttackRequests.Cleanup(world, events);
    }

    private static bool ValidSuccessor(WorldMutation world, int forceId, int? successor) =>
        successor is int id && id != forceId && world.Forces.TryGetValue(id, out var target) && !target.IsEliminated && !target.IsDormant;

    /// <summary>Retry held assets when the successor has made room; no repeated failure events.</summary>
    internal static void RetryArmamentTransfers(WorldMutation world, List<DomainEvent> events)
    {
        foreach (var force in world.Forces.Values.Where(f => !f.IsEliminated && f.ArmamentReserve is { IsEmpty: false }).OrderBy(f => f.Id).ToArray())
        {
            var (inventory, reserve) = ArmamentInventory.ReleaseReserve(force.ArmamentInventory ?? ImmutableDictionary<string, uint>.Empty, force.ArmamentReserve!);
            world.Replace(force with { ArmamentInventory = inventory.IsEmpty ? null : inventory, ArmamentReserve = reserve.IsEmpty ? null : reserve });
        }
        foreach (var force in world.Forces.Values.Where(f => f.IsEliminated && f.ArmamentEscrowSuccessorId is not null).OrderBy(f => f.Id).ToArray())
            TransferArmamentAssets(world, force.Id, force.ArmamentEscrowSuccessorId, events, reportFailure: false);
    }

    private static void TransferArmamentAssets(WorldMutation world, int forceId, int? successor, List<DomainEvent> events, bool reportFailure)
    {
        var source = world.Forces[forceId];
        var stock = source.ArmamentInventory;
        var reserve = source.ArmamentReserve;
        long credit = source.ArmamentMigrationCredit ?? 0;
        if ((stock is null || stock.IsEmpty) && (reserve is null || reserve.IsEmpty) && credit == 0) return;

        if (successor is not int targetId || targetId == forceId ||
            !world.Forces.TryGetValue(targetId, out var target) || target.IsEliminated || target.IsDormant ||
            credit > long.MaxValue - (target.ArmamentMigrationCredit ?? 0) ||
            reserve is not null && reserve.Any(entry => entry.Value > ulong.MaxValue - (target.ArmamentReserve?.GetValueOrDefault(entry.Key) ?? 0)) ||
            stock is not null && stock.Any(entry => !ArmamentCapacity.CanAdd(world, target, entry.Key, entry.Value)))
        {
            // Retain the full assets until the designated successor has sufficient capacity.
            world.Replace(source with { ArmamentEscrowSuccessorId = ValidSuccessor(world, forceId, successor) ? successor : null });
            if (reportFailure) events.Add(new ArmamentAssetsTransferredEvent(world.Clock, forceId, successor, false));
            return;
        }

        var inventory = target.ArmamentInventory ?? ImmutableDictionary<string, uint>.Empty;
        if (stock is not null)
            foreach (var (id, count) in stock.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                inventory = inventory.SetItem(id, checked(inventory.GetValueOrDefault(id) + count));
        var targetReserve = target.ArmamentReserve ?? ImmutableDictionary<string, ulong>.Empty;
        if (reserve is not null)
            foreach (var (id, count) in reserve.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                targetReserve = targetReserve.SetItem(id, checked(targetReserve.GetValueOrDefault(id) + count));
        world.Replace(target with { ArmamentInventory = inventory.IsEmpty ? null : inventory,
            ArmamentReserve = targetReserve.IsEmpty ? null : targetReserve,
            ArmamentMigrationCredit = credit == 0 ? target.ArmamentMigrationCredit : (target.ArmamentMigrationCredit ?? 0) + credit });
        world.Replace(source with { ArmamentInventory = null, ArmamentReserve = null, ArmamentMigrationCredit = null, ArmamentEscrowSuccessorId = null });
        events.Add(new ArmamentAssetsTransferredEvent(world.Clock, forceId, targetId, true));
    }
}
