using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record OfficerEscapedBattleEvent(GameClock Clock, int OfficerId, int? PreviousUnitId, int SettlementId) : DomainEvent;

internal static class BattlePersonnel
{
    // The city probability gate and the actual return destination are separate inputs.
    internal static bool Resolve(WorldMutation world, int officerId, BattleSource captor, int? retreatSettlementId,
        int nearbyAttackers, BattleRank rank, bool hasEscapeMount, bool doublesCapture,
        PersonnelRulebook places, List<DomainEvent> events,
        bool hasRetreatCity, WorldPosition origin, MilitaryRulebook military)
    {
        if (!world.Officers.TryGetValue(officerId, out var person) || !person.IsServing || person.OwnerId is null ||
            !captor.Exists(world) || captor.Owner(world) == person.OwnerId)
            throw new ArgumentException("Invalid battle personnel participants.");
        if (retreatSettlementId is int id && (!world.Settlements.TryGetValue(id, out var retreat) ||
            retreat.OwnerId != person.OwnerId || !retreat.CanHostPersons || retreat.Durability <= 0))
            throw new ArgumentException("Invalid retreat destination.");
        int chance = BattleCaptureRules.Chance(world.Abilities(person).War, nearbyAttackers, rank,
            hasRetreatCity, hasEscapeMount, doublesCapture);
        bool exempt = world.Forces[person.OwnerId.Value].IsBarbarian || world.Forces[captor.Owner(world)].IsBarbarian ||
            captor.IsSettlement && !world.Officers.Values.Any(p => p.IsServing && p.OwnerId == captor.Owner(world) &&
                p.SettlementId == captor.Id && !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id && t.Journey is not null));
        bool captured = !exempt && (chance == 100 || chance > 0 && (int)(world.NextRandom("battle-personnel-capture") % 100) < chance);
        if (captured)
            PersonnelTransitions.Capture(world, officerId, captor.IsSettlement ? captor.Id : null,
                captor.IsSettlement ? null : captor.Id, places, events);
        else
        {
            if (retreatSettlementId is int destination) Escape(world, officerId, destination, places, events, origin, military);
            else
            {
                Displace(world, officerId, origin, places, military, events);
            }
        }
        return captured;
    }

    internal static void Displace(WorldMutation world, int officerId, WorldPosition origin, PersonnelRulebook places,
        MilitaryRulebook military, List<DomainEvent> events)
    {
        var person = world.Officers[officerId];
        int? originPlace = person.UnitId is null ? person.SettlementId ?? person.RegionId : military.RegionAt(origin);
        bool dead = person.OwnerId is int owner && world.Forces[owner].IsBarbarian;
        if (dead)
        {
            OfficerDeath.Remove(world, officerId, OfficerDeathCause.NoRetreat, places, events, true);
            OfficerDeath.SettleTreasures(world, officerId, null, events);
            return;
        }
        var candidates = originPlace is int place ? PersonnelExile.Destinations(world, place, places) : [];
        int? destination = candidates.Length == 0 ? null : candidates[world.NextRandom("battle-personnel-placement") % (ulong)candidates.Length];
        PersonnelTransitions.CancelMissions(world, officerId, "battle-no-return-home", events, places, true);
        if (destination is not null && person.OwnerId is int previousOwner)
            RecruitmentRules.SetLoyalty(world, officerId, previousOwner, Math.Min(45, person.LoyaltyTo(previousOwner)), "battle-displacement", events);
        // An empty geographic pool has no random destination. Retain the last geographic location
        // while clearing references to the disappearing army; never invent a nearest-world teleport.
        int? location = destination ?? originPlace;
        if (location is int id) PersonnelTransitions.DiscoverDetached(world, officerId, id, events);
        else BattleAftermath.Detach(world, world.Officers[officerId], null, events);
        world.Replace(world.Officers[officerId] with { TacticCooldown = 0, Status = OfficerStatus.Free });
    }

    internal static void Escape(WorldMutation world, int officerId, int destination, PersonnelRulebook places,
        List<DomainEvent> events, WorldPosition origin, MilitaryRulebook military)
    {
        var person = world.Officers[officerId];
        int? corps = PersonnelLocations.Corps(world, person);
        if (!world.Settlements.TryGetValue(destination, out var home) || home.OwnerId != person.OwnerId ||
            home.IsDestroyed || !home.CanHostPersons || home.Durability == 0) throw new ArgumentException("Invalid escape destination.");
        PersonnelTransitions.CancelMissions(world, officerId, "person-escaped-battle", events, places, true);
        world.Replace(world.Officers[officerId] with { UnitId = null, SettlementId = destination, RegionId = null,
            Status = person.Status == OfficerStatus.Captive
                ? world.Forces[person.OwnerId!.Value].LeaderId == person.Id ? OfficerStatus.Ruler : OfficerStatus.Serving
                : person.Status, Acted = true, IsCommander = false,
            TacticCooldown = 0 });
        var cause = new PersistentTask(new Guid(Convert.FromHexString(CanonicalJson.Hash(new { OfficerId = officerId,
            world.Clock, Origin = origin, Destination = destination })).AsSpan(0, 16)), PersonnelReturns.Kind, "system",
            person.OwnerId!.Value, officerId, destination, world.Clock, world.Clock, 0, world.Simulation.NextSequence);
        world.SetSimulation(world.Simulation with { NextSequence = checked(world.Simulation.NextSequence + 1) });
        PersonnelReturns.Schedule(world, cause, officerId, destination, destination,
            BattleRetreat.Days(origin, military.Place(home).Position), true, events, new(origin, military.Place(home).Position, false), true, corps);
        events.Add(new OfficerEscapedBattleEvent(world.Clock, officerId, person.UnitId, destination));
    }

    internal static void ResumeReturn(WorldMutation world, PersistentTask task, SimulationRules rules, List<DomainEvent> events)
    {
        if (task.Journey?.ReturnLeg?.FieldTravel != true)
        {
            PersonnelReturns.Start(world, task, rules.Personnel, events, true);
            return;
        }
        var person = world.Officers[task.OfficerId];
        if (!person.IsServing || person.OwnerId != task.ForceId)
        {
            ReinforcementTransport.Disband(world, task, "reinforcement-executor-lost", events);
            return;
        }
        var origin = task.Journey.Route.Destination;
        var home = BattleRetreat.Destination(world, person, origin, task.Journey.DestinationPlaceId, rules.Military!,
            preferredCorps: task.Journey.ReturnLeg.CorpsId);
        if (home is null)
        {
            ReinforcementTransport.Disband(world, task, "reinforcement-no-home", events);
            Displace(world, person.Id, origin, rules.Personnel, rules.Military!, events);
            return;
        }
        int days = BattleRetreat.Days(origin, rules.Military!.Place(home).Position);
        int remaining = Math.Max(0, task.Journey.ReturnLeg.CountdownDays - task.ProgressDays - 1);
        if (days >= remaining) days -= remaining;
        PersonnelReturns.Schedule(world, task, person.Id, task.Journey.DestinationPlaceId, home.Id, days, true, events,
            new(origin, rules.Military.Place(home).Position, false), true,
            task.Journey.ReturnLeg.CorpsId);
    }
}
