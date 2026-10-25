using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SettlementCapturedEvent(GameClock Clock, int SettlementId, int? PreviousOwnerId, BattleSource Winner, bool Destroyed) : DomainEvent
{
    /// <summary>The selected recipient, frozen before absorption or subsequent ownership changes.</summary>
    [System.Text.Json.Serialization.JsonRequired] public required BattleObjectContext Source { get; init; }
    /// <summary>The combat source that triggered defeat, which may belong to another force.</summary>
    [System.Text.Json.Serialization.JsonRequired] public required BattleObjectContext Attacker { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public required SettlementState Result { get; init; }
}
public sealed record FacilityDestroyedEvent(GameClock Clock, int SettlementId, BattleSource Attacker) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired] public required BattleObjectContext Source { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public required int? PreviousOwnerId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public required SettlementState Result { get; init; }
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
        if (cause == UnitDefeatCause.DoubledMartialTactic && winner is { } source && source.IsCombatant(world))
            MeleeDeaths.Apply(world, unit, rules, events);
        var shape = MilitaryCombat.Area(unit, rules.Military!);
        ResolvePeople(world, world.UnitMembers(id), shape, unit, winner, unit.ServicePlaceId, rules, events, BattleObjectContext.Capture(unit));
        int? city = unit.ServicePlaceId is int home ?
            world.Settlements.GetValueOrDefault(home)?.Kind == SettlementKind.City ? home : rules.Personnel.RegionCities.GetValueOrDefault(home, -1) : null;
        if (city is int cityId && (!world.Settlements.TryGetValue(cityId, out var s) || s.Kind != SettlementKind.City)) city = null;
        UnitTerminationService.Complete(world, id, winner, city, rules.Governance, events, repairCorps: false,
            target: BattleObjectContext.Capture(unit));
        if (victorForce is int victorId) GovernanceLifecycle.EliminateIfDefeated(world, unit.OwnerId, rules, events, world.CurrentDateCommandsConsumed, victorId);
    }

    internal static void ResolveSettlementDefeat(WorldMutation world, int id, BattleSource attacker, SimulationRules rules, List<DomainEvent> events)
        => ResolveSettlementDefeats(world, [id], attacker, rules, events);

    internal static void ResolveSettlementDefeats(WorldMutation world, IReadOnlyList<int> ids, BattleSource attacker,
        SimulationRules rules, List<DomainEvent> events)
    {
        if (ids.Count == 0) return;
        if (!attacker.IsCombatant(world) || ids.Any(id => !MilitaryCombat.Defeated(world.Settlements[id])))
            throw new InvalidOperationException("Invalid settlement defeat.");
        // Keep the source alive until every target of one combat effect has settled.
        List<(int Id, BattleSource Receiver, int? PreviousOwner, int Owner, int[] Units)> captures = [];
        foreach (int id in ids)
        {
            var place = world.Settlements[id];
            var candidates = SiegeContributions.Candidates(world, id, rules.Military!);
            bool removed = !place.CanHostPersons || place.Durability == 0 && rules.Military!.Place(place).DestroyedAtZeroDurability;
            if ((removed ? attacker : SiegeContributions.Receiver(world, id, attacker, candidates)) is not { } receiver) continue;
            int owner = receiver.Owner(world);
            int? corps = receiver.Corps(world);
            int[] units = candidates.Where(u => u.OwnerId == owner && u.CorpsId == corps).Select(u => u.Id).ToArray();
            if (units.Length == 0 && !receiver.IsSettlement) units = [receiver.Id];
            captures.Add((id, receiver, place.OwnerId, owner, units));
        }
        var previousOwners = captures.Select(c => c.PreviousOwner).OfType<int>().Distinct().Order().ToArray();
        foreach (var capture in captures)
            CaptureSettlement(world, capture.Id, capture.Receiver, attacker, capture.Units, rules, events);
        foreach (int owner in previousOwners)
            GovernanceLifecycle.EliminateIfDefeated(world, owner, rules, events, world.CurrentDateCommandsConsumed,
                captures.Last(c => c.PreviousOwner == owner).Owner);
        foreach (var capture in captures.Where(c => !world.Settlements[c.Id].IsDestroyed &&
            world.Settlements[c.Id].OwnerId == c.Owner))
            foreach (int unitId in capture.Units)
                if (world.Units.TryGetValue(unitId, out var unit))
                    MilitaryMovement.Absorb(world, unit, capture.Id, rules.Governance, rules.Personnel, rules.Military!, events);
        GovernanceLifecycle.Reconcile(world, previousOwners.ToHashSet(), rules, events, world.CurrentDateCommandsConsumed);
        foreach (var capture in captures) MilitaryOrders.SettlementChanged(world, capture.Id, rules.Military!, events);
        foreach (var capture in captures) UnitDisplacement.Resolve(world, capture.Id, rules.Military!, events);
    }

    private static void CaptureSettlement(WorldMutation world, int id, BattleSource winner, BattleSource attacker, int[] candidates,
        SimulationRules rules, List<DomainEvent> events)
    {
        var settlement = world.Settlements[id];
        var defeatedContext = BattleObjectContext.Capture(world, new(id, true));
        if (!MilitaryCombat.Defeated(settlement))
            throw new InvalidOperationException("Settlement has not fallen.");
        var definition = rules.Military!.Place(settlement);
        if (!winner.IsCombatant(world) || !(MilitaryDiplomacy.CanFight(world, winner.Owner(world), settlement.OwnerId) ||
            winner.Owner(world) == settlement.OwnerId && definition.FacilityTypeId is int type && rules.Military.Facilities[type].IsWork))
            throw new InvalidOperationException("Invalid settlement captor.");
        AttackRequests.Fulfill(world, winner.Owner(world), id, events);
        if (!settlement.CanHostPersons)
        {
            if (!winner.IsSettlement) BattleLearning.DestroyedWork(world, winner.Id, definition, rules.Military, events);
            world.Replace(settlement with { IsDestroyed = true, OwnerId = null, CorpsId = null,
                Soldiers = 0, Wounded = 0, Morale = 0, Durability = 0, ConfusionDaysRemaining = 0 });
            MilitaryOrders.RemoveTarget(world, id, true, events);
            events.Add(new FacilityDestroyedEvent(world.Clock, id, winner)
                { Source = BattleObjectContext.Capture(world, winner), PreviousOwnerId = settlement.OwnerId,
                    Result = world.Settlements[id] });
            return;
        }
        bool destroyed = settlement.Durability == 0 && definition.DestroyedAtZeroDurability;
        bool abandoned = !destroyed && world.Forces[winner.Owner(world)].IsBarbarian;
        bool cleared = destroyed || abandoned;
        if (settlement.OwnerId is int previousOwner && !world.Forces[previousOwner].IsEliminated)
            BattleLearning.CapturedSettlement(world, attacker, rules.Military, events);
        if (cleared)
        {
            string reason = abandoned ? "settlement-abandoned" : "settlement-destroyed";
            if (candidates.Length > 0)
            {
                int recipient = candidates.Length == 1 ? candidates[0] :
                    candidates[(int)(world.NextRandom("battle-capture-wounded") % (uint)candidates.Length)];
                long pool = (long)settlement.Soldiers + settlement.Wounded;
                uint accepted = (uint)Math.Min(pool, MilitaryRulebook.WoundedCapacity - world.Units[recipient].Wounded);
                new BattleSource(recipient, false).Reward(world, accepted, 0);
                ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, recipient), ResourceKind.Wounded,
                    reason + "-recovery", id, pool, accepted, events);
            }
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, id), ResourceKind.Soldiers, reason, winner.Id,
                -(long)settlement.Soldiers, -(long)settlement.Soldiers, events);
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, id), ResourceKind.Wounded, reason, winner.Id,
                -(long)settlement.Wounded, -(long)settlement.Wounded, events);
        }
        ResolvePeople(world, world.SettlementMembers(id).Where(p => !world.IsTraveling(p.Id)), rules.Military.BuildingArea(definition),
            null, winner, id, rules, events, defeatedContext, !cleared && settlement.Durability > 0);
        if (settlement.OwnerId is int owner)
        {
            BattleTreasury.Transfer(world, owner, settlement.CorpsId, BattleTreasury.Weight(settlement), winner, new(id, true),
                "settlement-captured", events);
        }
        int publicOrder = cleared ? settlement.PublicOrder : CityBattleEffects.CapturedPublicOrder(world, settlement, winner.Owner(world), events);
        world.Replace(settlement with { SiegeContributions = null, PublicOrder = publicOrder, OwnerId = cleared ? null : winner.Owner(world), CorpsId = cleared ? null : winner.Corps(world),
            Facility = settlement.Facility is { } instance ? instance with { BuilderUnitId = null } : null,
            ConfusionDaysRemaining = 0,
            IsDestroyed = destroyed, Soldiers = cleared ? 0 : settlement.Soldiers, Wounded = cleared ? 0 : settlement.Wounded,
            Morale = cleared ? 0 : settlement.Morale, Durability = destroyed ? 0 :
                Math.Max(settlement.Durability, Math.Max(1, settlement.MaxDurability / 4)) });
        SettlementTasks.LocationChanged(world, id, "settlement-captured", rules, events);
        attacker.Reward(world, 0, 10);
        if (abandoned)
            foreach (var unit in world.Units.Values.Where(u => u.OwnerId == winner.Owner(world) && u.CorpsId == winner.Corps(world) &&
                u.Order is { Kind: MilitaryOrderKind.AttackSettlement } order && order.SettlementId == id).OrderBy(u => u.Id).ToArray())
                MilitaryOrders.Return(world, unit, rules.Military!, events, "settlement-abandoned");
        events.Add(new SettlementCapturedEvent(world.Clock, id, settlement.OwnerId, winner, destroyed)
            { Source = BattleObjectContext.Capture(world, winner), Attacker = BattleObjectContext.Capture(world, attacker), Result = world.Settlements[id] });
    }

    private static void ResolvePeople(WorldMutation world, IEnumerable<OfficerState> people,
        MapOccupancy from, UnitState? defeatedUnit, BattleSource? winner, int? fallbackPlace, SimulationRules rules, List<DomainEvent> events,
        BattleObjectContext defeated, bool canRecoverInPlace = false)
    {
        var military = rules.Military!;
        var participants = people.OrderBy(p => p.Id).ToArray();
        var captorContext = winner is { } winning ? BattleObjectContext.Capture(world, winning) : null;
        int firstFact = events.Count;
        foreach (var original in participants)
        {
            int? excluded = defeatedUnit is null ? fallbackPlace : null;
            int? retreat = BattleRetreat.Destination(world, original, from.Position, excluded, military)?.Id;
            bool hasRetreatCity = BattleRetreat.Destination(world, original, from.Position, excluded, military, citiesOnly: true) is not null;
            if (original.IsServing && winner is { } captor)
            {
                if (defeatedUnit is null && fallbackPlace is int lost && world.Settlements[lost].Kind == SettlementKind.City &&
                    original.OwnerId is int defender && world.Forces[defender].IsBarbarian)
                {
                    OfficerDeath.Remove(world, original.Id, OfficerDeathCause.NoRetreat, rules.Personnel, events, world.CurrentDateCommandsConsumed);
                    OfficerDeath.SettleTreasures(world, original.Id, null, events);
                    continue;
                }
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
                    if (person.Status == OfficerStatus.Captive)
                    {
                        bool ruler = person.OwnerId is int force && world.Forces[force].LeaderId == person.Id;
                        int? root = world.Corps.Values.SingleOrDefault(c => c.OwnerId == person.OwnerId && c.IsRoot)?.Id;
                        if (canRecoverInPlace && person.OwnerId == recipient.Owner(world) &&
                            (!ruler || (recipient.Corps(world) ?? root) == root))
                        {
                            world.Replace(person with { Status = ruler ? OfficerStatus.Ruler : OfficerStatus.Serving,
                                UnitId = null, SettlementId = fallbackPlace, RegionId = null, OfficeId = null,
                                IsCommander = false, Acted = true, TacticCooldown = 0, StatusStartYear = world.Clock.Year });
                            events.Add(new PrisonerRecoveredEvent(world.Clock, person.Id, person.OwnerId!.Value, fallbackPlace!.Value));
                        }
                        else if (retreat is int home) BattlePersonnel.Escape(world, person.Id, home, rules.Personnel, events, from.Position, military);
                        else BattlePersonnel.Displace(world, person.Id, from.Position, rules.Personnel, military, events);
                    }
                    else Detach(world, person, fallbackPlace, events);
                }
                else if (retreat is int home && person.OwnerId is not null && (person.IsServing || person.Status == OfficerStatus.Captive))
                    BattlePersonnel.Escape(world, person.Id, home, rules.Personnel, events, from.Position, military);
                else if (person.IsServing || person.Status == OfficerStatus.Captive)
                    BattlePersonnel.Displace(world, person.Id, from.Position, rules.Personnel, military, events);
                else Detach(world, person, fallbackPlace, events);
            }
        }
        if (captorContext is not null)
        {
            var facts = events.Skip(firstFact).ToArray();
            var members = participants.Where(p => p.IsServing).Select(p => p.Id).ToHashSet();
            var captured = facts.OfType<OfficerCapturedEvent>().Where(e => members.Contains(e.OfficerId)).Select(e => e.OfficerId).ToImmutableArray();
            var retreated = facts.OfType<OfficerEscapedBattleEvent>().Where(e => members.Contains(e.OfficerId)).Select(e => e.OfficerId).ToImmutableArray();
            int? last = participants.LastOrDefault(p => p.IsServing)?.Id;
            if (captured.Length + retreated.Length > 0)
                events.Add(new BattlePersonnelResolvedEvent(world.Clock, defeated, captorContext, captured, retreated,
                    last is int id && captured.Contains(id) ? id : null));
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

}
