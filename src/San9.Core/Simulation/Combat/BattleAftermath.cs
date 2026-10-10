using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SettlementCapturedEvent(GameClock Clock, int SettlementId, int? PreviousOwnerId, BattleSource Winner, bool Destroyed) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired] public required BattleObjectContext Source { get; init; }
}
public sealed record FacilityDestroyedEvent(GameClock Clock, int SettlementId, BattleSource Attacker) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired] public required BattleObjectContext Source { get; init; }
}
public sealed record ForceEliminatedEvent(GameClock Clock, int ForceId, bool WasPlayerControlled) : DomainEvent;
public sealed record ForceLeaderChangedEvent(GameClock Clock, int ForceId, int? PreviousId, int OfficerId) : DomainEvent;

internal enum UnitDefeatCause { Other, DoubledMartialTactic }

internal static class BattleAftermath
{
    internal static void TerminateUnit(WorldMutation world, int id, BattleSource? winner, SimulationRules rules, List<DomainEvent> events,
        UnitDefeatCause cause = UnitDefeatCause.Other)
    {
        var unit = world.Units[id];
        int? victorForce = winner is { } winningSource ? winningSource.Owner(world) : null;
        if (winner is { } victor && !world.Forces[unit.OwnerId].IsEliminated)
            BattleLearning.DefeatedUnit(world, victor, events);
        if (cause == UnitDefeatCause.DoubledMartialTactic && winner is { } source && source.Exists(world))
            MeleeDeaths.Apply(world, unit, rules, events);
        var shape = MilitaryCombat.Area(unit, rules.Military!);
        ResolvePeople(world, world.UnitMembers(id), shape, unit, winner, unit.ServicePlaceId, rules, events);
        int? city = unit.ServicePlaceId is int home ?
            world.Settlements.GetValueOrDefault(home)?.Kind == SettlementKind.City ? home : rules.Personnel.RegionCities.GetValueOrDefault(home, -1) : null;
        if (city is int cityId && (!world.Settlements.TryGetValue(cityId, out var s) || s.Kind != SettlementKind.City)) city = null;
        UnitTerminationService.Complete(world, id, winner, city, rules.Governance, events, repairCorps: false,
            target: BattleObjectContext.Capture(unit));
        if (victorForce is int victorId) EliminateIfDefeated(world, unit.OwnerId, rules, events, world.CurrentDateCommandsConsumed, victorId);
    }

    internal static void ResolveSettlementDefeat(WorldMutation world, int id, BattleSource winner, SimulationRules rules, List<DomainEvent> events)
        => ResolveSettlementDefeats(world, [id], winner, rules, events);

    internal static void ResolveSettlementDefeats(WorldMutation world, IReadOnlyList<int> ids, BattleSource winner,
        SimulationRules rules, List<DomainEvent> events)
    {
        if (ids.Count == 0) return;
        // Keep the source alive until every target of one combat effect has settled.
        int victor = winner.Owner(world);
        var previousOwners = ids.Select(id => world.Settlements[id].OwnerId).OfType<int>().Distinct().Order().ToArray();
        var captures = ids.Select(id => (Id: id, Units: CaptureUnits(world, id, winner, rules.Military!))).ToArray();
        foreach (var capture in captures)
            CaptureSettlement(world, capture.Id, winner, capture.Units, rules, events);
        foreach (int owner in previousOwners) EliminateIfDefeated(world, owner, rules, events, world.CurrentDateCommandsConsumed, victor);
        foreach (var capture in captures.Where(c => !world.Settlements[c.Id].IsDestroyed))
            foreach (int unitId in capture.Units)
                if (world.Units.TryGetValue(unitId, out var unit))
                    MilitaryMovement.Absorb(world, unit, capture.Id, rules.Governance, rules.Personnel, rules.Military!, events);
    }

    private static int[] CaptureUnits(WorldMutation world, int id, BattleSource winner, MilitaryRulebook military)
    {
        var area = new BattleSource(id, true).Area(world, military);
        int owner = winner.Owner(world);
        int? corps = winner.Corps(world);
        var candidates = world.Units.Values.Where(u => u.OwnerId == owner && u.CorpsId == corps &&
            !u.IsEmbedded && !u.IsPersonnel && u.Soldiers > 0 && u.Morale > 0 &&
            u.EffectiveOrder is { Kind: MilitaryOrderKind.AttackSettlement } order && order.SettlementId == id &&
            MilitaryCombat.Area(u, military).IntersectsExpanded(area, 5)).OrderBy(u => u.Id).Select(u => u.Id).ToArray();
        return candidates.Length == 0 && !winner.IsSettlement ? [winner.Id] : candidates;
    }

