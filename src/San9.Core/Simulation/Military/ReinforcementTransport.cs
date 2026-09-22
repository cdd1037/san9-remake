using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Owns the living troops carried by an outbound or returning personnel task.</summary>
internal static class ReinforcementTransport
{
    internal static int? Corps(WorldMutation world, PersistentTask task) =>
        world.Settlements.TryGetValue(task.OriginSettlementId, out var home) &&
        !home.IsDestroyed && home.OwnerId == task.ForceId ? home.CorpsId : null;

    internal static int MixMorale(uint soldiers, int morale, uint incoming, int incomingMorale) =>
        incoming == 0 ? morale : (int)(((long)soldiers * morale + (long)incoming * incomingMorale) /
            ((long)soldiers + incoming));

    internal static void Disband(WorldMutation world, PersistentTask task, string reason, List<DomainEvent> events)
    {
        if (task.Reinforcements is not { } troops) return;
        Demobilization.Return(world, troops.DemobilizationCityId, (long)troops.Soldiers + troops.Wounded, reason, task.OfficerId, events);
    }

    internal static void Arrive(WorldMutation world, PersistentTask task, PersonnelRulebook places,
        List<DomainEvent> events)
    {
        if (task.Reinforcements is not { } troops) return;
        var home = world.Settlements[task.OriginSettlementId];
        uint accepted = Math.Min(troops.Soldiers, home.SoldierCapacity - Math.Min(home.Soldiers, home.SoldierCapacity));
        world.Replace(home with { Soldiers = home.Soldiers + accepted,
            Morale = MixMorale(home.Soldiers, home.Morale, accepted, troops.Morale) });
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, home.Id), ResourceKind.Soldiers,
            "reinforcement-return", task.OfficerId, accepted, accepted, events);
        uint wounded = Math.Min(troops.Wounded, MilitaryRulebook.WoundedCapacity - home.Wounded);
        world.Replace(world.Settlements[home.Id] with { Wounded = home.Wounded + wounded });
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, home.Id), ResourceKind.Wounded,
            "reinforcement-return", task.OfficerId, troops.Wounded, wounded, events);
        Demobilization.Return(world, Demobilization.City(world, home.Id, places) ?? troops.DemobilizationCityId,
            (long)troops.Soldiers - accepted + troops.Wounded - wounded, "reinforcement-return-overflow", task.OfficerId, events);
    }
}
