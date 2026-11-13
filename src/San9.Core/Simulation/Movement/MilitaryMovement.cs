using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record DeployUnitCommand(Guid CommandId, int IssuerId, long Sequence, int SettlementId,
    ImmutableArray<int> OfficerIds, uint Soldiers, int FormationId, MilitaryOrder Order) : GameCommand(CommandId, IssuerId, Sequence)
{
    [JsonRequired] public ImmutableDictionary<int, int?> Tactics { get; init; } = ImmutableDictionary<int, int?>.Empty;
    [JsonRequired] public ImmutableDictionary<int, FormationPosition> Positions { get; init; } = ImmutableDictionary<int, FormationPosition>.Empty;
    public ImmutableDictionary<int, DetachmentState> Detachments { get; init; } = ImmutableDictionary<int, DetachmentState>.Empty;
    public int? NavalTrainingId { get; init; }
    public int? SiegeEquipmentId { get; init; }
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
public sealed record UnitOrderRequest(int UnitId, UnitPolicies? Policies = null, WorldPosition? Waypoint = null);

/// <summary>One atomic intention for independently moving units sharing a target.</summary>
public sealed record OrderUnitsCommand(Guid CommandId, int IssuerId, long Sequence,
    ImmutableArray<UnitOrderRequest> Units, MilitaryOrder Order) : GameCommand(CommandId, IssuerId, Sequence)
{
    [JsonRequired] public bool ConfirmAttack { get; init; }
    public OrderUnitCommand ForUnit(UnitOrderRequest unit) => new(CommandId, IssuerId, Sequence, unit.UnitId, Order)
        { Policies = unit.Policies, Waypoint = unit.Waypoint, ConfirmAttack = ConfirmAttack };
}

public sealed record UnitDeployedEvent(GameClock Clock, int UnitId, int SettlementId, ImmutableArray<int> OfficerIds) : DomainEvent;
public sealed record UnitOrderChangedEvent(GameClock Clock, int UnitId, MilitaryOrder? Order, string Reason) : DomainEvent
{
    [JsonRequired] public MilitaryOrder? PreviousOrder { get; init; }
}
public sealed record UnitMovedEvent(GameClock Clock, int UnitId, WorldPosition From, WorldPosition To, int Spent, NavigationStatus Status) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired] public ImmutableArray<WorldPosition> Steps { get; init; } = [];
}
public sealed record UnitWaypointReachedEvent(GameClock Clock, BattleObjectContext Unit, WorldPosition Position) : DomainEvent;
public sealed record UnitDisplacedEvent(GameClock Clock, int UnitId, int SettlementId, WorldPosition From, WorldPosition To) : DomainEvent;
public sealed record UnitAbsorbedEvent(GameClock Clock, int UnitId, int SettlementId) : DomainEvent
{
    [JsonRequired] public required BattleObjectContext Source { get; init; }
    [JsonRequired] public required SettlementState Result { get; init; }
    [JsonRequired] public bool WasTransport { get; init; }
}

