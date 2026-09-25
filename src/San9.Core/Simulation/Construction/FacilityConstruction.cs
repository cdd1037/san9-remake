using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ConstructFacilityCommand(Guid CommandId, int IssuerId, long Sequence, int SettlementId,
    ImmutableArray<int> OfficerIds, uint Soldiers, int FormationId, int RegionId, int FacilityTypeId, WorldPosition Position)
    : GameCommand(CommandId, IssuerId, Sequence)
{
    [JsonRequired] public UnitPolicies Policies { get; init; } = new();
}
public sealed record FacilityConstructionChangedEvent(GameClock Clock, int SettlementId, int UnitId, string Phase,
    int Durability) : DomainEvent;

internal static class FacilityConstruction
{
    internal static CommandRejection Start(WorldMutation world, ConstructFacilityCommand command, SimulationRules rules,
        List<DomainEvent> events)
    {
        if (rules.Military is not { } military || !military.Facilities.TryGetValue(command.FacilityTypeId, out var type) ||
            !military.ConstructionSites.TryGetValue((command.RegionId, command.Position), out var site) ||
            !military.Formations.TryGetValue(command.FormationId, out var formation) || formation.TravelMode != TravelMode.Land ||
            !world.Settlements.TryGetValue(command.SettlementId, out var home) || home.OwnerId != command.IssuerId ||
            world.AllSettlements.TryGetValue(command.RegionId, out var reserved) && reserved.IsDormant ||
            command.OfficerIds.IsDefaultOrEmpty || command.OfficerIds.Any(id => !world.Officers.ContainsKey(id)) ||
            !ConstructionPlacement.IsAvailable(command.IssuerId, site, type, military, rules.Personnel.RegionCities,
                world.Settlements.Values, world.Units.Values) || world.Units.Values.Any(u => u.Order?.Construction?.RegionId == command.RegionId))
            return CommandRejection.InvalidTask;
        if (type.RequiredTacticId is int required && !MilitaryTactics.HasTactic(world, command.IssuerId, command.OfficerIds, required, rules.Treasures))
            return CommandRejection.TacticUnavailable;
        int qualified = type.CostReductionTacticId is int discount ? command.OfficerIds.Count(id =>
            MilitaryTactics.HasTactic(world, command.IssuerId, [id], discount, rules.Treasures)) : 0;
        int cost = type.Cost(qualified);
        var account = ResourceAccounts.Resolve(world, command.IssuerId, home.CorpsId);
        if (ResourceAccounts.Balance(world, account, ResourceKind.Money) < cost) return CommandRejection.InsufficientFunds;
        if (LeadershipMaintenance.SelectUnitCommander(world, command.OfficerIds, rules.Governance) is not int commander)
            return CommandRejection.PersonUnavailable;
        var plan = new ConstructionPlan(command.RegionId, type.Id, command.Position, commander);
        if (world.Simulation.NextUnitId > int.MaxValue) return CommandRejection.ResourceOverflow;
        int unitId = (int)world.Simulation.NextUnitId;
        var rejection = MilitaryMovement.Deploy(world, new(command.CommandId, command.IssuerId, command.Sequence, home.Id,
            command.OfficerIds, command.Soldiers, command.FormationId, new(MilitaryOrderKind.Construct, null, null) { Construction = plan }) { Policies = command.Policies },
            military, rules.Governance, rules.Treasures, events, construction: true);
        if (rejection != CommandRejection.None) return rejection;
        if (MilitaryCombat.Area(world.Units[unitId], military).Overlaps(new(command.Position, type.Footprint)))
            return CommandRejection.NoDestination;
        ResourceAccounts.Change(world, account, ResourceKind.Money, -cost, "facility-construction", command.RegionId, events, true);
        events.Add(new FacilityConstructionChangedEvent(world.Clock, command.RegionId, unitId, "ordered", 0));
        return CommandRejection.None;
    }

    internal static void Cancel(WorldMutation world, int unitId, List<DomainEvent> events)
    {
        foreach (var place in world.Settlements.Values.Where(s => s.Facility?.BuilderUnitId == unitId).ToArray())
        {
            world.Replace(place with { IsDestroyed = true, OwnerId = null, CorpsId = null, Durability = 0, Soldiers = 0,
                Wounded = 0, Morale = 0, ConfusionDaysRemaining = 0, Facility = place.Facility! with { BuilderUnitId = null } });
            events.Add(new FacilityConstructionChangedEvent(world.Clock, place.Id, unitId, "cancelled", 0));
            foreach (var other in world.Units.Values.Where(u => u.Order?.SettlementId == place.Id).ToArray())
            {
                world.Replace(other with { Order = null, AwaitingPostAction = other.Order!.IsAttack });
                events.Add(new UnitOrderChangedEvent(world.Clock, other.Id, null, "construction-cancelled"));
            }
        }
    }

    internal static bool CanContinue(WorldMutation world, UnitState unit, MilitaryRulebook military,
        IReadOnlyDictionary<int, int> regionCities, TreasureRulebook treasures)
    {
        if (unit.EffectiveOrder?.Construction is not { } plan ||
            !military.Facilities.TryGetValue(plan.FacilityTypeId, out var type)) return false;
        var existing = world.Settlements.GetValueOrDefault(plan.RegionId);
        bool building = existing?.Facility?.BuilderUnitId == unit.Id && !existing.IsDestroyed;
        return !(!world.Officers.TryGetValue(plan.LeaderId, out var leader) || !leader.IsServing || leader.UnitId != unit.Id ||
                !regionCities.TryGetValue(plan.RegionId, out int cityId) ||
                !world.Settlements.TryGetValue(cityId, out var city) || city.Kind != SettlementKind.City || city.OwnerId != unit.OwnerId || city.IsDestroyed || city.Durability == 0 ||
                type.RequiredTacticId is int required && !MilitaryTactics.HasTactic(world, unit.OwnerId,
                    world.Officers.Values.Where(p => p.IsServing && p.UnitId == unit.Id).Select(p => p.Id), required, treasures) ||
                building && existing!.OwnerId != unit.OwnerId || !building && existing is { IsDestroyed: false });
    }

