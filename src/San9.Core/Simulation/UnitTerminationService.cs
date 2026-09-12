using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record UnitTerminatedEvent(GameClock Clock, int UnitId, int? WinnerId, long UnrecoveredSoldiers) : DomainEvent;

/// <summary>Final resource and identity commit after personnel outcomes have been settled by the battle transaction.</summary>
internal static class UnitTerminationService
{
    internal static void Complete(WorldMutation world, int unitId, int? winnerId, int? associatedCityId,
        GovernanceRulebook governance, List<DomainEvent> events, bool repairCorps = true)
    {
        if (!world.Units.TryGetValue(unitId, out var defeated) || world.Officers.Values.Any(p => p.UnitId == unitId))
            throw new InvalidOperationException("Unit termination requires resolved personnel and a live unit.");
        UnitState? winner = winnerId is int id && world.Units.TryGetValue(id, out var found) ? found : null;
        if (winnerId is not null && (winner is null || winner.OwnerId == defeated.OwnerId))
            throw new ArgumentException("Invalid battle winner.");
        SettlementState? city = associatedCityId is int cityId && world.Settlements.TryGetValue(cityId, out var place) &&
            place.Kind == SettlementKind.City ? place : null;
        if (associatedCityId is not null && city is null) throw new ArgumentException("Invalid associated city.");
        SettlementState? home = defeated.ServicePlaceId is int homeId && world.Settlements.TryGetValue(homeId, out var origin) &&
            origin.OwnerId == defeated.OwnerId && origin.CanHostPersons && origin.Durability > 0 ? origin : null;
        long pool = (long)defeated.Soldiers + defeated.Wounded;
        long recovered = 0;
        if (winner is not null)
        {
            TransferTreasury(world, defeated, winner, pool, events);
            long half = pool / 2;
            if (home is not null) recovered += Wounded(world, home, half, unitId, events);
            uint accepted = (uint)Math.Min(half, MilitaryRulebook.WoundedCapacity - winner.Wounded);
            world.Replace(winner with { Wounded = winner.Wounded + accepted, Morale = Math.Min(100, winner.Morale + 10) });
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, winner.Id), ResourceKind.Wounded,
                "unit-defeated", unitId, half, accepted, events);
            recovered += accepted;
        }
        else
        {
            long population = home is null ? pool : pool / 2;
            if (home is not null) recovered += Wounded(world, home, population, unitId, events);
            if (city is not null)
            {
                // Home and associated city can be the same object; read its latest state.
                city = world.Settlements[city.Id];
                uint accepted = (uint)Math.Min(population, 500000 - city.DraftPopulation);
                world.Replace(city with { DraftPopulation = city.DraftPopulation + accepted });
                ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, city.Id), ResourceKind.DraftPopulation,
                    "unit-defeated", unitId, population, accepted, events);
                recovered += accepted;
            }
        }
        foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
        {
            uint cargo = resource == ResourceKind.Money ? defeated.CargoMoney : defeated.CargoFood;
            if (winner is not null)
                ResourceAccounts.Change(world, ResourceAccounts.Resolve(world, winner.OwnerId, winner.CorpsId), resource,
                    cargo, "defeated-cargo", unitId, events);
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unitId), resource, "defeated-cargo", winnerId,
                -(long)cargo, -(long)cargo, events);
        }
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unitId), ResourceKind.Soldiers, "unit-defeated", winnerId,
            -(long)defeated.Soldiers, -(long)defeated.Soldiers, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unitId), ResourceKind.Wounded, "unit-defeated", winnerId,
            -(long)defeated.Wounded, -(long)defeated.Wounded, events);
        world.RemoveUnit(unitId);
        if (repairCorps && defeated.CorpsId is int corps && GovernanceMaintenance.RepairCorps(world, corps, governance, events) != CommandRejection.None)
            throw new InvalidOperationException("Battle transaction cannot settle the defeated corps.");
        events.Add(new UnitTerminatedEvent(world.Clock, unitId, winnerId, pool - recovered));
    }

    private static uint Wounded(WorldMutation world, SettlementState home, long requested, int unitId, List<DomainEvent> events)
    {
        uint accepted = (uint)Math.Min(requested, MilitaryRulebook.WoundedCapacity - home.Wounded);
        world.Replace(home with { Wounded = home.Wounded + accepted });
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, home.Id), ResourceKind.Wounded,
            "unit-defeated", unitId, requested, accepted, events);
        return accepted;
    }

    private static void TransferTreasury(WorldMutation world, UnitState defeated, UnitState winner, long weight, List<DomainEvent> events)
    {
        var source = ResourceAccounts.Resolve(world, defeated.OwnerId, defeated.CorpsId);
        var target = ResourceAccounts.Resolve(world, winner.OwnerId, winner.CorpsId);
        long total = world.Units.Values.Where(u => u.OwnerId == defeated.OwnerId && ResourceAccounts.Resolve(world, u.OwnerId, u.CorpsId) == source)
            .Sum(u => (long)u.Soldiers + u.Wounded) + world.Settlements.Values.Where(s => s.OwnerId == defeated.OwnerId &&
                ResourceAccounts.Resolve(world, defeated.OwnerId, s.CorpsId) == source).Sum(s => (long)s.Soldiers + s.Wounded);
        long ratio = total == 0 ? 250 : weight * 250 / total;
        foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
        {
            long amount = ResourceAccounts.Balance(world, source, resource) * ratio / 500;
            ResourceAccounts.Change(world, source, resource, -amount, "battle-treasury", defeated.Id, events);
            ResourceAccounts.Change(world, target, resource, amount, "battle-treasury", defeated.Id, events);
        }
    }
}
