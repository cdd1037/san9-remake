using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record DeployUnitCommand(Guid CommandId, int IssuerId, long Sequence, int SettlementId,
    ImmutableArray<int> OfficerIds, uint Soldiers, int FormationId, MilitaryOrder Order,
    uint CargoMoney = 0, uint CargoFood = 0) : GameCommand(CommandId, IssuerId, Sequence)
{
    [JsonRequired] public bool ConfirmAttack { get; init; }
    [JsonRequired] public UnitPolicies Policies { get; init; } = new();
}
public sealed record OrderUnitCommand(Guid CommandId, int IssuerId, long Sequence, int UnitId, MilitaryOrder Order)
    : GameCommand(CommandId, IssuerId, Sequence)
{
    [JsonRequired] public bool ConfirmAttack { get; init; }
}
public sealed record UnitDeployedEvent(GameClock Clock, int UnitId, int SettlementId, ImmutableArray<int> OfficerIds) : DomainEvent;
public sealed record UnitOrderChangedEvent(GameClock Clock, int UnitId, MilitaryOrder? Order, string Reason) : DomainEvent;
public sealed record UnitMovedEvent(GameClock Clock, int UnitId, WorldPosition From, WorldPosition To, int Spent, NavigationStatus Status) : DomainEvent;
public sealed record UnitAbsorbedEvent(GameClock Clock, int UnitId, int SettlementId) : DomainEvent;

internal static class MilitaryMovement
{
    internal static void Validate(WorldSnapshot world, MilitaryRulebook? rules)
    {
        if (world.Officers.Any(p => p.EquippedTacticId is int id && (rules is null || !rules.Tactics.ContainsKey(id))))
            throw new ArgumentException("Unknown equipped tactic.");
        if (rules is not null && world.Settlements.Any(s => s.IsDestroyed &&
            (!rules.TryPlace(s, out var place) || !place.DestroyedAtZeroDurability && !place.CanDemolish)))
            throw new ArgumentException("Settlement definition does not permit destruction.");
        foreach (var unit in world.Units)
        {
            if (unit.CommanderId is int commander && !world.Officers.Any(p => p.Id == commander && p.IsServing && p.UnitId == unit.Id))
                throw new ArgumentException("Unit commander is not a serving member.");
            if (rules is null)
            {
                if (unit.FormationId is not null || unit.Order is not null) throw new ArgumentException("Missing military content.");
                continue;
            }
            if (unit.FormationId is not int id || !rules.Formations.TryGetValue(id, out var formation) ||
                unit.MovementRemainder >= rules.Map.MaximumStepCost(formation.TravelMode) ||
                unit.Soldiers > formation.SoldierCapacity || world.Officers.Count(p => p.UnitId == unit.Id && p.IsServing) > formation.OfficerCapacity ||
                rules.Map.PassageCost(unit.Position, formation.Footprint, formation.TravelMode, []) is null)
                throw new ArgumentException("Invalid unit formation, location or capacity.");
            if (formation.Combat is not null && world.Officers.Any(p => p.IsServing && p.UnitId == unit.Id && !unit.MemberRanks.ContainsKey(p.Id)))
                throw new ArgumentException("Missing battle member rank.");
            if (unit.Order?.Construction is { } plan && (!rules.ConstructionSites.TryGetValue((plan.RegionId, plan.Position), out var site) ||
                !site.FacilityIds.Contains(plan.FacilityTypeId) || !world.Officers.Any(p => p.Id == plan.LeaderId && p.UnitId == unit.Id && p.IsServing)))
                throw new ArgumentException("Invalid construction plan.");
        }
        foreach (var settlement in world.Settlements.Where(s => s.Facility is not null))
        {
            var instance = settlement.Facility!;
            if (rules is null || !rules.Facilities.TryGetValue(instance.TypeId, out var type) ||
                !rules.ConstructionSites.TryGetValue((settlement.Id, instance.Position), out var site) || !site.FacilityIds.Contains(type.Id) ||
                settlement.MaxDurability != type.MaxDurability || settlement.SoldierCapacity != type.SoldierCapacity ||
                settlement.ConsumesFood != type.ConsumesFood || !settlement.IsDestroyed && settlement.CanHostPersons != (instance.BuilderUnitId is null && type.CanHostPersons))
                throw new ArgumentException("Invalid constructed facility state.");
        }
    }

