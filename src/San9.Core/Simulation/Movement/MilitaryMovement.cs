using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record DeployUnitCommand(Guid CommandId, int IssuerId, long Sequence, int SettlementId,
    ImmutableArray<int> OfficerIds, uint Soldiers, int FormationId, MilitaryOrder Order) : GameCommand(CommandId, IssuerId, Sequence)
{
    [JsonRequired] public ImmutableDictionary<int, int?> Tactics { get; init; } = ImmutableDictionary<int, int?>.Empty;
    [JsonRequired] public ImmutableDictionary<int, BattleRank> Ranks { get; init; } = ImmutableDictionary<int, BattleRank>.Empty;
    [JsonRequired] public int DepartureDelayDays { get; init; }
    [JsonRequired] public WorldPosition? Waypoint { get; init; }
    [JsonRequired] public int? NavalFormationId { get; init; }
    [JsonRequired] public bool ConfirmAttack { get; init; }
    [JsonRequired] public UnitPolicies Policies { get; init; } = new();
}
public sealed record TransportUnitCommand(Guid CommandId, int IssuerId, long Sequence, int SettlementId,
    ImmutableArray<int> OfficerIds, uint Soldiers, int DestinationId, uint CargoMoney = 0, uint CargoFood = 0)
    : GameCommand(CommandId, IssuerId, Sequence)
{
    [JsonRequired] public UnitPolicies Policies { get; init; } = new();
    [JsonRequired] public WorldPosition? Waypoint { get; init; }
}
public sealed record OrderUnitCommand(Guid CommandId, int IssuerId, long Sequence, int UnitId, MilitaryOrder Order)
    : GameCommand(CommandId, IssuerId, Sequence)
{
    [JsonRequired] public bool ConfirmAttack { get; init; }
    [JsonRequired] public UnitPolicies? Policies { get; init; }
    [JsonRequired] public WorldPosition? Waypoint { get; init; }
}
public sealed record UnitDeployedEvent(GameClock Clock, int UnitId, int SettlementId, ImmutableArray<int> OfficerIds) : DomainEvent;
public sealed record UnitOrderChangedEvent(GameClock Clock, int UnitId, MilitaryOrder? Order, string Reason) : DomainEvent;
public sealed record UnitMovedEvent(GameClock Clock, int UnitId, WorldPosition From, WorldPosition To, int Spent, NavigationStatus Status) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired] public ImmutableArray<WorldPosition> Steps { get; init; } = [];
}
public sealed record UnitAbsorbedEvent(GameClock Clock, int UnitId, int SettlementId) : DomainEvent
{
    [JsonRequired] public required BattleObjectContext Source { get; init; }
    [JsonRequired] public bool WasTransport { get; init; }
}

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
                !rules.ValidUnitFormations(id, unit.NavalFormationId) || !rules.AllowsOrder(id, unit.Order) ||
                rules.Purpose(id) != UnitPurpose.Combat && (unit.EngagementTargetId is not null || unit.AwaitingPostAction) ||
                unit.NavalFormationId is int naval && (unit.Soldiers > rules.Formations[naval].SoldierCapacity ||
                    world.Officers.Count(p => p.UnitId == unit.Id && p.IsServing) > rules.Formations[naval].OfficerCapacity) ||
                unit.MovementRemainder >= rules.Travel(unit).Map.MaximumStepCost(rules.Travel(unit).Mode) ||
                unit.Soldiers > formation.SoldierCapacity || world.Officers.Count(p => p.UnitId == unit.Id && p.IsServing) > formation.OfficerCapacity ||
                rules.Travel(unit).Map.PassageCost(unit.Position, rules.Travel(unit).Shape, rules.Travel(unit).Mode, []) is null)
                throw new ArgumentException("Invalid unit formation, location or capacity.");
            if (unit.Waypoint is { } via && rules.Travel(unit).Map.PassageCost(via, rules.Travel(unit).Shape, rules.Travel(unit).Mode, []) is null)
                throw new ArgumentException("Invalid unit waypoint.");
            if (unit.Lookahead is { } stage && (rules.Travel(unit).Map.PassageCost(stage.Position, rules.Travel(unit).Shape, rules.Travel(unit).Mode, []) is null ||
                stage.Target.Left < 0 || stage.Target.Top < 0 || stage.Target.Right > rules.Travel(unit).Map.FineWidth || stage.Target.Bottom > rules.Travel(unit).Map.FineHeight))
                throw new ArgumentException("Invalid navigation lookahead.");
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

    internal static CommandRejection Transport(WorldMutation world, TransportUnitCommand command, MilitaryRulebook? rules,
        GovernanceRulebook governance, TreasureRulebook treasures, List<DomainEvent> events)
    {
        if (rules?.TransportFormations is not { } pair) return CommandRejection.UnsupportedCommand;
        return Deploy(world, new(command.CommandId, command.IssuerId, command.Sequence, command.SettlementId,
            command.OfficerIds, command.Soldiers, pair.LandFormationId, new(MilitaryOrderKind.Transport, null, command.DestinationId))
            { NavalFormationId = pair.NavalFormationId, Policies = command.Policies, Waypoint = command.Waypoint }, rules, governance, treasures, events,
            UnitPurpose.Transport, command.CargoMoney, command.CargoFood);
    }

    internal static CommandRejection Deploy(WorldMutation world, DeployUnitCommand command, MilitaryRulebook? rules,
        GovernanceRulebook governance, TreasureRulebook treasures, List<DomainEvent> events,
        UnitPurpose purpose = UnitPurpose.Combat, uint cargoMoney = 0, uint cargoFood = 0)
    {
        if (rules is null || !rules.Formations.TryGetValue(command.FormationId, out var formation))
            return CommandRejection.UnsupportedCommand;
        if (rules.Purpose(formation.Id) != purpose) return CommandRejection.FormationUnavailable;
        int? navalId = purpose == UnitPurpose.Combat
            ? command.NavalFormationId ?? rules.NavalFormationForDeployment(formation.Id) : command.NavalFormationId;
        if (!rules.ValidUnitFormations(formation.Id, navalId)) return CommandRejection.InvalidTask;
        var naval = navalId is int selected ? rules.Formations[selected] : null;
        if (purpose == UnitPurpose.Combat && (!rules.CanSelectFormation(world.Forces[command.IssuerId], formation.Id) ||
            naval is not null && !rules.CanSelectFormation(world.Forces[command.IssuerId], naval.Id)))
            return CommandRejection.FormationUnavailable;
        var travel = rules.Travel(formation.Id, navalId);
        long deploymentCost = purpose == UnitPurpose.Construction ? 0 : (long)formation.DeploymentCost + (naval?.DeploymentCost ?? 0);
        if (command.DepartureDelayDays is < 0 or > 9) return CommandRejection.InvalidTask;
        if (command.Policies is null || command.Order is null || purpose != UnitPurpose.Construction && command.Order.Kind == MilitaryOrderKind.Construct) return CommandRejection.InvalidTask;
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
        if (command.Tactics is null || command.Ranks is null ||
            command.Tactics.Keys.Concat(command.Ranks.Keys).Any(id => !command.OfficerIds.Contains(id)) ||
            command.Ranks.Values.Any(rank => !Enum.IsDefined(rank))) return CommandRejection.InvalidTask;
        foreach (var (person, tacticId) in command.Tactics)
            if (tacticId is int tactic && (!rules.Tactics.TryGetValue(tactic, out var definition) || !definition.Selectable ||
                !MilitaryTactics.HasTactic(world, command.IssuerId, [person], tactic, treasures))) return CommandRejection.TacticUnavailable;
        if (!FormationEligibility.CanUse(world, command.IssuerId, command.OfficerIds, formation, treasures) ||
            naval is not null && !FormationEligibility.CanUse(world, command.IssuerId, command.OfficerIds, naval, treasures))
            return CommandRejection.MissingFormationTactic;
        if (naval is not null && (command.Soldiers > naval.SoldierCapacity || command.OfficerIds.Length > naval.OfficerCapacity)) return CommandRejection.InvalidTask;
        if (!ValidOrder(world, command.IssuerId, command.Order, formation, rules, navalId)) return CommandRejection.InvalidTask;
        if (!command.ConfirmAttack && MilitaryDiplomacy.Warning(world, command.IssuerId, command.Order, rules) != AttackWarning.None)
            return CommandRejection.AttackConfirmationRequired;
        var account = ResourceAccounts.Resolve(world, command.IssuerId, home.CorpsId);
        if ((ulong)cargoMoney + (uint)deploymentCost > ResourceAccounts.Balance(world, account, ResourceKind.Money) ||
            cargoFood > ResourceAccounts.Balance(world, account, ResourceKind.Food)) return CommandRejection.InsufficientFunds;
        var occupancy = new MovementOccupancy(world, rules).Query(world, command.IssuerId, command.Policies);
        WorldPosition? start = MilitaryRouteQuery.DeploymentPosition(travel, rules.BuildingArea(place), occupancy);
        if (start is null) return CommandRejection.NoDestination;
        if (command.Waypoint is { } via && (Occupancy(world, rules, null, null).Any(o => o.Overlaps(new(via, new(1, 1)))) ||
            !travel.Map.FindRoute(start.Value, via, travel.Shape, travel.Mode, occupancy).Reachable))
            return CommandRejection.NoDestination;
        if (world.Simulation.NextUnitId > int.MaxValue) return CommandRejection.ResourceOverflow;
        int id = (int)world.Simulation.NextUnitId;
        int? commander = LeadershipMaintenance.SelectUnitCommander(world, command.OfficerIds, governance);
        var unit = new UnitState(id, home.OwnerId!.Value, start.Value, command.Soldiers)
        {
            CorpsId = home.CorpsId, ServicePlaceId = home.Id, FormationId = formation.Id, NavalFormationId = navalId, Morale = home.Morale,
            MemberRanks = formation.CaptureRanks.IsEmpty ? ImmutableDictionary<int, BattleRank>.Empty : command.OfficerIds
                .Select((person, index) => (person, rank: command.Ranks.GetValueOrDefault(person, formation.CaptureRanks[index]))).ToImmutableDictionary(p => p.person, p => p.rank),
            CargoMoney = cargoMoney, CargoFood = cargoFood, Order = command.Order,
            DepartureDaysRemaining = command.DepartureDelayDays, Waypoint = command.Waypoint,
            Policies = command.Policies, CommanderId = commander, CommandedThisXun = true,
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
            world.Replace(world.Officers[officer] with { EquippedTacticId = command.Tactics.GetValueOrDefault(officer, world.Officers[officer].EquippedTacticId), SettlementId = null, UnitId = unit.Id, Acted = true, IsCommander = false });
        events.Add(new UnitDeployedEvent(world.Clock, unit.Id, home.Id, command.OfficerIds));
        return CommandRejection.None;
    }

    internal static CommandRejection Order(WorldMutation world, OrderUnitCommand command, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        if (rules is null) return CommandRejection.UnsupportedCommand;
        if (!world.Units.TryGetValue(command.UnitId, out var unit) || unit.IsEmbedded || unit.IsPersonnel || unit.Soldiers == 0 || unit.OwnerId != command.IssuerId ||
            unit.FormationId is not int id || !rules.Formations.TryGetValue(id, out var formation)) return CommandRejection.InvalidTask;
        if (!ValidOrder(world, unit.OwnerId, command.Order, formation, rules, unit.NavalFormationId)) return CommandRejection.InvalidTask;
        if (command.Order.Kind == MilitaryOrderKind.Construct) return CommandRejection.InvalidTask;
        var policies = command.Policies ?? unit.Policies;
        var travel = rules.Travel(unit);
        if (command.Waypoint is { } via && (Occupancy(world, rules, unit.Id, null).Any(o => o.Overlaps(new(via, new(1, 1)))) ||
            !travel.Map.FindRoute(unit.Position, via, travel.Shape, travel.Mode,
                world.MovementOccupancy(rules, unit.OwnerId, policies, unit.Id, null)).Reachable)) return CommandRejection.InvalidTask;
        if (!command.ConfirmAttack && MilitaryDiplomacy.Warning(world, command.IssuerId, command.Order, rules) != AttackWarning.None)
            return CommandRejection.AttackConfirmationRequired;
        FacilityConstruction.Cancel(world, unit.Id, events);
        world.AssignMilitaryOrder(unit.Id, command.Order, policies, command.Waypoint);
        events.Add(new UnitOrderChangedEvent(world.Clock, unit.Id, command.Order, "ordered"));
        if (policies != unit.Policies) events.Add(new UnitPoliciesChangedEvent(world.Clock, unit.Id, policies));
        return CommandRejection.None;
    }

    internal static bool ValidOrder(IWorldQuery world, int owner, MilitaryOrder? order, FormationRule formation, MilitaryRulebook rules, int? navalFormationId = null)
    {
        if (order is null || !order.IsValid || !rules.AllowsOrder(formation.Id, order)) return false;
        if (order.Kind == MilitaryOrderKind.Construct) return true;
        if (order.IsAttack) return MilitaryCombat.ValidTarget(world, owner, order, formation, rules) ||
            navalFormationId is int naval && MilitaryCombat.ValidTarget(world, owner, order, rules.Formations[naval], rules);
        var travel = rules.Travel(formation.Id, navalFormationId);
        if (order.Kind == MilitaryOrderKind.Move)
            return order.SettlementId is null && order.Position is WorldPosition p &&
                travel.Map.PassageCost(p, travel.Shape, travel.Mode, []) is not null;
        return order.Position is null && order.SettlementId is int target &&
            world.Settlements.TryGetValue(target, out var destination) && destination.OwnerId == owner &&
            destination.CanHostPersons && destination.Durability > 0 && rules.TryPlace(destination, out var place) &&
            travel.Entrances(rules.BuildingArea(place)).Any(p => travel.Map.PassageCost(p, travel.Shape, travel.Mode, []) is not null);
    }

    internal static void AdvanceDay(WorldMutation world, MilitaryRulebook? rules, GovernanceRulebook governance, PersonnelRulebook geography, List<DomainEvent> events)
    {
        if (rules is null) return;
        foreach (int id in world.Units.Keys.Order().ToArray())
        {
            var unit = world.Units[id];
            if (unit.DepartureDaysRemaining > 0) continue;
            if (unit.EffectiveOrder is not { } order || unit.Condition == UnitCondition.Confused) continue;
            if (order.Kind == MilitaryOrderKind.Construct) continue;
            var formation = rules.Formations[unit.FormationId!.Value];
            if (!ValidOrder(world, unit.OwnerId, order, formation, rules, unit.NavalFormationId))
            {
                MilitaryOrder? fallback = unit.ServicePlaceId is int home && home != order.SettlementId
                    ? new(MilitaryOrderKind.Return, null, home) : null;
                if (!ValidOrder(world, unit.OwnerId, fallback, formation, rules, unit.NavalFormationId)) fallback = null;
                world.Replace(unit with { Order = fallback });
                events.Add(new UnitOrderChangedEvent(world.Clock, id, fallback, "target-invalid"));
                continue;
            }
            WorldPosition from = unit.Position;
            var result = MilitaryNavigation.Advance(world, unit, unit.Id, rules, MilitaryNavigation.Budget(unit, rules));
            unit = MilitaryNavigation.Apply(unit, result);
            world.Replace(unit);
            events.Add(new UnitMovedEvent(world.Clock, id, from, result.Position, result.Spent, result.Status) { Steps = result.Navigation.Steps });
            if (order.IsAttack) continue;
            if (!result.Arrived) continue;
            if (order.SettlementId is int destination) Absorb(world, unit, destination, governance, geography, rules, events);
            else
            {
                world.Replace(unit with { Order = null });
                events.Add(new UnitOrderChangedEvent(world.Clock, id, null, "arrived"));
            }
        }
    }

    internal static void TickDepartureDelays(WorldMutation world)
    {
        foreach (var unit in world.Units.Values.Where(u => u.DepartureDaysRemaining > 0).OrderBy(u => u.Id).ToArray())
            world.Replace(unit with { DepartureDaysRemaining = unit.DepartureDaysRemaining - 1 });
    }

    internal static void Absorb(WorldMutation world, UnitState unit, int destinationId, GovernanceRulebook governance, PersonnelRulebook geography, MilitaryRulebook military, List<DomainEvent> events)
    {
        var home = world.Settlements[destinationId];
        var returnHome = unit.EffectiveOrder?.Kind == MilitaryOrderKind.Transport && unit.ServicePlaceId != home.Id
            ? PersonnelReturns.TransportDestination(world, unit, home, military) : home;
        if (!CanAbsorb(world, unit, home, returnHome))
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
        foreach (var officer in world.UnitMembers(unit.Id).OrderBy(p => p.Id).ToArray())
        {
            world.Replace(officer with { UnitId = null, SettlementId = home.Id, Acted = true });
            if (officer.IsServing && returnHome.Id != home.Id)
                PersonnelReturns.FromTransport(world, officer.Id, unit, home.Id, returnHome, military, events);
        }
        world.RemoveUnit(unit.Id);
        MilitaryPolicies.RemoveTarget(world, unit.Id, false, events);
        if (unit.CorpsId is int previous && previous != home.CorpsId)
        {
            if (GovernanceMaintenance.RepairCorps(world, previous, governance, events) != CommandRejection.None)
                throw new InvalidOperationException("Validated unit absorption could not settle previous corps.");
        }
        events.Add(new UnitAbsorbedEvent(world.Clock, unit.Id, home.Id)
            { Source = BattleObjectContext.Capture(unit), WasTransport = unit.Order?.Kind == MilitaryOrderKind.Transport });
    }

    private static bool CanAbsorb(WorldMutation world, UnitState unit, SettlementState home, SettlementState returnHome)
    {
        if (unit.CorpsId is not int previous || previous == home.CorpsId || world.Corps[previous].IsRoot)
            return true;
        bool survivingMembers = world.Officers.Values.Any(p => p.IsServing && p.OwnerId == unit.OwnerId &&
            (p.UnitId == unit.Id ? returnHome.CorpsId == previous : PersonnelLocations.Corps(world, p) == previous));
        if (survivingMembers && world.Settlements.Values.Any(s => s.CorpsId == previous && s.Kind == SettlementKind.City)) return true;
        var force = world.Forces[unit.OwnerId];
        bool forceReceivesCargo = ResourceAccounts.Resolve(world, unit.OwnerId, home.CorpsId).Id == ResourceAccounts.Root(world, unit.OwnerId).Id;
        uint money = forceReceivesCargo ? ResourceAccounts.Project(ResourceAccounts.Root(world, force.Id).Treasury.Money, unit.CargoMoney) : ResourceAccounts.Root(world, force.Id).Treasury.Money;
        uint food = forceReceivesCargo ? ResourceAccounts.Project(ResourceAccounts.Root(world, force.Id).Treasury.Food, unit.CargoFood) : ResourceAccounts.Root(world, force.Id).Treasury.Food;
        return (ulong)money + world.Corps[previous].Treasury.Money <= uint.MaxValue && (ulong)food + world.Corps[previous].Treasury.Food <= uint.MaxValue;
    }

    internal static IEnumerable<MapOccupancy> Occupancy(WorldMutation world, MilitaryRulebook rules, int? movingUnit, int? entry)
    {
        foreach (var settlement in world.Settlements.Values.Where(s => s.Id != entry && !s.IsDestroyed))
            if (rules.TryPlace(settlement, out var place))
            {
                yield return rules.BuildingArea(place);
            }
        foreach (var unit in world.Units.Values.Where(u => u.Id != movingUnit))
        {
            if (unit.FormationId is not int formation) continue;
            yield return new(unit.Position, rules.Formation(unit).Footprint);
        }
    }
}
