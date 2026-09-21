using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SettlementCapturedEvent(GameClock Clock, int SettlementId, int? PreviousOwnerId, BattleSource Winner, bool Destroyed) : DomainEvent;
public sealed record FacilityDestroyedEvent(GameClock Clock, int SettlementId, BattleSource Attacker) : DomainEvent;
public sealed record ForceEliminatedEvent(GameClock Clock, int ForceId, bool WasPlayerControlled) : DomainEvent;
public sealed record ForceLeaderChangedEvent(GameClock Clock, int ForceId, int? PreviousId, int OfficerId) : DomainEvent;

internal static class BattleAftermath
{
    internal static void TerminateUnit(WorldMutation world, int id, BattleSource? winner, SimulationRules rules, List<DomainEvent> events)
    {
        var unit = world.Units[id];
        if (winner is { } victor && !world.Forces[unit.OwnerId].IsEliminated)
            BattleLearning.DefeatedUnit(world, victor, events);
        var shape = MilitaryCombat.Area(unit, rules.Military!);
        ResolvePeople(world, p => p.UnitId == id, shape, unit, winner, unit.ServicePlaceId, rules, events);
        int? city = unit.ServicePlaceId is int home ?
            world.Settlements.GetValueOrDefault(home)?.Kind == SettlementKind.City ? home : rules.Personnel.RegionCities.GetValueOrDefault(home, -1) : null;
        if (city is int cityId && (!world.Settlements.TryGetValue(cityId, out var s) || s.Kind != SettlementKind.City)) city = null;
        UnitTerminationService.Complete(world, id, winner, city, rules.Governance, events, repairCorps: false);
    }

