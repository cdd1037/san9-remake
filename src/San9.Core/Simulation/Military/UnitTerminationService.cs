using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum UnitTerminationReason { Defeated, ForceEliminated, MoraleCollapsed, Surrendered }

public sealed record UnitTerminatedEvent(GameClock Clock, int UnitId, BattleSource? Winner, long UnrecoveredSoldiers) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired] public required UnitTerminationReason Reason { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public required BattleObjectContext Target { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public BattleObjectContext? Source { get; init; }
}

/// <summary>Final resource and identity commit after personnel outcomes have been settled by the battle transaction.</summary>
internal static class UnitTerminationService
{
    internal static void Complete(WorldMutation world, int unitId, BattleSource? winner, int? associatedCityId,
        GovernanceRulebook governance, List<DomainEvent> events, bool repairCorps = true, BattleObjectContext? target = null)
    {
        if (!world.Units.TryGetValue(unitId, out var defeated) || world.Officers.Values.Any(p => p.UnitId == unitId))
            throw new InvalidOperationException("Unit termination requires resolved personnel and a live unit.");
        if (winner is { } captor && (!captor.IsCombatant(world) || captor.Owner(world) == defeated.OwnerId))
            throw new ArgumentException("Invalid battle winner.");
        SettlementState? city = associatedCityId is int cityId && world.Settlements.TryGetValue(cityId, out var place) &&
            place.Kind == SettlementKind.City ? place : null;
        if (associatedCityId is not null && city is null) throw new ArgumentException("Invalid associated city.");
        SettlementState? home = defeated.ServicePlaceId is int homeId && world.Settlements.TryGetValue(homeId, out var origin) &&
            origin.OwnerId == defeated.OwnerId && origin.CanHostPersons && origin.Durability > 0 ? origin : null;
        long pool = (long)defeated.Soldiers + defeated.Wounded;
        long recovered = 0;
        if (winner is { } victor)
        {
            TransferTreasury(world, defeated, victor, pool, events);
            long half = pool / 2;
            if (home is not null) recovered += Wounded(world, home, half, unitId, events);
            uint wounded = victor.IsSettlement ? world.Settlements[victor.Id].Wounded : world.Units[victor.Id].Wounded;
            uint accepted = (uint)Math.Min(half, MilitaryRulebook.WoundedCapacity - wounded);
            victor.Reward(world, accepted, 10);
            ResourceAccounts.Report(world, victor.Account, ResourceKind.Wounded,
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
                Demobilization.Return(world, city.Id, population, "unit-defeated", unitId, events);
                recovered += population;
            }
        }
        Remove(world, defeated, winner, "unit-defeated", events);
        if (repairCorps && defeated.CorpsId is int corps && GovernanceMaintenance.RepairCorps(world, corps, governance, events) != CommandRejection.None)
            throw new InvalidOperationException("Battle transaction cannot settle the defeated corps.");
        events.Add(new UnitTerminatedEvent(world.Clock, unitId, winner, pool - recovered)
        {
            Reason = winner is null && defeated.Morale == 0 ? UnitTerminationReason.MoraleCollapsed : UnitTerminationReason.Defeated,
            Target = target ?? BattleObjectContext.Capture(defeated),
            Source = winner is { } source ? BattleObjectContext.Capture(world, source) : null
        });
    }

    internal static void Demobilize(WorldMutation world, int unitId, int? cityId, List<DomainEvent> events)
    {
        if (!world.Units.TryGetValue(unitId, out var unit) || world.Officers.Values.Any(p => p.UnitId == unitId))
            throw new InvalidOperationException("Demobilization requires resolved personnel and a live unit.");
        long pool = (long)unit.Soldiers + unit.Wounded;
        if (cityId is int city) Demobilization.Return(world, city, pool, "force-eliminated", unitId, events);
        Remove(world, unit, null, "force-eliminated", events);
        events.Add(new UnitTerminatedEvent(world.Clock, unitId, null, cityId is null ? pool : 0)
        {
            Target = BattleObjectContext.Capture(unit), Source = null, Reason = UnitTerminationReason.ForceEliminated
        });
    }

    private static void Remove(WorldMutation world, UnitState defeated, BattleSource? winner, string reason, List<DomainEvent> events)
    {
        int unitId = defeated.Id;
        foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
        {
            uint cargo = resource == ResourceKind.Money ? defeated.CargoMoney : defeated.CargoFood;
            if (winner is { } recipient)
                ResourceAccounts.Change(world, ResourceAccounts.Resolve(world, recipient.Owner(world), recipient.Corps(world)), resource,
                    cargo, "defeated-cargo", unitId, events);
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unitId), resource, "defeated-cargo", winner?.Id,
                -(long)cargo, -(long)cargo, events);
        }
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unitId), ResourceKind.Soldiers, reason, winner?.Id,
            -(long)defeated.Soldiers, -(long)defeated.Soldiers, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unitId), ResourceKind.Wounded, reason, winner?.Id,
            -(long)defeated.Wounded, -(long)defeated.Wounded, events);
        FacilityConstruction.Cancel(world, unitId, events);
        world.RemoveUnit(unitId);
        MilitaryOrders.RemoveTarget(world, unitId, false, events);
    }

    private static uint Wounded(WorldMutation world, SettlementState home, long requested, int unitId, List<DomainEvent> events)
    {
        uint accepted = (uint)Math.Min(requested, MilitaryRulebook.WoundedCapacity - home.Wounded);
        world.Replace(home with { Wounded = home.Wounded + accepted });
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, home.Id), ResourceKind.Wounded,
            "unit-defeated", unitId, requested, accepted, events);
        return accepted;
    }

    private static void TransferTreasury(WorldMutation world, UnitState defeated, BattleSource winner, long weight, List<DomainEvent> events)
    {
        BattleTreasury.Transfer(world, defeated.OwnerId, defeated.CorpsId, weight, winner, new(defeated.Id, false), "battle-treasury", events);
    }
}