    internal static CommandRejection Deploy(WorldMutation world, DeployUnitCommand command, MilitaryRulebook? rules,
        GovernanceRulebook governance, TreasureRulebook treasures, List<DomainEvent> events, bool construction = false)
    {
        if (rules is null || !rules.Formations.TryGetValue(command.FormationId, out var formation))
            return CommandRejection.UnsupportedCommand;
        int deploymentCost = construction ? 0 : formation.DeploymentCost;
        if (command.Policies is null || command.Order is null || !construction && command.Order.Kind == MilitaryOrderKind.Construct) return CommandRejection.InvalidTask;
        if (!world.Settlements.TryGetValue(command.SettlementId, out var home) || home.OwnerId != command.IssuerId ||
            !home.CanHostPersons || home.Durability == 0 || !rules.TryPlace(home, out var place))
            return CommandRejection.NoDestination;
        if (home.ConfusionDaysRemaining > 0) return CommandRejection.SettlementConfused;
        if (command.Soldiers == 0 || command.Soldiers > home.Soldiers || command.Soldiers > formation.SoldierCapacity ||
            command.OfficerIds.IsDefaultOrEmpty || command.OfficerIds.Length > formation.OfficerCapacity ||
            command.OfficerIds.Distinct().Count() != command.OfficerIds.Length)
            return CommandRejection.InvalidTask;
        if (command.OfficerIds.Any(id => !PersonnelMissions.CanAct(world, command.IssuerId, id) || world.Officers[id].SettlementId != home.Id))
            return CommandRejection.PersonUnavailable;
        if (!FormationEligibility.CanUse(world, command.IssuerId, command.OfficerIds, formation, treasures))
            return CommandRejection.MissingFormationTactic;
        if (!ValidOrder(world, command.IssuerId, command.Order, formation, rules)) return CommandRejection.InvalidTask;
        if (!command.ConfirmAttack && MilitaryDiplomacy.Warning(world, command.IssuerId, command.Order, rules) != AttackWarning.None)
            return CommandRejection.AttackConfirmationRequired;
        if (command.Order.Kind != MilitaryOrderKind.Transport && (command.CargoMoney != 0 || command.CargoFood != 0))
            return CommandRejection.InvalidTask;
        var account = ResourceAccounts.Resolve(world, command.IssuerId, home.CorpsId);
        if ((ulong)command.CargoMoney + (uint)deploymentCost > ResourceAccounts.Balance(world, account, ResourceKind.Money) ||
            command.CargoFood > ResourceAccounts.Balance(world, account, ResourceKind.Food)) return CommandRejection.InsufficientFunds;
        var occupancy = Occupancy(world, rules, null, null).ToArray();
        WorldPosition? start = place.Entrances(formation.Footprint).Cast<WorldPosition?>().FirstOrDefault(p =>
            rules.Map.PassageCost(p!.Value, formation.Footprint, formation.TravelMode, occupancy) is not null);
        if (start is null) return CommandRejection.NoDestination;
        if (world.Simulation.NextUnitId > int.MaxValue) return CommandRejection.ResourceOverflow;
        int id = (int)world.Simulation.NextUnitId;
        int? commander = LeadershipMaintenance.SelectUnitCommander(world, command.OfficerIds, governance);
        var unit = new UnitState(id, home.OwnerId!.Value, start.Value, command.Soldiers)
        {
            CorpsId = home.CorpsId, ServicePlaceId = home.Id, FormationId = formation.Id, Morale = home.Morale,
            MemberRanks = formation.CaptureRanks.IsEmpty ? ImmutableDictionary<int, BattleRank>.Empty : command.OfficerIds
                .Select((person, index) => (person, rank: formation.CaptureRanks[index])).ToImmutableDictionary(p => p.person, p => p.rank),
            CargoMoney = command.CargoMoney, CargoFood = command.CargoFood, Order = command.Order,
            Policies = command.Policies, CommanderId = commander,
            OrderClass = UnitOrderClass.Military
        };
        world.AddUnit(unit);
        world.SetSimulation(world.Simulation with { NextUnitId = world.Simulation.NextUnitId + 1 });
        world.Replace(home with { Soldiers = home.Soldiers - unit.Soldiers, Morale = home.Soldiers == unit.Soldiers ? 0 : home.Morale });
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, home.Id), ResourceKind.Soldiers,
            "deploy", unit.Id, -(long)unit.Soldiers, -(long)unit.Soldiers, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Soldiers,
            "deploy", home.Id, unit.Soldiers, unit.Soldiers, events);
        ResourceAccounts.Change(world, account, ResourceKind.Money, -deploymentCost, "formation-deployment", unit.Id, events, true);
        ResourceAccounts.Change(world, account, ResourceKind.Money, -(long)unit.CargoMoney, "transport-load", unit.Id, events, true);
        ResourceAccounts.Change(world, account, ResourceKind.Food, -(long)unit.CargoFood, "transport-load", unit.Id, events, true);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Money, "transport-load", home.Id, unit.CargoMoney, unit.CargoMoney, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Food, "transport-load", home.Id, unit.CargoFood, unit.CargoFood, events);
        foreach (int officer in command.OfficerIds.Order())
            world.Replace(world.Officers[officer] with { SettlementId = null, UnitId = unit.Id, Acted = true, IsCommander = false });
        events.Add(new UnitDeployedEvent(world.Clock, unit.Id, home.Id, command.OfficerIds));
        return CommandRejection.None;
    }

    internal static CommandRejection Order(WorldMutation world, OrderUnitCommand command, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        if (rules is null) return CommandRejection.UnsupportedCommand;
        if (!world.Units.TryGetValue(command.UnitId, out var unit) || unit.OwnerId != command.IssuerId ||
            unit.FormationId is not int id || !rules.Formations.TryGetValue(id, out var formation)) return CommandRejection.InvalidTask;
        if (!ValidOrder(world, unit.OwnerId, command.Order, formation, rules)) return CommandRejection.InvalidTask;
        if (command.Order.Kind == MilitaryOrderKind.Construct) return CommandRejection.InvalidTask;
        if (!command.ConfirmAttack && MilitaryDiplomacy.Warning(world, command.IssuerId, command.Order, rules) != AttackWarning.None)
            return CommandRejection.AttackConfirmationRequired;
        FacilityConstruction.Cancel(world, unit.Id, events);
        world.Replace(unit with { Order = command.Order, EngagementTargetId = null, AwaitingPostAction = false });
        events.Add(new UnitOrderChangedEvent(world.Clock, unit.Id, command.Order, "ordered"));
        return CommandRejection.None;
    }

    internal static bool ValidOrder(WorldMutation world, int owner, MilitaryOrder? order, FormationRule formation, MilitaryRulebook rules)
    {
        if (order is null || !order.IsValid) return false;
        if (order.Kind == MilitaryOrderKind.Construct) return true;
        if (order.IsAttack) return MilitaryCombat.ValidTarget(world, owner, order, formation, rules);
        if (order.Kind == MilitaryOrderKind.Move)
            return order.SettlementId is null && order.Position is WorldPosition p &&
                rules.Map.PassageCost(p, formation.Footprint, formation.TravelMode, []) is not null;
        return order.Position is null && order.SettlementId is int target &&
            world.Settlements.TryGetValue(target, out var destination) && destination.OwnerId == owner &&
            destination.CanHostPersons && destination.Durability > 0 && rules.TryPlace(destination, out var place) &&
            place.Entrances(formation.Footprint).Any(p => rules.Map.PassageCost(p, formation.Footprint, formation.TravelMode, []) is not null);
    }

    internal static void AdvanceDay(WorldMutation world, MilitaryRulebook? rules, GovernanceRulebook governance, PersonnelRulebook geography, List<DomainEvent> events)
    {
        if (rules is null) return;
        foreach (int id in world.Units.Keys.Order().ToArray())
        {
            var unit = world.Units[id];
            if (unit.EffectiveOrder is not { } order || unit.Condition == UnitCondition.Confused) continue;
            if (order.Kind == MilitaryOrderKind.Construct) continue;
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
            WorldPosition from = unit.Position;
            var result = MilitaryNavigation.Advance(world, unit, rules, MilitaryNavigation.Budget(unit, rules));
            unit = MilitaryNavigation.Apply(unit, result);
            world.Replace(unit);
            events.Add(new UnitMovedEvent(world.Clock, id, from, result.Position, result.Spent, result.Status));
            if (order.IsAttack) continue;
            if (!result.Arrived) continue;
            if (order.SettlementId is int destination) Absorb(world, unit, destination, governance, geography, events);
            else
            {
                world.Replace(unit with { Order = null });
                events.Add(new UnitOrderChangedEvent(world.Clock, id, null, "arrived"));
            }
        }
    }

    internal static void Absorb(WorldMutation world, UnitState unit, int destinationId, GovernanceRulebook governance, PersonnelRulebook geography, List<DomainEvent> events)
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
        uint wounded = Math.Min(MilitaryRulebook.WoundedCapacity - home.Wounded, unit.Wounded);
        long overflow = (long)unit.Soldiers - accepted + unit.Wounded - wounded;
        int? cityId = Demobilization.City(world, home.Id, geography);
        if (overflow > 0 && cityId is null)
        {
            events.Add(new UnitOrderChangedEvent(world.Clock, unit.Id, unit.Order, "absorption-no-demobilization-city"));
            return;
        }
        world.Replace(home with { Soldiers = total, Morale = morale, Wounded = home.Wounded + wounded });
        if (cityId is int city) Demobilization.Return(world, city, overflow, "unit-absorbed-demobilized", unit.Id, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, home.Id), ResourceKind.Wounded, "unit-absorbed", unit.Id, wounded, wounded, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Wounded, "unit-absorbed", home.Id, -(long)unit.Wounded, -(long)unit.Wounded, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, home.Id), ResourceKind.Soldiers, "unit-absorbed", unit.Id, accepted, accepted, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Soldiers, "unit-absorbed", home.Id, -(long)unit.Soldiers, -(long)unit.Soldiers, events);
        var account = ResourceAccounts.Resolve(world, unit.OwnerId, home.CorpsId);
        ResourceAccounts.Change(world, account, ResourceKind.Money, unit.CargoMoney, "transport-unload", unit.Id, events);
        ResourceAccounts.Change(world, account, ResourceKind.Food, unit.CargoFood, "transport-unload", unit.Id, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Money, "transport-unload", home.Id, -(long)unit.CargoMoney, -(long)unit.CargoMoney, events);
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Food, "transport-unload", home.Id, -(long)unit.CargoFood, -(long)unit.CargoFood, events);
        foreach (var officer in world.Officers.Values.Where(p => p.UnitId == unit.Id).OrderBy(p => p.Id).ToArray())
            world.Replace(officer with { UnitId = null, SettlementId = home.Id, Acted = true });
        world.RemoveUnit(unit.Id);
        MilitaryPolicies.RemoveTarget(world, unit.Id, false, events);
        if (unit.CorpsId is int previous && previous != home.CorpsId)
        {
            if (GovernanceMaintenance.RepairCorps(world, previous, governance, events) != CommandRejection.None)
                throw new InvalidOperationException("Validated unit absorption could not settle previous corps.");
        }
        events.Add(new UnitAbsorbedEvent(world.Clock, unit.Id, home.Id));
    }

    private static bool CanAbsorb(WorldMutation world, UnitState unit, SettlementState home)
    {
        if (unit.CorpsId is not int previous || previous == home.CorpsId || world.Corps[previous].IsRoot)
            return true;
        bool survivingMembers = world.Officers.Values.Any(p => p.IsServing && p.OwnerId == unit.OwnerId &&
            p.UnitId != unit.Id && PersonnelLocations.Corps(world, p) == previous);
        if (survivingMembers && world.Settlements.Values.Any(s => s.CorpsId == previous && s.Kind == SettlementKind.City)) return true;
        var force = world.Forces[unit.OwnerId];
        bool forceReceivesCargo = ResourceAccounts.Resolve(world, unit.OwnerId, home.CorpsId).Id == ResourceAccounts.Root(world, unit.OwnerId).Id;
        uint money = forceReceivesCargo ? ResourceAccounts.Project(ResourceAccounts.Root(world, force.Id).Treasury.Money, unit.CargoMoney) : ResourceAccounts.Root(world, force.Id).Treasury.Money;
        uint food = forceReceivesCargo ? ResourceAccounts.Project(ResourceAccounts.Root(world, force.Id).Treasury.Food, unit.CargoFood) : ResourceAccounts.Root(world, force.Id).Treasury.Food;
        return (ulong)money + world.Corps[previous].Treasury.Money <= uint.MaxValue && (ulong)food + world.Corps[previous].Treasury.Food <= uint.MaxValue;
    }

    internal static IEnumerable<MapOccupancy> Occupancy(WorldMutation world, MilitaryRulebook rules, int? movingUnit, int? entry, bool movement = false)
    {
        foreach (var settlement in world.Settlements.Values.Where(s => s.Id != entry && !s.IsDestroyed))
            if (rules.TryPlace(settlement, out var place))
            {
                if (movement && movingUnit is int moving && world.Units.TryGetValue(moving, out var actor) &&
                    !MilitaryDiplomacy.Work(world, settlement.Id, rules) &&
                    (settlement.OwnerId is null || !MilitaryDiplomacy.AutomaticEnemy(world, actor.OwnerId, settlement.OwnerId))) continue;
                yield return new(place.Position, place.Footprint);
            }
        foreach (var unit in world.Units.Values.Where(u => u.Id != movingUnit))
        {
            if (unit.FormationId is not int formation) continue;
            yield return new(unit.Position, rules.Formations[formation].Footprint);
        }
    }
}
