using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ReturnCargo([property: JsonRequired] ResourceKind Resource, [property: JsonRequired] uint Amount);
public sealed record ReturnCargoDeliveredEvent(GameClock Clock, Guid TaskId, int OfficerId, int ForceId,
    int DestinationId, int? CorpsId, ResourceKind Resource, uint Amount, uint Received) : DomainEvent;
public sealed record ReturnCargoLostEvent(GameClock Clock, Guid TaskId, int OfficerId, int ForceId,
    ResourceKind Resource, uint Amount, string Reason) : DomainEvent;

internal static class ReturningResources
{
    internal static ReturnCargo? From(PersistentTask task) => task.ReturnCargo ?? (task.Gift is { Offer.Kind: not GiftKind.Treasure } gift
        ? new(gift.Offer.Kind == GiftKind.Money ? ResourceKind.Money : ResourceKind.Food, gift.Offer.Amount) : null);

    internal static void Lose(WorldMutation world, PersistentTask task, string reason, List<DomainEvent> events)
    {
        if (From(task) is { } cargo)
            events.Add(new ReturnCargoLostEvent(world.Clock, task.Id, task.OfficerId, task.ForceId, cargo.Resource, cargo.Amount, reason));
    }

    internal static void Deliver(WorldMutation world, PersistentTask task, List<DomainEvent> events)
    {
        if (task.ReturnCargo is not { } cargo) return;
        var account = ResourceAccounts.Resolve(world, task.ForceId, null);
        uint received = Math.Min(cargo.Amount, ResourceAccounts.Receivable(world, account, cargo.Resource));
        ResourceAccounts.Change(world, account, cargo.Resource, cargo.Amount, "gift-returned", task.OfficerId, events);
        events.Add(new ReturnCargoDeliveredEvent(world.Clock, task.Id, task.OfficerId, task.ForceId, task.OriginSettlementId,
            world.Settlements[task.OriginSettlementId].CorpsId, cargo.Resource, cargo.Amount, received));
    }
}