internal static class MilitaryMovement
{
    internal static void Validate(WorldSnapshot world, MilitaryRulebook? rules)
    {
        if (world.Simulation.NewGame.MixedTroopsEnabled && (rules is null || rules.TroopTypes.IsEmpty || rules.ResearchProjects.IsEmpty ||
            rules.Formations.Values.Any(formation => formation.TravelMode == TravelMode.Water) &&
            !rules.NavalTraining.Values.Any(training => training.ResearchProjectId is null)))
            throw new ArgumentException("Mixed troop rules are missing required content.");
        if (world.Forces.Any(force => force.ArmamentInventory?.Keys.Any(id => rules is null || !rules.Armaments.ContainsKey(id)) == true ||
            force.ArmamentReserve?.Keys.Any(id => rules is null || !rules.Armaments.ContainsKey(id)) == true ||
            force.ArmamentProduction is { } production && (rules is null || force.IsDormant || force.IsEliminated ||
                !rules.Armaments.TryGetValue(production.DefinitionId, out var item) || item.ProductionDays is not int days ||
                production.DaysRemaining > days || item.ResearchProjectId is string project && !force.CompletedResearch.Contains(project) ||
                !world.Settlements.Any(place => place.Id == production.SettlementId && place.OwnerId == force.Id &&
                    !place.IsDormant && !place.IsDestroyed && place.CanHostPersons))))
            throw new ArgumentException("Invalid force armament references.");
        if (rules is not null)
        foreach (var force in world.Forces)
        {
            if (force.CompletedResearch.Any(id => !rules.ResearchProjects.TryGetValue(id, out var project) ||
                    project.Prerequisites.Any(required => !force.CompletedResearch.Contains(required))) ||
                force.ActiveResearch is { } research && (!rules.ResearchProjects.TryGetValue(research.ProjectId, out var current) ||
                    current.Prerequisites.Any(required => !force.CompletedResearch.Contains(required)) ||
                    research.DaysRemaining > current.Days || !world.Settlements.Any(place => place.Id == research.SettlementId &&
                        place.OwnerId == force.Id && !place.IsDormant && !place.IsDestroyed && place.CanHostPersons)))
                throw new ArgumentException("Invalid force research references.");
        }
        if (world.Forces.Any(f => f.FormationGrants.Any(g => rules is null || !rules.Formations.ContainsKey(g.FormationId))))
            throw new ArgumentException("Unknown force formation grant.");
        if (world.Officers.Any(p => p.FormationProficiencies?.Keys.Any(id => rules is null || !rules.Formations.ContainsKey(id)) == true ||
            p.FormationPractice?.Keys.Any(id => rules is null || !rules.Formations.ContainsKey(id)) == true ||
            p.FormationTeaching is { } lesson && (rules is null || !rules.Formations.ContainsKey(lesson.FormationId))))
            throw new ArgumentException("Unknown officer formation reference.");
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
                rules.Purpose(id) != UnitPurpose.Combat && (unit.EngagementTargetId is not null || unit.ContactTarget is not null || unit.AwaitingPostAction) ||
                unit.NavalFormationId is int naval && (unit.Soldiers > rules.Formations[naval].SoldierCapacity ||
                    world.Officers.Count(p => p.UnitId == unit.Id && p.IsServing) > rules.Formations[naval].OfficerCapacity) ||
                unit.MovementRemainder >= rules.Travel(unit).Map.MaximumStepCost(rules.Travel(unit).Mode) ||
                unit.Soldiers > formation.SoldierCapacity || world.Officers.Count(p => p.UnitId == unit.Id && p.IsServing) > formation.OfficerCapacity ||
                rules.Travel(unit).Map.EndpointPassageCost(unit.Position, rules.Travel(unit).Mode) is null)
                throw new ArgumentException("Invalid unit formation, location or capacity.");
            if (unit.Waypoint is { } via && rules.Travel(unit).Map.PassageCost(via, rules.Travel(unit).Shape, rules.Travel(unit).Mode, []) is null)
                throw new ArgumentException("Invalid unit waypoint.");
            if (unit.Lookahead is { } stage && (stage.Position.X >= rules.Map.FineWidth || stage.Position.Y >= rules.Map.FineHeight))
                throw new ArgumentException("Invalid navigation lookahead.");
            if (formation.Combat is not null && world.Officers.Any(p => p.IsServing && p.UnitId == unit.Id && !unit.MemberPositions.ContainsKey(p.Id)))
                throw new ArgumentException("Missing battle member position.");
            if (world.Simulation.NewGame.MixedTroopsEnabled && !unit.IsPersonnel && !unit.IsEmbedded &&
                rules.Purpose(id) == UnitPurpose.Combat &&
                (world.Officers.Any(person => person.IsServing && person.UnitId == unit.Id) && unit.Detachments.IsEmpty ||
                 (unit.NavalFormationId is not null || formation.TravelMode == TravelMode.Water) && unit.NavalTrainingId is null))
                throw new ArgumentException("Missing mixed troop unit configuration.");
            if (!unit.Detachments.IsEmpty && (unit.Detachments.Any(pair =>
                    !world.Officers.Any(p => p.Id == pair.Key && p.IsServing && p.UnitId == unit.Id) ||
                    !rules.TroopTypes.TryGetValue(pair.Value.TroopTypeId, out var troop) || pair.Value.Level > troop.MaximumLevel ||
                    !ValidArmament(pair.Value.Weapon, ArmamentSlot.Weapon, pair.Value.TroopTypeId, rules) ||
                    !ValidArmament(pair.Value.Armor, ArmamentSlot.Armor, pair.Value.TroopTypeId, rules)) ||
                world.Officers.Any(p => p.IsServing && p.UnitId == unit.Id && !unit.Detachments.ContainsKey(p.Id))))
                throw new ArgumentException("Invalid unit detachment members or troop definitions.");
            if (unit.NavalTrainingId is int training && !rules.NavalTraining.ContainsKey(training))
                throw new ArgumentException("Invalid unit naval training reference.");
            if (unit.SiegeEquipmentId is int siege && !rules.SiegeEquipment.ContainsKey(siege))
                throw new ArgumentException("Invalid unit siege equipment reference.");
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