    private static void CaptureSettlement(WorldMutation world, int id, BattleSource winner, int[] candidates,
        SimulationRules rules, List<DomainEvent> events)
    {
        var settlement = world.Settlements[id];
        if (!MilitaryCombat.Defeated(settlement))
            throw new InvalidOperationException("Settlement has not fallen.");
        if (!winner.Exists(world) || !MilitaryDiplomacy.CanFight(world, winner.Owner(world), settlement.OwnerId)) throw new InvalidOperationException("Invalid settlement captor.");
        var definition = rules.Military!.Place(settlement);
        AttackRequests.Fulfill(world, winner.Owner(world), id, events);
        if (!settlement.CanHostPersons)
        {
            if (!winner.IsSettlement) BattleLearning.DestroyedWork(world, winner.Id, definition, rules.Military, events);
            world.Replace(settlement with { IsDestroyed = true, OwnerId = null, CorpsId = null,
                Soldiers = 0, Wounded = 0, Morale = 0, Durability = 0, ConfusionDaysRemaining = 0 });
            MilitaryPolicies.RemoveTarget(world, id, true, events);
            events.Add(new FacilityDestroyedEvent(world.Clock, id, winner) { Source = BattleObjectContext.Capture(world, winner) });
            return;
        }
        bool destroyed = settlement.Durability == 0 && definition.DestroyedAtZeroDurability;
        if (settlement.OwnerId is int previousOwner && !world.Forces[previousOwner].IsEliminated)
            BattleLearning.CapturedSettlement(world, winner, rules.Military, events);
        if (destroyed)
        {
            if (candidates.Length > 0)
            {
                int recipient = candidates.Length == 1 ? candidates[0] :
                    candidates[(int)(world.NextRandom("battle-capture-wounded") % (uint)candidates.Length)];
                long pool = (long)settlement.Soldiers + settlement.Wounded;
                uint accepted = (uint)Math.Min(pool, MilitaryRulebook.WoundedCapacity - world.Units[recipient].Wounded);
                new BattleSource(recipient, false).Reward(world, accepted, 0);
                ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, recipient), ResourceKind.Wounded,
                    "settlement-destroyed-recovery", id, pool, accepted, events);
            }
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, id), ResourceKind.Soldiers, "settlement-destroyed", winner.Id,
                -(long)settlement.Soldiers, -(long)settlement.Soldiers, events);
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, id), ResourceKind.Wounded, "settlement-destroyed", winner.Id,
                -(long)settlement.Wounded, -(long)settlement.Wounded, events);
        }
        ResolvePeople(world, world.SettlementMembers(id).Where(p => !world.IsTraveling(p.Id)), rules.Military.BuildingArea(definition),
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
            Morale = destroyed ? 0 : settlement.Morale, Durability = destroyed ? 0 :
                Math.Max(settlement.Durability, Math.Max(1, settlement.MaxDurability / 4)) });
        foreach (var task in world.Simulation.Tasks.Where(t => t.OriginSettlementId == id || t.Journey?.DestinationPlaceId == id)
            .OrderBy(t => t.Sequence).ThenBy(t => t.Id).ToArray())
        {
            TaskCancellation.Apply(world, task, "settlement-captured", events);
            if (task.Journey is not null) BattlePersonnel.ResumeReturn(world, task, rules, events);
        }
        winner.Reward(world, 0, 10);
        MilitaryPolicies.SettlementChanged(world, id, rules.Military!, events);
        events.Add(new SettlementCapturedEvent(world.Clock, id, settlement.OwnerId, winner, destroyed)
            { Source = BattleObjectContext.Capture(world, winner) });
    }

    private static void ResolvePeople(WorldMutation world, IEnumerable<OfficerState> people,
        MapOccupancy from, UnitState? defeatedUnit, BattleSource? winner, int? fallbackPlace, SimulationRules rules, List<DomainEvent> events)
    {
        var military = rules.Military!;
        foreach (var original in people.OrderBy(p => p.Id).ToArray())
        {
            int? excluded = defeatedUnit is null ? fallbackPlace : null;
            int? retreat = BattleRetreat.Destination(world, original, from.Position, excluded, military)?.Id;
            bool hasRetreatCity = BattleRetreat.Destination(world, original, from.Position, excluded, military, citiesOnly: true) is not null;
            if (original.IsServing && winner is { } captor)
            {
                int nearby = BattleRetreat.CaptureParticipants(world,
                    new(defeatedUnit?.Id ?? fallbackPlace!.Value, defeatedUnit is null), captor.Owner(world), from, military);
                bool mount = world.HasEscapeMount(original.Id, rules.Treasures);
                var rank = original.UnitId is null ? BattleRank.Middle :
                    military.Rank(world.Units[original.UnitId.Value], original.Id);
                bool doubles = !captor.IsSettlement && military.Formation(world.Units[captor.Id]).DoublesCapture;
                BattlePersonnel.Resolve(world, original.Id, captor, retreat, nearby, rank, mount, doubles,
                    rules.Personnel, events, hasRetreatCity, from.Position, military);
            }
            else
            {
                PersonnelTransitions.CancelMissions(world, original.Id, "battle-location-lost", events, rules.Personnel, world.CurrentDateCommandsConsumed);
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

    internal static void Reconcile(WorldMutation world, IReadOnlySet<int> affectedForces, SimulationRules rules, List<DomainEvent> events, bool afterDailyCommands, string reason = "battle")
    {
        foreach (var force in world.Forces.Values.Where(f => !f.IsEliminated && affectedForces.Contains(f.Id)).OrderBy(f => f.Id).ToArray())
        {
            var serving = world.Officers.Values.Where(p => p.OwnerId == force.Id && p.IsServing).OrderBy(p => p.Id).ToArray();
            if (EliminateIfDefeated(world, force.Id, rules, events, afterDailyCommands)) continue;
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
            if (corps.IsRoot)
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

    private static bool EliminateIfDefeated(WorldMutation world, int forceId, SimulationRules rules,
        List<DomainEvent> events, bool afterDailyCommands, int? victorForceId = null)
    {
        if (world.Forces[forceId].IsEliminated) return true;
        if (ForceLifecycle.HasCity(world, forceId) && world.Officers.Values.Any(p => p.OwnerId == forceId && p.IsServing)) return false;
        Eliminate(world, forceId, rules, events, afterDailyCommands, victorForceId);
        return true;
    }

    private static void Eliminate(WorldMutation world, int forceId, SimulationRules rules, List<DomainEvent> events, bool afterDailyCommands, int? victorForceId)
    {
        var force = world.Forces[forceId];
        if (victorForceId is int victor)
            AllianceDiplomacy.CompleteSubjugation(world, forceId, victor, AllianceVictoryCause.ForceDefeated, events);
        ForceTransfer.CancelTasks(world, forceId, rules, "force-eliminated", events, afterDailyCommands);
        foreach (var person in world.Officers.Values.Where(p => p.OwnerId == forceId).OrderBy(p => p.Id).ToArray())
        {
            if (force.IsBarbarian && person.Status != OfficerStatus.Captive)
            {
                OfficerDeath.Remove(world, person.Id, OfficerDeathCause.ForceEliminated, rules.Personnel, events, afterDailyCommands);
                OfficerDeath.SettleTreasures(world, person.Id, null, events);
                continue;
            }
            if (person.IsServing && person.UnitId is int fieldUnit)
            {
                BattlePersonnel.Displace(world, person.Id, world.Units[fieldUnit].Position, rules.Personnel, rules.Military!, events);
                continue;
            }
            PersonnelTransitions.CancelMissions(world, person.Id, "force-eliminated", events, rules.Personnel, afterDailyCommands);
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
            foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
                    ResourceAccounts.Change(world, new(ResourceOwnerKind.Corps, corps.Id), resource,
                        -(long)ResourceAccounts.Balance(world, new(ResourceOwnerKind.Corps, corps.Id), resource), "force-eliminated", null, events);
            world.RemoveCorps(corps.Id);
        }
        ForceLifecycle.Retire(world, forceId, events);
    }
}