    internal static void ResolveSettlementDefeat(WorldMutation world, int id, BattleSource winner, SimulationRules rules, List<DomainEvent> events)
    {
        var settlement = world.Settlements[id];
        if (!MilitaryCombat.Defeated(settlement))
            throw new InvalidOperationException("Settlement has not fallen.");
        if (!winner.Exists(world) || !MilitaryDiplomacy.CanFight(world, winner.Owner(world), settlement.OwnerId)) throw new InvalidOperationException("Invalid settlement captor.");
        var definition = rules.Military!.Place(settlement);
        if (AttackRequests.Fulfill(world, winner.Owner(world), id, events))
            EconomicCycle.UpdateTrust(world, rules.Economy, events);
        if (!settlement.CanHostPersons)
        {
            if (!winner.IsSettlement) BattleLearning.DestroyedWork(world, winner.Id, definition, rules.Military, events);
            world.Replace(settlement with { IsDestroyed = true, OwnerId = null, CorpsId = null,
                Soldiers = 0, Wounded = 0, Morale = 0, Durability = 0, ConfusionDaysRemaining = 0 });
            MilitaryPolicies.RemoveTarget(world, id, true, events);
            events.Add(new FacilityDestroyedEvent(world.Clock, id, winner));
            return;
        }
        bool destroyed = settlement.Durability == 0 && definition.DestroyedAtZeroDurability;
        if (settlement.OwnerId is int previousOwner && !world.Forces[previousOwner].IsEliminated)
            BattleLearning.CapturedSettlement(world, winner, rules.Military, events);
        if (destroyed)
        {
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, id), ResourceKind.Soldiers, "settlement-destroyed", winner.Id,
                -(long)settlement.Soldiers, -(long)settlement.Soldiers, events);
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, id), ResourceKind.Wounded, "settlement-destroyed", winner.Id,
                -(long)settlement.Wounded, -(long)settlement.Wounded, events);
        }
        var traveling = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
        ResolvePeople(world, p => p.SettlementId == id && !traveling.Contains(p.Id), new(definition.Position, definition.Footprint),
            null, winner, id, rules, events);
        if (settlement.OwnerId is int owner)
        {
            BattleTreasury.Transfer(world, owner, settlement.CorpsId, BattleTreasury.Weight(settlement), winner, id,
                "settlement-captured", events);
        }
        int publicOrder = destroyed ? settlement.PublicOrder : CityBattleEffects.CapturedPublicOrder(world, settlement, winner.Owner(world), events);
        world.Replace(settlement with { PublicOrder = publicOrder, OwnerId = destroyed ? null : winner.Owner(world), CorpsId = destroyed ? null : winner.Corps(world),
            Facility = settlement.Facility is { } instance ? instance with { BuilderUnitId = null } : null,
            ConfusionDaysRemaining = 0,
            IsDestroyed = destroyed, Soldiers = destroyed ? 0 : settlement.Soldiers, Wounded = destroyed ? 0 : settlement.Wounded,
            Morale = destroyed ? 0 : Math.Max(1, winner.Morale(world)), Durability = destroyed ? 0 :
                Math.Max(settlement.Durability, Math.Max(1, settlement.MaxDurability / 4)) });
        foreach (var task in world.Simulation.Tasks.Where(t => t.OriginSettlementId == id || t.Journey?.DestinationPlaceId == id)
            .OrderBy(t => t.Sequence).ThenBy(t => t.Id).ToArray())
        {
            TaskCancellation.Apply(world, task, "settlement-captured", events);
            if (task.Journey is not null) BattlePersonnel.ResumeReturn(world, task, rules, events);
        }
        winner.Reward(world, 0, 10);
        MilitaryPolicies.SettlementChanged(world, id, rules.Military!, events);
        events.Add(new SettlementCapturedEvent(world.Clock, id, settlement.OwnerId, winner, destroyed));
    }

    private static void ResolvePeople(WorldMutation world, Func<OfficerState, bool> located,
        MapOccupancy from, UnitState? defeatedUnit, BattleSource? winner, int? fallbackPlace, SimulationRules rules, List<DomainEvent> events)
    {
        var military = rules.Military!;
        foreach (var original in world.Officers.Values.Where(located).OrderBy(p => p.Id).ToArray())
        {
            int? excluded = defeatedUnit is null ? fallbackPlace : null;
            int? retreat = BattleRetreat.Destination(world, original, from.Position, excluded, military)?.Id;
            bool hasRetreatCity = BattleRetreat.Destination(world, original, from.Position, excluded, military, citiesOnly: true) is not null;
            if (original.IsServing && winner is { } captor)
            {
                int nearby = BattleRetreat.CaptureParticipants(world,
                    new(defeatedUnit?.Id ?? fallbackPlace!.Value, defeatedUnit is null), captor.Owner(world), from, military);
                bool mount = world.Treasures.Values.Any(t => t.HolderId == original.Id && t.Discovery != TreasureDiscovery.Disabled &&
                    rules.Treasures.Items.TryGetValue(t.Id, out var treasure) && treasure.EscapeMount);
                var rank = original.UnitId is null ? BattleRank.Middle :
                    world.Units[original.UnitId.Value].MemberRanks.TryGetValue(original.Id, out var memberRank) ? memberRank :
                    throw new InvalidOperationException("Missing field member rank.");
                bool doubles = !captor.IsSettlement && military.Formations[world.Units[captor.Id].FormationId!.Value].DoublesCapture;
                BattlePersonnel.Resolve(world, original.Id, captor, retreat, nearby, rank, mount, doubles,
                    rules.Personnel, events, hasRetreatCity, from.Position, military);
            }
            else
            {
                PersonnelTransitions.CancelMissions(world, original.Id, "battle-location-lost", events, rules.Personnel, true);
                var person = world.Officers[original.Id];
                if (winner is { } recipient)
                {
                    if (person.Status == OfficerStatus.Captive && (defeatedUnit is not null || person.OwnerId == recipient.Owner(world)))
                    {
                        if (retreat is int home) BattlePersonnel.Escape(world, person.Id, home, rules.Personnel, events, from.Position, military);
                        else BattlePersonnel.Displace(world, person.Id, from.Position, rules.Personnel, military, events);
                    }
                    else if (person.Status == OfficerStatus.Captive)
                        world.Replace(person with { UnitId = recipient.IsSettlement ? null : recipient.Id,
                            SettlementId = recipient.IsSettlement ? recipient.Id : null, RegionId = null, IsCommander = false });
                    else Detach(world, person, fallbackPlace, events);
                }
                else if (retreat is int home && person.OwnerId is not null && (person.IsServing || person.Status == OfficerStatus.Captive))
                    BattlePersonnel.Escape(world, person.Id, home, rules.Personnel, events, from.Position, military);
                else if (person.IsServing || person.Status == OfficerStatus.Captive)
                    BattlePersonnel.Displace(world, person.Id, from.Position, rules.Personnel, military, events);
                else Detach(world, person, fallbackPlace, events);
            }
        }
    }

    internal static void Detach(WorldMutation world, OfficerState person, int? place, List<DomainEvent> events)
    {
        if (person.OwnerId is int owner && world.Forces[owner].AdviserId == person.Id)
            world.Replace(world.Forces[owner] with { AdviserId = null });
        var status = person.IsServing || person.Status == OfficerStatus.Captive ? OfficerStatus.Free : person.Status;
        world.Replace(person with { OwnerId = null, Status = status, UnitId = null, SettlementId = place,
            RegionId = null, OfficeId = null, IsCommander = false, Acted = true,
            StatusStartYear = status == person.Status ? person.StatusStartYear : world.Clock.Year });
        events.Add(new PersonAffiliationChangedEvent(world.Clock, person.Id, person.OwnerId, null, place ?? -1));
    }

    internal static void Reconcile(WorldMutation world, IReadOnlySet<int> affectedForces, SimulationRules rules, List<DomainEvent> events, string reason = "battle")
    {
        foreach (var force in world.Forces.Values.Where(f => !f.IsEliminated && affectedForces.Contains(f.Id)).OrderBy(f => f.Id).ToArray())
        {
            var serving = world.Officers.Values.Where(p => p.OwnerId == force.Id && p.IsServing).OrderBy(p => p.Id).ToArray();
            if (!ForceLifecycle.HasCity(world, force.Id) || serving.Length == 0)
            {
                Eliminate(world, force.Id, rules, events);
                continue;
            }
            if (force.LeaderId is not int leader || !world.Officers.TryGetValue(leader, out var ruler) ||
                ruler.OwnerId != force.Id || !ruler.IsServing && ruler.Status != OfficerStatus.Captive)
            {
                var successor = rules.Governance.SelectSuccessor(serving, force.LeaderId ?? serving[0].Id, world.AbilitiesFor(serving)) ?? serving[0];
                world.Replace(world.Forces[force.Id] with { LeaderId = successor.Id, AdviserId = force.AdviserId == successor.Id ? null : world.Forces[force.Id].AdviserId });
                world.Replace(successor with { Status = OfficerStatus.Ruler, OfficeId = null });
                events.Add(new ForceLeaderChangedEvent(world.Clock, force.Id, force.LeaderId, successor.Id));
            }
        }
        foreach (var corps in world.Corps.Values.OrderBy(c => c.Id).ToArray())
        {
            var people = world.Officers.Values.Where(p => p.OwnerId == corps.OwnerId && p.IsServing && PersonnelLocations.Corps(world, p) == corps.Id).ToArray();
            if (corps.Treasury is null)
            {
                if (world.Forces[corps.OwnerId].LeaderId is int ruler) world.Replace(corps with { LeaderId = ruler });
                continue;
            }
            if (people.Length > 0 && world.Settlements.Values.Any(s => s.CorpsId == corps.Id && s.Kind == SettlementKind.City))
            {
                if (GovernanceMaintenance.RepairCorps(world, corps.Id, rules.Governance, events) != CommandRejection.None)
                    throw new InvalidOperationException("Cannot repair surviving battle corps.");
                continue;
            }
            GovernanceMaintenance.DissolveForced(world, corps.Id, reason, events);
        }
    }

    private static void Eliminate(WorldMutation world, int forceId, SimulationRules rules, List<DomainEvent> events)
    {
        var force = world.Forces[forceId];
        ForceTransfer.CancelTasks(world, forceId, rules, "force-eliminated", events);
        foreach (var person in world.Officers.Values.Where(p => p.OwnerId == forceId).OrderBy(p => p.Id).ToArray())
        {
            if (force.IsBarbarian && person.Status != OfficerStatus.Captive)
            {
                OfficerDeath.Remove(world, person.Id, OfficerDeathCause.ForceEliminated, rules.Personnel, events, true);
                OfficerDeath.SettleTreasures(world, person.Id, null, events);
                continue;
            }
            if (person.IsServing && person.UnitId is int fieldUnit)
            {
                BattlePersonnel.Displace(world, person.Id, world.Units[fieldUnit].Position, rules.Personnel, rules.Military!, events);
                continue;
            }
            PersonnelTransitions.CancelMissions(world, person.Id, "force-eliminated", events, rules.Personnel, true);
            var p = world.Officers[person.Id];
            world.Replace(p with { OwnerId = null, Status = p.Status == OfficerStatus.Captive ? OfficerStatus.Captive : OfficerStatus.Free,
                UnitId = p.Status == OfficerStatus.Captive ? p.UnitId : null, OfficeId = null, IsCommander = false, Acted = true,
                StatusStartYear = p.Status == OfficerStatus.Captive ? p.StatusStartYear : world.Clock.Year });
        }
        foreach (var unit in world.Units.Values.Where(u => u.OwnerId == forceId).ToArray())
        {
            // Remaining occupants are prisoners; the shared defeat path releases and places them
            // by their own allegiance before deleting the detention unit.
            TerminateUnit(world, unit.Id, null, rules, events);
        }
        foreach (var settlement in world.Settlements.Values.Where(s => s.OwnerId == forceId).ToArray())
            world.Replace(settlement with { OwnerId = null, CorpsId = null });
        foreach (var corps in world.Corps.Values.Where(c => c.OwnerId == forceId).ToArray())
        {
            if (corps.Treasury is not null)
                foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
                    ResourceAccounts.Change(world, new(ResourceOwnerKind.Corps, corps.Id), resource,
                        -(long)ResourceAccounts.Balance(world, new(ResourceOwnerKind.Corps, corps.Id), resource), "force-eliminated", null, events);
            world.RemoveCorps(corps.Id);
        }
        ForceLifecycle.Retire(world, forceId, events);
    }
}
