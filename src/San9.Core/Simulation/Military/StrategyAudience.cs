using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Participants and target values retained before a strategy changes their identities or resources.</summary>
public sealed record StrategyAudience(int ForceId, int TargetForceId, PersonOccurrence Target, int? CommanderId,
    long Money, long Food, int Morale, uint Soldiers, int Loyalty)
{
    internal static StrategyAudience Capture(WorldMutation world, int force, SettlementState place)
    {
        var account = ResourceAccounts.Resolve(world, place.OwnerId!.Value, place.CorpsId);
        return new(force, place.OwnerId.Value, new(place.OwnerId, place.Id, null),
            world.ServingSettlementMembers(place.Id).FirstOrDefault(p => p.IsCommander)?.Id,
            ResourceAccounts.Root(world, place.OwnerId.Value).Treasury.Money,
            ResourceAccounts.Balance(world, account, ResourceKind.Food), place.Morale, place.Soldiers, 0);
    }

    internal static StrategyAudience Capture(WorldMutation world, int force, OfficerState person) =>
        new(force, person.OwnerId!.Value, PersonOccurrence.Capture(world, person), person.Id, 0, 0, 0, 0, person.Loyalty);

    internal static StrategyAudience Capture(WorldMutation world, int force, UnitState unit) =>
        new(force, unit.OwnerId, new(unit.OwnerId, unit.ServicePlaceId, unit.Position), unit.CommanderId, 0, 0, unit.Morale, unit.Soldiers, 0);
}
