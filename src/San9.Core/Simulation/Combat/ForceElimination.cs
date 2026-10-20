using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SettlementNeutralizedEvent(GameClock Clock, int SettlementId, int PreviousOwnerId) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired] public required SettlementState Result { get; init; }
}

/// <summary>Settles a defeated force's remaining assets before retiring its persistent identity.</summary>
internal static class ForceElimination
{
    private const string Reason = "force-eliminated";

    internal static void Apply(WorldMutation world, int forceId, SimulationRules rules, List<DomainEvent> events,
        bool afterDailyCommands, int? victorForceId)
    {
        var force = world.Forces[forceId];
        if (victorForceId is int victor)
            AllianceDiplomacy.CompleteSubjugation(world, forceId, victor, AllianceVictoryCause.ForceDefeated, events);
        ForceTransfer.CancelTasks(world, forceId, rules, Reason, events, afterDailyCommands);
        foreach (var person in world.Officers.Values.Where(p => p.OwnerId == forceId).OrderBy(p => p.Id).ToArray())
        {
            if (force.IsBarbarian && person.Status != OfficerStatus.Captive)
            {
                OfficerDeath.Remove(world, person.Id, OfficerDeathCause.ForceEliminated, rules.Personnel, events, afterDailyCommands);
                OfficerDeath.SettleTreasures(world, person.Id, null, events);
            }
            else if (person.IsServing)
            {
                int? place = person.UnitId is int unit ? rules.Military!.RegionAt(world.Units[unit].Position)
                    : person.SettlementId ?? person.RegionId;
                BattlePersonnel.DisplaceAtPlace(world, person.Id, place, rules.Personnel, events);
            }
            else
            {
                PersonnelTransitions.CancelMissions(world, person.Id, Reason, events, rules.Personnel, afterDailyCommands);
                var current = world.Officers[person.Id];
                world.Replace(current with { OwnerId = null, OfficeId = null, IsCommander = false, Acted = true });
                events.Add(new PersonAffiliationChangedEvent(world.Clock, person.Id, forceId, null,
                    current.SettlementId ?? current.RegionId ?? -1));
            }
        }
        foreach (var unit in world.Units.Values.Where(u => u.OwnerId == forceId).OrderBy(u => u.Id).ToArray())
        {
            ReleaseCaptives(world, world.UnitMembers(unit.Id), rules.Military?.RegionAt(unit.Position), unit.Position, rules, events);
            int? city = rules.Military?.RegionAt(unit.Position) is int region ? Demobilization.City(world, region, rules.Personnel) : null;
            UnitTerminationService.Demobilize(world, unit.Id, unit.IsPersonnel ? null : city, events);
        }
        foreach (int id in world.Settlements.Values.Where(s => s.OwnerId == forceId).OrderBy(s => s.Id).Select(s => s.Id).ToArray())
        {
            ReleaseCaptives(world, world.SettlementMembers(id), id, null, rules, events);
            var place = world.Settlements[id];
            if (Demobilization.City(world, id, rules.Personnel) is int city)
                Demobilization.Return(world, city, (long)place.Soldiers + place.Wounded, Reason, id, events);
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, id), ResourceKind.Soldiers,
                Reason, forceId, -(long)place.Soldiers, -(long)place.Soldiers, events);
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, id), ResourceKind.Wounded,
                Reason, forceId, -(long)place.Wounded, -(long)place.Wounded, events);
            // Read after population recovery: a remaining city can itself be the destination pool.
            var result = world.Settlements[id] with { OwnerId = null, CorpsId = null, Soldiers = 0, Wounded = 0,
                Morale = 0, ConfusionDaysRemaining = 0, CombatTargetUnitId = null };
            world.Replace(result);
            MilitaryPolicies.RemoveTarget(world, id, true, events);
            events.Add(new SettlementNeutralizedEvent(world.Clock, id, forceId) { Result = result });
        }
        foreach (var corps in world.Corps.Values.Where(c => c.OwnerId == forceId).OrderBy(c => c.Id).ToArray())
        {
            foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
                ResourceAccounts.Change(world, new(ResourceOwnerKind.Corps, corps.Id), resource,
                    -(long)ResourceAccounts.Balance(world, new(ResourceOwnerKind.Corps, corps.Id), resource), Reason, null, events);
            world.RemoveCorps(corps.Id);
        }
        ForceLifecycle.Retire(world, forceId, events);
    }

    private static void ReleaseCaptives(WorldMutation world, IEnumerable<OfficerState> occupants, int? origin,
        WorldPosition? position, SimulationRules rules, List<DomainEvent> events)
    {
        foreach (var person in occupants.Where(p => p.Status == OfficerStatus.Captive).OrderBy(p => p.Id).ToArray())
        {
            if (origin is int place && rules.Personnel.Places.ContainsKey(place) &&
                PrisonerRelease.Depart(world, person.Id, place, world.Simulation.NextSequence,
                    PrisonerDepartureKind.Displaced, null, rules.Personnel, rules.Economy, events, position) == CommandRejection.None)
            {
                world.SetSimulation(world.Simulation with { NextSequence = checked(world.Simulation.NextSequence + 1) });
                continue;
            }
            BattlePersonnel.DisplaceAtPlace(world, person.Id, origin, rules.Personnel, events);
        }
    }
}
