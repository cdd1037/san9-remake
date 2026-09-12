using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class WoundedRecovery
{
    internal static uint Amount(uint wounded, uint available, int coefficient, int roll, bool engaged)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(coefficient);
        ArgumentOutOfRangeException.ThrowIfNegative(roll);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(roll, 99);
        long amount = (100L * coefficient + roll + Math.Min(1000, wounded / 10)) * (engaged ? 1 : 2);
        return (uint)Math.Min(Math.Min(wounded, available), amount);
    }

    internal static void Apply(WorldMutation world, MilitaryRulebook? military, List<DomainEvent> events)
    {
        if (military is null) return;
        foreach (int id in world.Settlements.Keys.Order().ToArray())
        {
            var home = world.Settlements[id];
            if (home.IsDestroyed || home.Durability <= 0 || home.Wounded == 0 ||
                !military.TryPlace(home, out var place) || place.WoundedRecovery is not int coefficient) continue;
            bool engaged = world.Units.Values.Any(u => u.Order is { Kind: MilitaryOrderKind.AttackSettlement } order &&
                order.SettlementId == id && MilitaryCombat.ValidTarget(world, u.OwnerId, order, military.Formations[u.FormationId!.Value], military) &&
                MilitaryCombat.Area(u, military).DistanceTo(new(place.Position, place.Footprint)) <= military.Formations[u.FormationId!.Value].Attack!.Range);
            int roll = (int)(world.NextRandom("military-wounded-recovery") % 100);
            uint recovered = Amount(home.Wounded, home.SoldierCapacity - home.Soldiers, coefficient, roll, engaged);
            if (recovered == 0) continue;
            uint total = home.Soldiers + recovered;
            int recoveryMorale = home.OwnerId is int owner ? EconomyMath.RecruitMorale(world.Forces[owner].Trust) : 0;
            int morale = (int)(((long)home.Soldiers * home.Morale + (long)recovered * recoveryMorale) / total);
            world.Replace(home with { Soldiers = total, Wounded = home.Wounded - recovered, Morale = morale });
            var account = new ResourceAccount(ResourceOwnerKind.Settlement, id);
            ResourceAccounts.Report(world, account, ResourceKind.Wounded, "wounded-recovery", id, -(long)recovered, -(long)recovered, events);
            ResourceAccounts.Report(world, account, ResourceKind.Soldiers, "wounded-recovery", id, recovered, recovered, events);
        }
    }
}