    private static bool ValidArmament(ArmamentAllocation? allocation, ArmamentSlot slot, int troopTypeId, MilitaryRulebook rules) =>
        allocation is null || allocation.IsValid && rules.Armaments.TryGetValue(allocation.DefinitionId, out var definition) &&
        definition.Slot == slot && definition.Supports(troopTypeId);

    internal static CommandRejection Transport(WorldMutation world, TransportUnitCommand command, MilitaryRulebook? rules,
        GovernanceRulebook governance, TreasureRulebook treasures, List<DomainEvent> events)
    {
        if (rules?.TransportFormations is not { } pair) return CommandRejection.UnsupportedCommand;
        return Deploy(world, TransportDeployment(command, pair), rules, governance, treasures, events,
            UnitPurpose.Transport, command.CargoMoney, command.CargoFood);
    }

    internal static CommandRejection TransportEligibility(WorldMutation world, TransportUnitCommand command,
        MilitaryRulebook? rules, GovernanceRulebook governance, TreasureRulebook treasures) => rules?.TransportFormations is not { } pair
        ? CommandRejection.UnsupportedCommand : PrepareDeployment(world, TransportDeployment(command, pair), rules, governance, treasures,
            UnitPurpose.Transport, command.CargoMoney, command.CargoFood, out _);

    private static DeployUnitCommand TransportDeployment(TransportUnitCommand command, FixedUnitFormations pair) =>
        new(command.CommandId, command.IssuerId, command.Sequence, command.SettlementId, command.OfficerIds,
            command.Soldiers, pair.LandFormationId, new(MilitaryOrderKind.Transport, null, command.DestinationId))
        { NavalFormationId = pair.NavalFormationId, Policies = command.Policies, Waypoint = command.Waypoint };

    internal static CommandRejection Deploy(WorldMutation world, DeployUnitCommand command, MilitaryRulebook? rules,
        GovernanceRulebook governance, TreasureRulebook treasures, List<DomainEvent> events,
        UnitPurpose purpose = UnitPurpose.Combat, uint cargoMoney = 0, uint cargoFood = 0)
    {
        var rejection = PrepareDeployment(world, command, rules, governance, treasures, purpose, cargoMoney, cargoFood, out var prepared);
        if (rejection != CommandRejection.None) return rejection;
        return ApplyDeployment(world, command, governance, events, prepared!, cargoMoney, cargoFood);
    }

