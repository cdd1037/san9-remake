using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record DeployUnitCommand(Guid CommandId, int IssuerId, long Sequence, int SettlementId,
    ImmutableArray<int> OfficerIds, uint Soldiers, int FormationId, MilitaryOrder Order,
    uint CargoMoney = 0, uint CargoFood = 0) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record OrderUnitCommand(Guid CommandId, int IssuerId, long Sequence, int UnitId, MilitaryOrder Order)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record UnitDeployedEvent(GameClock Clock, int UnitId, int SettlementId, ImmutableArray<int> OfficerIds) : DomainEvent;
public sealed record UnitOrderChangedEvent(GameClock Clock, int UnitId, MilitaryOrder? Order, string Reason) : DomainEvent;
public sealed record UnitMovedEvent(GameClock Clock, int UnitId, WorldPosition From, WorldPosition To, int Spent, bool Reachable) : DomainEvent;
public sealed record UnitAbsorbedEvent(GameClock Clock, int UnitId, int SettlementId) : DomainEvent;

internal static class MilitaryMovement
{
    internal static void Validate(WorldSnapshot world, MilitaryRulebook? rules)
    {
        foreach (var unit in world.Units)
        {
            if (rules is null)
            {
                if (unit.FormationId is not null || unit.Order is not null) throw new ArgumentException("Missing military content.");
                continue;
            }
            if (unit.FormationId is not int id || !rules.Formations.TryGetValue(id, out var formation) ||
                unit.Soldiers > formation.SoldierCapacity || world.Officers.Count(p => p.UnitId == unit.Id) > formation.OfficerCapacity ||
                rules.Map.PassageCost(unit.Position, formation.Footprint, formation.TravelMode, []) is null)
                throw new ArgumentException("Invalid unit formation, location or capacity.");
        }
    }