    internal static void AdvanceDay(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.Military is not { } military) return;
        foreach (int id in world.Units.Keys.Order().ToArray())
        {
            var unit = world.Units[id];
            if (unit.EffectiveOrder?.Construction is not { } plan) continue;
            var type = military.Facilities[plan.FacilityTypeId];
            var existing = world.Settlements.GetValueOrDefault(plan.RegionId);
            bool building = existing?.Facility?.BuilderUnitId == id && !existing.IsDestroyed;
            if (!CanContinue(world, unit, military, rules.Personnel.RegionCities, rules.Treasures))
            {
                Cancel(world, id, events);
                world.Replace(world.Units[id] with { Order = null });
                events.Add(new UnitOrderChangedEvent(world.Clock, id, null, "construction-target-invalid"));
                continue;
            }
            if (unit.Condition is UnitCondition.Confused or UnitCondition.Routed) continue;
            var leader = world.Officers[plan.LeaderId];
            var formation = military.Formation(unit);
            var target = new MapOccupancy(plan.Position, type.Footprint);
            var movement = MilitaryNavigation.Advance(world, unit, military, MilitaryNavigation.Budget(unit, military));
            world.Replace(MilitaryNavigation.Apply(unit, movement));
            events.Add(new UnitMovedEvent(world.Clock, id, unit.Position, movement.Position, movement.Spent, movement.Status));
            if (!movement.Arrived || new MapOccupancy(movement.Position, military.Formation(unit, movement.Position).Footprint).Overlaps(target)) continue;
            if (!building && !ConstructionPlacement.IsAvailable(unit.OwnerId, military.ConstructionSites[(plan.RegionId, plan.Position)],
                type, military, rules.Personnel.RegionCities, world.Settlements.Values, world.Units.Values.Where(u => u.Id != id))) continue;
            if (!building)
            {
                int durability = type.DailyProgress(unit.Soldiers, world.Abilities(leader).Leadership, type.MaxDurability - 1);
                var place = new SettlementState(plan.RegionId, unit.OwnerId, 0, durability, type.MaxDurability, unit.Morale, 0, 0, 0, 0, 0)
                {
                    Kind = SettlementKind.Facility, CorpsId = unit.CorpsId, CanHostPersons = false,
                    SoldierCapacity = type.SoldierCapacity, ConsumesFood = type.ConsumesFood,
                    Facility = new(type.Id, plan.Position, id)
                };
                if (existing is null) world.AddSettlement(place); else world.Replace(place);
                events.Add(new FacilityConstructionChangedEvent(world.Clock, place.Id, id, "started", durability));
                Grow(world, unit, type, military, true, events);
                continue;
            }
            int next = existing!.Durability + type.DailyProgress(unit.Soldiers, world.Abilities(leader).Leadership,
                existing.MaxDurability - existing.Durability);
            bool complete = next == existing.MaxDurability;
            world.Replace(existing with { Durability = next, CanHostPersons = complete && type.CanHostPersons,
                Facility = existing.Facility! with { BuilderUnitId = complete ? null : id } });
            events.Add(new FacilityConstructionChangedEvent(world.Clock, existing.Id, id, complete ? "completed" : "progress", next));
            Grow(world, unit, type, military, complete, events);
            if (!complete) continue;
            unit = world.Units[id] with { Order = null };
            world.Replace(unit);
            if (type.CanHostPersons) MilitaryMovement.Absorb(world, unit, existing.Id, rules.Governance, rules.Personnel, events);
            else
            {
                var order = new MilitaryOrder(MilitaryOrderKind.Return, null, unit.ServicePlaceId);
                if (MilitaryMovement.ValidOrder(world, unit.OwnerId, order, formation, military))
                    world.Replace(unit with { Order = order });
                events.Add(new UnitOrderChangedEvent(world.Clock, id, world.Units[id].Order, "construction-completed"));
            }
        }
    }

    private static void Grow(WorldMutation world, UnitState unit, FacilityRule type, MilitaryRulebook military,
        bool milestone, List<DomainEvent> events)
    {
        if (type.ConstructionGrowthTacticId is not int tacticId) return;
        var tactic = military.Tactics[tacticId];
        var members = world.Officers.Values.Where(p => p.IsServing && p.OwnerId == unit.OwnerId && p.UnitId == unit.Id)
            .OrderBy(p => p.Id).ToArray();
        if (members.Length == 0) return;
        if (milestone)
        {
            var actor = members[(int)(world.NextRandom("military-construction-growth-member") % (uint)members.Length)];
            OfficerAbilityGrowth.TryIncrease(world, actor.Id, OfficerAbility.Leadership, "military-construction-growth", events);
        }
        foreach (var member in members)
        {
            OfficerTacticGrowth.Increase(world, member.Id, tactic.Category!.Value, milestone ? 3 : 1, events);
            if (milestone) OfficerTacticGrowth.TryLearn(world, member.Id, tactic, "military-construction-learning", events);
        }
    }
}