    internal static CommandRejection ApplyDeployment(WorldMutation world, DeployUnitCommand command,
        GovernanceRulebook governance, List<DomainEvent> events, DeploymentPreparation prepared, uint cargoMoney = 0, uint cargoFood = 0)
    {
        var (formation, navalId, start, account, deploymentCost, consumption) = prepared;
        var home = world.Settlements[command.SettlementId];
        if (!consumption.IsEmpty)
        {
            var force = world.Forces[command.IssuerId];
            var inventory = force.ArmamentInventory ?? ImmutableDictionary<string, uint>.Empty;
            foreach (var (definitionId, count) in consumption)
            {
                uint available = inventory.GetValueOrDefault(definitionId);
                if (count > available) return CommandRejection.InvalidTask;
                inventory = count == available ? inventory.Remove(definitionId) : inventory.SetItem(definitionId, available - count);
            }
            world.Replace(force with { ArmamentInventory = inventory.IsEmpty ? null : inventory });
        }
        int id = (int)world.Simulation.NextUnitId;
        int? commander = LeadershipMaintenance.SelectUnitCommander(world, command.OfficerIds, governance);
        var unit = new UnitState(id, home.OwnerId!.Value, start, command.Soldiers)
        {
            CorpsId = home.CorpsId, ServicePlaceId = home.Id, FormationId = formation.Id, NavalFormationId = navalId, Morale = home.Morale,
            MemberPositions = formation.Positions.IsEmpty ? command.Positions : command.OfficerIds
                .Select((person, index) => (person, position: command.Positions.GetValueOrDefault(person, formation.Positions[index]))).ToImmutableDictionary(p => p.person, p => p.position),
            Detachments = command.Detachments,
            NavalTrainingId = command.NavalTrainingId,
            SiegeEquipmentId = command.SiegeEquipmentId,
            CargoMoney = cargoMoney, CargoFood = cargoFood, Order = command.Order,
            DepartureDaysRemaining = command.DepartureDelayDays, Waypoint = command.Waypoint,
            Policies = command.Policies, CommanderId = commander, CommandedThisXun = true, HasEnteredDailyCycle = false,
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

    internal sealed record DeploymentPreparation(FormationRule Formation, int? NavalId,
        WorldPosition Start, ResourceAccount Account, long Cost, ImmutableDictionary<string, uint> ArmamentConsumption);

    internal static CommandRejection PrepareDeployment(WorldMutation world, DeployUnitCommand command,
        MilitaryRulebook? rules, GovernanceRulebook governance, TreasureRulebook treasures, UnitPurpose purpose, uint cargoMoney, uint cargoFood,
        out DeploymentPreparation? prepared, DeploymentPlan? plan = null)
    {
        prepared = null;
        if (rules is null || !rules.Formations.TryGetValue(command.FormationId, out var formation))
            return CommandRejection.UnsupportedCommand;
        if (world.Simulation.NewGame.MixedTroopsEnabled && rules.SiegeEquipment.Values.Any(item => item.FormationId == formation.Id))
            return CommandRejection.FormationUnavailable;
        if (rules.Purpose(formation.Id) != purpose) return CommandRejection.FormationUnavailable;
        int? navalId = purpose == UnitPurpose.Combat
            ? command.NavalFormationId ?? rules.NavalFormationForDeployment(formation.Id) : command.NavalFormationId;
        if (!rules.ValidUnitFormations(formation.Id, navalId)) return CommandRejection.InvalidTask;
        var naval = navalId is int selected ? rules.Formations[selected] : null;
        if (purpose == UnitPurpose.Combat &&
            (!(world.Simulation.NewGame.MixedTroopsEnabled && formation.TravelMode == TravelMode.Land) &&
                !rules.CanSelectFormation(world.Forces[command.IssuerId], formation.Id) ||
            naval is not null && !rules.CanSelectFormation(world.Forces[command.IssuerId], naval.Id)))
            return CommandRejection.FormationUnavailable;
        var travel = rules.Travel(formation.Id, navalId, command.Soldiers);
        long deploymentCost = purpose == UnitPurpose.Construction ? 0 : (long)formation.DeploymentCost + (naval?.DeploymentCost ?? 0);
        if (command.DepartureDelayDays is < 0 or > 9) return CommandRejection.InvalidTask;
        if (command.Policies is null || command.Order is null || purpose != UnitPurpose.Construction && command.Order.Kind == MilitaryOrderKind.Construct) return CommandRejection.InvalidTask;
        if (!world.Settlements.TryGetValue(command.SettlementId, out var home) || home.OwnerId != command.IssuerId ||
            !home.CanHostPersons || home.Durability == 0 || !rules.TryPlace(home, out var place))
            return CommandRejection.NoDestination;
        if (home.ConfusionDaysRemaining > 0) return CommandRejection.SettlementConfused;
        if (command.Soldiers == 0 || command.Soldiers > (plan?.AvailableSoldiers(home.Id) ?? home.Soldiers) || command.Soldiers > formation.SoldierCapacity ||
            command.OfficerIds.IsDefaultOrEmpty || command.OfficerIds.Length > formation.OfficerCapacity ||
            command.OfficerIds.Distinct().Count() != command.OfficerIds.Length)
            return CommandRejection.InvalidTask;
        if (command.OfficerIds.Any(id => plan?.IsOfficerReserved(id) == true ||
            !PersonnelMissions.CanAct(world, command.IssuerId, id) || world.Officers[id].SettlementId != home.Id))
            return CommandRejection.PersonUnavailable;
        if (command.Tactics is null || command.Positions is null || command.Detachments is null ||
            command.Tactics.Keys.Concat(command.Positions.Keys).Any(id => !command.OfficerIds.Contains(id)) ||
            command.Positions.Values.Any(position => !position.IsValid)) return CommandRejection.InvalidTask;
        if (purpose == UnitPurpose.Combat && world.Simulation.NewGame.MixedTroopsEnabled == command.Detachments.IsEmpty)
            return CommandRejection.InvalidTask;
        if (command.SiegeEquipmentId is int siegeId)
        {
            if (purpose != UnitPurpose.Combat || !world.Simulation.NewGame.MixedTroopsEnabled ||
                !rules.SiegeEquipment.TryGetValue(siegeId, out var siege) ||
                !world.Forces[command.IssuerId].CompletedResearch.Contains(siege.ResearchProjectId)) return CommandRejection.InvalidTask;
            deploymentCost += siege.DeploymentCost;
        }
        if (purpose == UnitPurpose.Combat && world.Simulation.NewGame.MixedTroopsEnabled && navalId is not null)
        {
            if (command.NavalTrainingId is not int trainingId || !rules.NavalTraining.TryGetValue(trainingId, out var training) ||
                training.ResearchProjectId is string project && !world.Forces[command.IssuerId].CompletedResearch.Contains(project))
                return CommandRejection.InvalidTask;
            deploymentCost += training.DeploymentCost;
        }
        else if (command.NavalTrainingId is not null) return CommandRejection.InvalidTask;
        if (!command.Detachments.IsEmpty && (purpose != UnitPurpose.Combat ||
            command.Detachments.Count != command.OfficerIds.Length ||
            command.Detachments.Any(pair => !command.OfficerIds.Contains(pair.Key) || pair.Value is null ||
                !pair.Value.IsValid || pair.Value.Soldiers == 0 ||
                !rules.TroopTypes.TryGetValue(pair.Value.TroopTypeId, out var troop) || pair.Value.Level > troop.MaximumLevel ||
                troop.LevelResearchProjects.Any(research => research.Key <= pair.Value.Level &&
                    !world.Forces[command.IssuerId].CompletedResearch.Contains(research.Value))) ||
            command.Detachments.Values.Aggregate(0UL, (sum, d) => sum + d.Soldiers) != command.Soldiers))
            return CommandRejection.InvalidTask;
        var needed = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var detachment in command.Detachments.Values)
        foreach (var (allocation, slot) in new[] { (detachment.Weapon, ArmamentSlot.Weapon), (detachment.Armor, ArmamentSlot.Armor) })
        {
            if (allocation is null) continue;
            if (!ValidArmament(allocation, slot, detachment.TroopTypeId, rules) || allocation.IssuedSets == 0 ||
                allocation.InitialSoldiers != detachment.Soldiers || allocation.Casualties != 0)
                return CommandRejection.InvalidTask;
            needed[allocation.DefinitionId] = needed.GetValueOrDefault(allocation.DefinitionId) + allocation.IssuedSets;
        }
        var stock = world.Forces[command.IssuerId].ArmamentInventory;
        if (needed.Any(pair => pair.Value > uint.MaxValue || pair.Value + (plan?.ReservedArmament(pair.Key) ?? 0) >
            (stock?.GetValueOrDefault(pair.Key) ?? 0))) return CommandRejection.InvalidTask;
        deploymentCost += rules.DetachmentEquipmentCost(command.Detachments.Values);
        foreach (var (person, tacticId) in command.Tactics)
            if (tacticId is int tactic && (!rules.Tactics.TryGetValue(tactic, out var definition) || !definition.Selectable ||
                !MilitaryTactics.HasTactic(world, command.IssuerId, [person], tactic, treasures))) return CommandRejection.TacticUnavailable;
        int? selectedCommander = LeadershipMaintenance.SelectUnitCommander(world, command.OfficerIds, governance);
        var commander = selectedCommander is int commanderId ? world.Officers[commanderId] : null;
        if (purpose == UnitPurpose.Combat && world.Simulation.NewGame.MixedTroopsEnabled &&
            formation.TravelMode == TravelMode.Land && commander?.FormationProficiencies is null &&
            !rules.CanSelectFormation(world.Forces[command.IssuerId], formation.Id)) return CommandRejection.FormationUnavailable;
        if (!FormationEligibility.CanUse(world, command.IssuerId, command.OfficerIds, formation, treasures,
                commander, personalFormation: purpose == UnitPurpose.Combat) ||
            naval is not null && !FormationEligibility.CanUse(world, command.IssuerId, command.OfficerIds, naval, treasures) ||
            command.SiegeEquipmentId is int siegeIdForEligibility &&
            !FormationEligibility.CanUse(world, command.IssuerId, command.OfficerIds,
                rules.Formations[rules.SiegeEquipment[siegeIdForEligibility].FormationId], treasures))
            return CommandRejection.MissingFormationTactic;
        if (naval is not null && (command.Soldiers > naval.SoldierCapacity || command.OfficerIds.Length > naval.OfficerCapacity)) return CommandRejection.InvalidTask;
        if (!ValidOrder(world, command.IssuerId, command.Order, formation, rules, navalId, command.Soldiers, command.SiegeEquipmentId)) return CommandRejection.InvalidTask;
        if (!command.ConfirmAttack && MilitaryDiplomacy.Warning(world, command.IssuerId, command.Order, rules) != AttackWarning.None)
            return CommandRejection.AttackConfirmationRequired;
        var account = ResourceAccounts.Resolve(world, command.IssuerId, home.CorpsId);
        if ((long)cargoMoney + deploymentCost > ResourceAccounts.Balance(world, account, ResourceKind.Money) - (plan?.ReservedMoney(account) ?? 0) ||
            cargoFood > ResourceAccounts.Balance(world, account, ResourceKind.Food)) return CommandRejection.InsufficientFunds;
        var existing = world.MovementOccupancy(rules, command.IssuerId, command.Policies, null, null);
        IReadOnlyList<MapOccupancy> occupancy = plan is null || plan.Positions.Count == 0 ? existing : [.. existing, .. plan.Positions];
        WorldPosition? start = MilitaryRouteQuery.DeploymentPosition(travel, rules.BuildingArea(place), occupancy);
        if (start is null) return CommandRejection.NoDestination;
        if (command.Waypoint is { } via && (travel.Map.PassageCost(via, travel.Shape, travel.Mode, []) is null ||
            Occupancy(world, rules, null, null).Any(o => o.Overlaps(new(via, new(1, 1)))) ||
            plan is not null && plan.Positions.Any(o => o.Overlaps(new(via, new(1, 1)))) ||
            !travel.Map.FindRoute(start.Value, via, travel.Shape, travel.Mode, occupancy).Reachable))
            return CommandRejection.NoDestination;
        if (world.Simulation.NextUnitId > int.MaxValue - (long)(plan?.Count ?? 0)) return CommandRejection.ResourceOverflow;
        prepared = new(formation, navalId, start.Value, account, deploymentCost,
            needed.ToImmutableDictionary(pair => pair.Key, pair => (uint)pair.Value, StringComparer.Ordinal));
        return CommandRejection.None;
    }

    internal static CommandRejection OrderEligibility(WorldMutation world, OrderUnitCommand command, MilitaryRulebook? rules)
    {
        if (rules is null) return CommandRejection.UnsupportedCommand;
        if (!world.Units.TryGetValue(command.UnitId, out var unit) || unit.IsEmbedded || unit.IsPersonnel || unit.Soldiers == 0 || unit.OwnerId != command.IssuerId ||
            unit.FormationId is not int id || !rules.Formations.TryGetValue(id, out var formation)) return CommandRejection.InvalidTask;
        if (!unit.CanReceiveOrders || !ValidOrder(world, unit.OwnerId, command.Order, formation, rules, unit.NavalFormationId, unit.Soldiers, unit.SiegeEquipmentId)) return CommandRejection.InvalidTask;
        if (command.Order.Kind == MilitaryOrderKind.Construct) return CommandRejection.InvalidTask;
        var policies = command.Policies ?? unit.Policies;
        var travel = rules.Travel(unit);
        if (command.Waypoint is { } via && (travel.Map.PassageCost(via, travel.Shape, travel.Mode, []) is null ||
            Occupancy(world, rules, unit.Id, null).Any(o => o.Overlaps(new(via, new(1, 1)))) ||
            !travel.Map.FindRoute(unit.Position, via, travel.Shape, travel.Mode,
                world.MovementOccupancy(rules, unit.OwnerId, policies, unit.Id, null)).Reachable)) return CommandRejection.InvalidTask;
        if (!command.ConfirmAttack && MilitaryDiplomacy.Warning(world, command.IssuerId, command.Order, rules) != AttackWarning.None)
            return CommandRejection.AttackConfirmationRequired;
        return CommandRejection.None;
    }

    internal static CommandRejection Order(WorldMutation world, OrderUnitCommand command, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        var rejection = OrderEligibility(world, command, rules);
        if (rejection != CommandRejection.None) return rejection;
        ApplyOrder(world, command, events);
        return CommandRejection.None;
    }

    internal static CommandRejection OrdersEligibility(WorldMutation world, OrderUnitsCommand command, MilitaryRulebook? rules)
    {
        if (command.Units.IsDefaultOrEmpty || command.Units.Any(u => u is null) ||
            command.Units.Select(u => u.UnitId).Distinct().Count() != command.Units.Length)
            return CommandRejection.InvalidTask;
        foreach (var unit in command.Units)
        {
            var rejection = OrderEligibility(world, command.ForUnit(unit), rules);
            if (rejection != CommandRejection.None) return rejection;
        }
        return CommandRejection.None;
    }

    internal static CommandRejection Orders(WorldMutation world, OrderUnitsCommand command, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        var rejection = OrdersEligibility(world, command, rules);
        if (rejection != CommandRejection.None) return rejection;
        foreach (var unit in command.Units.OrderBy(u => u.UnitId)) ApplyOrder(world, command.ForUnit(unit), events);
        return CommandRejection.None;
    }

    private static void ApplyOrder(WorldMutation world, OrderUnitCommand command, List<DomainEvent> events)
    {
        var unit = world.Units[command.UnitId];
        var policies = command.Policies ?? unit.Policies;
        FacilityConstruction.Cancel(world, unit.Id, events);
        world.AssignMilitaryOrder(unit.Id, command.Order, policies, command.Waypoint);
        UnitOrderChanges.Report(world, unit.Id, command.Order, "ordered", events);
        if (policies != unit.Policies) events.Add(new UnitPoliciesChangedEvent(world.Clock, unit.Id, policies));
    }

    internal static bool ValidOrder(IWorldQuery world, int owner, MilitaryOrder? order, FormationRule formation, MilitaryRulebook rules, int? navalFormationId = null, uint soldiers = 1, int? siegeEquipmentId = null)
    {
        if (order is null || !order.IsValid || !rules.AllowsOrder(formation.Id, order)) return false;
        if (order.Kind == MilitaryOrderKind.Construct) return true;
        if (order.IsAttack)
        {
            if (siegeEquipmentId is int equipment && !rules.SiegeEquipment.ContainsKey(equipment)) return false;
            var attackFormation = order.SettlementId is not null && siegeEquipmentId is int siege
                ? rules.Formations[rules.SiegeEquipment[siege].FormationId] : formation;
            return MilitaryCombat.ValidTarget(world, owner, order, attackFormation, rules) ||
                navalFormationId is int naval && MilitaryCombat.ValidTarget(world, owner, order, rules.Formations[naval], rules);
        }
        var travel = rules.Travel(formation.Id, navalFormationId, soldiers);
        if (order.Kind == MilitaryOrderKind.Move)
            return order.SettlementId is null && order.Position is WorldPosition p &&
                travel.Map.PassageCost(p, travel.Shape, travel.Mode, []) is not null;
        return order.Position is null && order.SettlementId is int target &&
            world.Settlements.TryGetValue(target, out var destination) && destination.OwnerId == owner &&
            destination.CanHostPersons && destination.Durability > 0 && rules.TryPlace(destination, out var place) &&
            travel.Entrances(rules.BuildingArea(place)).Any(p => travel.Map.PassageCost(p, travel.Shape, travel.Mode, []) is not null);
    }

    internal static void BeginDailyCycle(WorldMutation world)
    {
        foreach (var unit in world.Units.Values.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.FormationId is not null &&
            u.EffectiveOrder is not null && !u.HasEnteredDailyCycle).OrderBy(u => u.Id).ToArray())
            world.Replace(unit with { HasEnteredDailyCycle = true });
    }