    internal static CommandRejection Deploy(WorldMutation world, DeployUnitCommand command, MilitaryRulebook? rules,
        GovernanceRulebook governance, List<DomainEvent> events)
    {
        if (rules is null || !rules.Formations.TryGetValue(command.FormationId, out var formation))
            return CommandRejection.UnsupportedCommand;
        if (!world.Settlements.TryGetValue(command.SettlementId, out var home) || home.OwnerId != command.IssuerId ||
            !home.CanHostPersons || home.Durability == 0 || !rules.Places.TryGetValue(home.Id, out var place))
            return CommandRejection.NoDestination;
        if (command.Soldiers == 0 || command.Soldiers > home.Soldiers || command.Soldiers > formation.SoldierCapacity ||
            command.OfficerIds.IsDefaultOrEmpty || command.OfficerIds.Length > formation.OfficerCapacity ||
            command.OfficerIds.Distinct().Count() != command.OfficerIds.Length)
            return CommandRejection.InvalidTask;
        if (command.OfficerIds.Any(id => !PersonnelMissions.CanAct(world, command.IssuerId, id) || world.Officers[id].SettlementId != home.Id))
            return CommandRejection.PersonUnavailable;
        if (!ValidOrder(world, command.IssuerId, command.Order, formation, rules)) return CommandRejection.InvalidTask;
        if (command.Order.Kind != MilitaryOrderKind.Transport && (command.CargoMoney != 0 || command.CargoFood != 0))
            return CommandRejection.InvalidTask;
        var account = ResourceAccounts.Resolve(world, command.IssuerId, home.CorpsId);
        if ((ulong)command.CargoMoney + (uint)formation.DeploymentCost > ResourceAccounts.Balance(world, account, ResourceKind.Money) ||
            command.CargoFood > ResourceAccounts.Balance(world, account, ResourceKind.Food)) return CommandRejection.InsufficientFunds;
        var occupancy = Occupancy(world, rules, null, null).ToArray();
        WorldPosition? start = Exits(place, formation.Footprint).Cast<WorldPosition?>().FirstOrDefault(p =>
            rules.Map.PassageCost(p!.Value, formation.Footprint, formation.TravelMode, occupancy) is not null);
        if (start is null) return CommandRejection.NoDestination;
        if (world.Simulation.NextUnitId > int.MaxValue) return CommandRejection.ResourceOverflow;
        int id = (int)world.Simulation.NextUnitId;
        var unit = new UnitState(id, home.OwnerId!.Value, start.Value, command.Soldiers)
        {
            CorpsId = home.CorpsId, ServicePlaceId = home.Id, FormationId = formation.Id, Morale = home.Morale,
            CargoMoney = command.CargoMoney, CargoFood = command.CargoFood, Order = command.Order,
            OrderClass = UnitOrderClass.Military
        };
        world.AddUnit(unit);
        world.SetSimulation(world.Simulation with { NextUnitId = world.Simulation.NextUnitId + 1 });
        world.Replace(home with { Soldiers = home.Soldiers - unit.Soldiers, Morale = home.Soldiers == unit.Soldiers ? 0 : home.Morale });
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, home.Id), ResourceKind.Soldiers,
            "deploy", unit.Id, -(long)unit.Soldiers, -(long)unit.Soldiers, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Soldiers,
            "deploy", home.Id, unit.Soldiers, unit.Soldiers, events);
        ResourceAccounts.Change(world, account, ResourceKind.Money, -formation.DeploymentCost, "formation-deployment", unit.Id, events, true);
        ResourceAccounts.Change(world, account, ResourceKind.Money, -(long)unit.CargoMoney, "transport-load", unit.Id, events, true);
        ResourceAccounts.Change(world, account, ResourceKind.Food, -(long)unit.CargoFood, "transport-load", unit.Id, events, true);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Money, "transport-load", home.Id, unit.CargoMoney, unit.CargoMoney, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Food, "transport-load", home.Id, unit.CargoFood, unit.CargoFood, events);
        foreach (int officer in command.OfficerIds)
            world.Replace(world.Officers[officer] with { SettlementId = null, UnitId = unit.Id, Acted = true, IsCommander = false });
        GovernanceMaintenance.RepairCommander(world, home.Id, governance, events);
        events.Add(new UnitDeployedEvent(world.Clock, unit.Id, home.Id, command.OfficerIds));
        return CommandRejection.None;
    }

    internal static CommandRejection Order(WorldMutation world, OrderUnitCommand command, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        if (rules is null) return CommandRejection.UnsupportedCommand;
        if (!world.Units.TryGetValue(command.UnitId, out var unit) || unit.OwnerId != command.IssuerId ||
            unit.FormationId is not int id || !rules.Formations.TryGetValue(id, out var formation)) return CommandRejection.InvalidTask;
        if (!ValidOrder(world, unit.OwnerId, command.Order, formation, rules)) return CommandRejection.InvalidTask;
        world.Replace(unit with { Order = command.Order });
        events.Add(new UnitOrderChangedEvent(world.Clock, unit.Id, command.Order, "ordered"));
        return CommandRejection.None;
    }

    private static bool ValidOrder(WorldMutation world, int owner, MilitaryOrder? order, FormationRule formation, MilitaryRulebook rules)
    {
        if (order is null || !Enum.IsDefined(order.Kind)) return false;
        if (order.Kind == MilitaryOrderKind.Move)
            return order.SettlementId is null && order.Position is WorldPosition p &&
                rules.Map.PassageCost(p, formation.Footprint, formation.TravelMode, []) is not null;
        return order.Position is null && order.SettlementId is int target &&
            world.Settlements.TryGetValue(target, out var destination) && destination.OwnerId == owner &&
            destination.CanHostPersons && destination.Durability > 0 && rules.Places.TryGetValue(target, out var place) &&
            rules.Map.PassageCost(place.Position, formation.Footprint, formation.TravelMode, []) is not null;
    }

    internal static void AdvanceDay(WorldMutation world, MilitaryRulebook? rules, GovernanceRulebook governance, List<DomainEvent> events)
    {
        if (rules is null) return;
        foreach (int id in world.Units.Keys.Order().ToArray())
        {
            var unit = world.Units[id];
            if (unit.Order is not { } order) continue;
            var formation = rules.Formations[unit.FormationId!.Value];
            if (!ValidOrder(world, unit.OwnerId, order, formation, rules))
            {
                MilitaryOrder? fallback = unit.ServicePlaceId is int home && home != order.SettlementId
                    ? new(MilitaryOrderKind.Return, null, home) : null;
                if (!ValidOrder(world, unit.OwnerId, fallback, formation, rules)) fallback = null;
                world.Replace(unit with { Order = fallback });
                events.Add(new UnitOrderChangedEvent(world.Clock, id, fallback, "target-invalid"));
                continue;
            }
            WorldPosition target = order.Position ?? rules.Places[order.SettlementId!.Value].Position;
            WorldPosition from = unit.Position;
            // The normalized formation budget and terrain costs use the same fine-map units.
            var result = rules.Map.Advance(unit.Position, target, formation.Footprint, formation.TravelMode,
                Occupancy(world, rules, unit.Id, order.SettlementId), formation.MovementBudget);
            unit = unit with { Position = result.Position };
            world.Replace(unit);
            events.Add(new UnitMovedEvent(world.Clock, id, from, result.Position, result.Spent, result.Reachable));
            if (!result.Arrived) continue;
            if (order.SettlementId is int destination) Absorb(world, unit, destination, governance, events);
            else
            {
                world.Replace(unit with { Order = null });
                events.Add(new UnitOrderChangedEvent(world.Clock, id, null, "arrived"));
            }
        }
    }

    private static void Absorb(WorldMutation world, UnitState unit, int destinationId, GovernanceRulebook governance, List<DomainEvent> events)
    {
        var home = world.Settlements[destinationId];
        if (!CanAbsorb(world, unit, home))
        {
            events.Add(new UnitOrderChangedEvent(world.Clock, unit.Id, unit.Order, "absorption-blocked"));
            return;
        }
        uint accepted = Math.Min(home.SoldierCapacity - home.Soldiers, unit.Soldiers);
        uint total = home.Soldiers + accepted;
        int morale = total == 0 ? 0 : (int)(((long)home.Soldiers * home.Morale + (long)accepted * unit.Morale) / total);
        world.Replace(home with { Soldiers = total, Morale = morale });
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, home.Id), ResourceKind.Soldiers, "unit-absorbed", unit.Id, unit.Soldiers, accepted, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Soldiers, "unit-absorbed", home.Id, -(long)unit.Soldiers, -(long)unit.Soldiers, events);
        var account = ResourceAccounts.Resolve(world, unit.OwnerId, home.CorpsId);
        ResourceAccounts.Change(world, account, ResourceKind.Money, unit.CargoMoney, "transport-unload", unit.Id, events);
        ResourceAccounts.Change(world, account, ResourceKind.Food, unit.CargoFood, "transport-unload", unit.Id, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Money, "transport-unload", home.Id, -(long)unit.CargoMoney, -(long)unit.CargoMoney, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Food, "transport-unload", home.Id, -(long)unit.CargoFood, -(long)unit.CargoFood, events);
        foreach (var officer in world.Officers.Values.Where(p => p.UnitId == unit.Id).OrderBy(p => p.Id).ToArray())
            world.Replace(officer with { UnitId = null, SettlementId = home.Id, Acted = true });
        world.RemoveUnit(unit.Id);
        if (unit.CorpsId is int previous && previous != home.CorpsId)
        {
            if (GovernanceMaintenance.RepairCorps(world, previous, governance, events) != CommandRejection.None)
                throw new InvalidOperationException("Validated unit absorption could not settle previous corps.");
        }
        GovernanceMaintenance.RepairCommander(world, home.Id, governance, events);
        events.Add(new UnitAbsorbedEvent(world.Clock, unit.Id, home.Id));
    }

    private static bool CanAbsorb(WorldMutation world, UnitState unit, SettlementState home)
    {
        if (unit.CorpsId is not int previous || previous == home.CorpsId || world.Corps[previous].Treasury is not { } treasury)
            return true;
        bool survivingMembers = world.Officers.Values.Any(p => p.IsServing && p.OwnerId == unit.OwnerId &&
            p.UnitId != unit.Id && PersonnelLocations.Corps(world, p) == previous);
        if (survivingMembers && world.Settlements.Values.Any(s => s.CorpsId == previous && s.Kind == SettlementKind.City)) return true;
        var force = world.Forces[unit.OwnerId];
        bool forceReceivesCargo = ResourceAccounts.Resolve(world, unit.OwnerId, home.CorpsId).Kind == ResourceOwnerKind.Force;
        uint money = forceReceivesCargo ? ResourceAccounts.Project(force.Money, unit.CargoMoney) : force.Money;
        uint food = forceReceivesCargo ? ResourceAccounts.Project(force.Food, unit.CargoFood) : force.Food;
        return (ulong)money + treasury.Money <= uint.MaxValue && (ulong)food + treasury.Food <= uint.MaxValue;
    }

    private static IEnumerable<MapOccupancy> Occupancy(WorldMutation world, MilitaryRulebook rules, int? movingUnit, int? entry)
    {
        foreach (var place in rules.Places.Values.Where(p => p.Id != entry && world.Settlements.ContainsKey(p.Id)))
            yield return new(place.Position, place.Footprint);
        foreach (var unit in world.Units.Values.Where(u => u.Id != movingUnit))
        {
            if (unit.FormationId is not int formation) continue;
            yield return new(unit.Position, rules.Formations[formation].Footprint);
        }
    }

    private static IEnumerable<WorldPosition> Exits(BattlefieldPlace place, Footprint footprint)
    {
        var area = new MapOccupancy(place.Position, place.Footprint);
        for (int x = (int)area.Left; x < area.Right; x++)
            yield return new(x, (int)area.Top - (footprint.Height + 1) / 2);
        for (int y = (int)area.Top; y < area.Bottom; y++)
            yield return new((int)area.Right + footprint.Width / 2, y);
        for (int x = (int)area.Left; x < area.Right; x++)
            yield return new(x, (int)area.Bottom + footprint.Height / 2);
        for (int y = (int)area.Top; y < area.Bottom; y++)
            yield return new((int)area.Left - (footprint.Width + 1) / 2, y);
    }
}