    internal sealed record Arrival(int UnitId, MilitaryOrder Order);

    internal static IReadOnlyList<Arrival> AdvanceDay(WorldMutation world, MilitaryRulebook? rules, List<DomainEvent> events)
    {
        List<Arrival> arrivals = [];
        if (rules is null) return arrivals;
        foreach (int id in world.Units.Keys.Order().ToArray())
        {
            var unit = world.Units[id];
            if (unit.DepartureDaysRemaining > 0) continue;
            if (unit.EffectiveOrder is not { } order || unit.Condition == UnitCondition.Confused) continue;
            if (order.Kind == MilitaryOrderKind.Construct) continue;
            var formation = rules.Formations[unit.FormationId!.Value];
            if (!ValidOrder(world, unit.OwnerId, order, formation, rules, unit.NavalFormationId, unit.Soldiers, unit.SiegeEquipmentId))
            {
                // Forced returns are projections over the stored mission, not replacements for it.
                if (!unit.HasForcedReturn) MilitaryOrders.ResolveOrder(world, unit, rules, events);
                continue;
            }
            var result = MilitaryNavigation.Advance(world, unit, unit.Id, rules, MilitaryNavigation.Budget(unit, rules));
            unit = MilitaryNavigation.Commit(world, unit, result, rules, events);
            if (unit.EffectiveOrder?.IsAttack == true) continue;
            if (!result.Arrived) continue;
            arrivals.Add(new(unit.Id, order));
        }
        return arrivals;
    }

    internal static void ResolveArrivals(WorldMutation world, IReadOnlyList<Arrival> arrivals, MilitaryRulebook? rules,
        GovernanceRulebook governance, PersonnelRulebook geography, List<DomainEvent> events)
    {
        if (rules is null) return;
        foreach (var arrival in arrivals)
        {
            if (!world.Units.TryGetValue(arrival.UnitId, out var unit) || unit.EffectiveOrder != arrival.Order) continue;
            if (arrival.Order.SettlementId is int destination) Absorb(world, unit, destination, governance, geography, rules, events);
            else
            {
                world.Replace(unit with { Order = null, AutonomousReturn = false, PursuitResponsePending = false });
                UnitOrderChanges.Report(world, unit.Id, null, "arrived", events);
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
        int root = ResourceAccounts.Root(world, unit.OwnerId).Id;
        int sourceCorps = unit.CorpsId ?? root, previousCorps = home.CorpsId ?? root;
        bool transfer = returnHome.Id == home.Id && sourceCorps != previousCorps &&
            unit.CommanderId is int commander && world.Officers.TryGetValue(commander, out var leader) &&
            leader.UnitId == unit.Id && leader.Status == OfficerStatus.Ruler;
        var followers = transfer ? world.Units.Values.Where(u => u.Id != unit.Id && u.OwnerId == unit.OwnerId &&
            u.ServicePlaceId == home.Id && (u.IsEmbedded || u.IsPersonnel || u.EffectiveOrder is not null ||
                u.EffectiveOrderClass == UnitOrderClass.Military)).ToArray() : [];
        var affected = new[] { sourceCorps, previousCorps }.Concat(followers.Select(u => u.CorpsId ?? root)).Distinct().ToArray();
        if (!CanAbsorb(world, unit, home, returnHome, transfer, followers.Select(u => u.Id).ToHashSet(), affected))
        {
            UnitOrderChanges.Report(world, unit.Id, unit.Order, "absorption-blocked", events);
            return;
        }
        var force = world.Forces[unit.OwnerId];
        if (!ArmamentInventory.CanReturnWithReserve(force.ArmamentInventory ?? ImmutableDictionary<string, uint>.Empty,
                force.ArmamentReserve ?? ImmutableDictionary<string, ulong>.Empty, unit.Detachments.Values))
        {
            UnitOrderChanges.Report(world, unit.Id, unit.Order, "absorption-blocked", events);
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
            UnitOrderChanges.Report(world, unit.Id, unit.Order, "absorption-no-demobilization-city", events);
            return;
        }
        home = home with { Soldiers = total, Morale = morale, Wounded = home.Wounded + wounded,
            CorpsId = transfer ? sourceCorps : home.CorpsId };
        world.Replace(home);
        foreach (var follower in followers) world.Replace(follower with { CorpsId = sourceCorps });
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
        MilitaryOrders.RemoveTarget(world, unit.Id, false, events);
        foreach (int previous in affected)
        {
            if (GovernanceMaintenance.RepairCorps(world, previous, governance, events) != CommandRejection.None)
                throw new InvalidOperationException("Validated unit absorption could not settle previous corps.");
        }
        events.Add(new UnitAbsorbedEvent(world.Clock, unit.Id, home.Id)
            { Source = BattleObjectContext.Capture(unit), WasTransport = unit.Order?.Kind == MilitaryOrderKind.Transport,
                Result = world.Settlements[home.Id] });
    }

    private static bool CanAbsorb(WorldMutation world, UnitState unit, SettlementState home, SettlementState returnHome,
        bool transfer, IReadOnlySet<int> followers, IReadOnlyList<int> affected)
    {
        var root = ResourceAccounts.Root(world, unit.OwnerId);
        int source = unit.CorpsId ?? root.Id;
        int PlaceCorps(SettlementState place) => transfer && place.Id == home.Id ? source : place.CorpsId ?? root.Id;
        int? PersonCorps(OfficerState person) => person.UnitId == unit.Id ? PlaceCorps(returnHome) :
            person.UnitId is int id && followers.Contains(id) ? source :
            person.SettlementId == home.Id ? PlaceCorps(home) : PersonnelLocations.Corps(world, person) ?? root.Id;
        bool rootReceivesCargo = PlaceCorps(home) == root.Id;
        ulong money = rootReceivesCargo ? ResourceAccounts.Project(root.Treasury.Money, unit.CargoMoney) : root.Treasury.Money;
        ulong food = rootReceivesCargo ? ResourceAccounts.Project(root.Treasury.Food, unit.CargoFood) : root.Treasury.Food;
        foreach (int id in affected.Where(id => id != root.Id))
        {
            bool people = world.Officers.Values.Any(p => p.IsServing && p.OwnerId == unit.OwnerId && PersonCorps(p) == id);
            bool cities = world.Settlements.Values.Any(s => s.Kind == SettlementKind.City && PlaceCorps(s) == id);
            if (people && cities) continue;
            var corps = world.Corps[id];
            if (corps.LeaderId == world.Forces[unit.OwnerId].LeaderId) return false;
            money += corps.Treasury.Money;
            food += corps.Treasury.Food;
        }
        return money <= uint.MaxValue && food <= uint.MaxValue;
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
            yield return new(unit.Position, rules.UnitFootprint(unit));
        }
    }
}
