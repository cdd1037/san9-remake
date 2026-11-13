using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ForcesConsolidatedEvent(GameClock Clock, int SurvivorId, int AbsorbedId,
    long MoneyRemoved, uint MoneyReceived, long FoodRemoved, uint FoodReceived) : DomainEvent;

internal static class ForceConsolidation
{
    private const string Reason = "force-consolidated";

    internal static long Strength(WorldMutation world, int force) => SoldierTotals.Force(world, force);

    internal static void Apply(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (world.Companion.DisableComputerForceConsolidation ||
            !world.Simulation.ForceConsolidationEnabled || !world.Simulation.ForceConsolidationPending) return;
        var players = world.Simulation.Controls.Where(c => c.Kind == ControllerKind.Player).Select(c => c.ForceId).ToHashSet();
        var forces = world.Forces.Values.Where(f => !f.IsEliminated && !f.IsBarbarian && f.LeaderId is not null).OrderBy(f => f.Id).ToArray();
        int Cities(int id) => world.Settlements.Values.Count(s => s.OwnerId == id && s.Kind == SettlementKind.City && !s.IsDestroyed);
        bool InAlliance(int id) => world.Simulation.Alliance is { } alliance &&
            (alliance.TargetForceId == id || alliance.MemberForceIds.Contains(id));
        var candidates = forces.Where(f => !players.Contains(f.Id) && Cities(f.Id) <= 4 && !InAlliance(f.Id)).ToArray();
        foreach (var player in forces.Where(f => players.Contains(f.Id) && Cities(f.Id) >= 2))
        {
            long limit = Strength(world, player.Id) / 2;
            foreach (var first in candidates)
            {
                long firstStrength = Strength(world, first.Id);
                if (firstStrength >= limit || !SurrenderDiplomacy.Neighbors(world, first.Id, player.Id, rules)) continue;
                foreach (var second in candidates)
                {
                    if (first.Id == second.Id) continue;
                    var firstLeader = world.Officers[first.LeaderId!.Value];
                    var secondLeader = world.Officers[second.LeaderId!.Value];
                    if (firstLeader.PrefersConsolidationLeadership && secondLeader.PrefersConsolidationLeadership) continue;
                    long secondStrength = Strength(world, second.Id);
                    if (secondStrength >= limit || !SurrenderDiplomacy.Neighbors(world, second.Id, first.Id, rules)) continue;
                    int relation = MilitaryDiplomacy.Relation(world, first.Id, second.Id);
                    int threshold = relation * relation / 100 - PersonnelRulebook.AffinityDistance(firstLeader, secondLeader);
                    if ((int)(world.NextRandom("event-force-consolidation") % 100) >= threshold) continue;
                    bool firstSurvives = firstLeader.PrefersConsolidationLeadership ||
                        !secondLeader.PrefersConsolidationLeadership && firstStrength > secondStrength;
                    Merge(world, firstSurvives ? first.Id : second.Id, firstSurvives ? second.Id : first.Id, rules, events);
                    world.SetSimulation(world.Simulation with { ForceConsolidationPending = false });
                    return;
                }
            }
        }
    }

    private static void Merge(WorldMutation world, int survivor, int absorbed, SimulationRules rules, List<DomainEvent> events)
    {
        var source = world.Forces[absorbed];
        var target = world.Forces[survivor];
        var leader = world.Officers[target.LeaderId!.Value];
        int? root = world.Corps.Values.Where(c => c.OwnerId == survivor && c.IsRoot).Select(c => (int?)c.Id).SingleOrDefault();
        ForceTransfer.CancelTasks(world, absorbed, rules, Reason, events, false);
        foreach (var person in world.Officers.Values.Where(p => p.OwnerId == absorbed).OrderBy(p => p.Id).ToArray())
        {
            int loyalty = person.Id == source.LeaderId ? 100 :
                Math.Clamp(person.Loyalty + (50 - PersonnelRulebook.AffinityDistance(person, leader)) / 2, 0, 255);
            PersonnelTransitions.TransferAllegiance(world, person.Id, survivor, loyalty, Place(world, person, rules),
                Reason, rules.Personnel, events);
        }
        world.Replace(world.Forces[absorbed] with { LeaderId = null, AdviserId = null });
        var transferredPlaces = ForceTransfer.Settlements(world, absorbed, survivor, root, events);
        var transferred = world.Units.Values.Where(u => u.OwnerId == absorbed).OrderBy(u => u.Id).ToArray();
        foreach (var original in transferred)
        {
            FacilityConstruction.Cancel(world, original.Id, events);
            var unit = world.Units[original.Id];
            var members = world.UnitMembers(unit.Id).Where(p => p.IsServing).Select(p => p.Id).Order().ToArray();
            world.Replace(unit with { OwnerId = survivor, CorpsId = root, CommanderId = unit.FormationId is null ? null :
                LeadershipMaintenance.SelectUnitCommander(world, members, rules.Governance),
                EngagementTargetId = null, ContactTarget = null, Lookahead = null });
            events.Add(new UnitTransferredEvent(world.Clock, unit.Id, absorbed, survivor));
        }
        foreach (var person in world.Officers.Values.Where(p => p.Status == OfficerStatus.Captive && p.OwnerId == survivor &&
            PrisonerCompensation.Captor(world, p) == survivor).OrderBy(p => p.Id).ToArray())
        {
            var position = person.UnitId is int id ? world.Units[id].Position : (WorldPosition?)null;
            int origin = Place(world, person, rules);
            if (PrisonerRelease.Depart(world, person.Id, origin, world.Simulation.NextSequence,
                PrisonerDepartureKind.Recovered, null, rules.Personnel, rules.Economy, events, position) != CommandRejection.None)
                throw new InvalidOperationException("Consolidation prisoner recovery failed.");
            world.SetSimulation(world.Simulation with { NextSequence = checked(world.Simulation.NextSequence + 1) });
        }
        var money = ForceTransfer.Treasury(world, absorbed, survivor, ResourceKind.Money, 1, Reason, null, events);
        var food = ForceTransfer.Treasury(world, absorbed, survivor, ResourceKind.Food, 1, Reason, null, events);
        foreach (var corps in world.Corps.Values.Where(c => c.OwnerId == absorbed).OrderBy(c => c.Id).ToArray())
        {
            world.RemoveCorps(corps.Id);
            events.Add(new CorpsDissolvedEvent(world.Clock, absorbed, corps.Id, root ?? -1));
        }
        ForceLifecycle.Retire(world, absorbed, events, survivor);
        foreach (var unit in transferred) MilitaryPolicies.AfterTransfer(world, unit.Id, rules.Military, events, absorbed);
        if (rules.Military is { } military)
        {
            foreach (int place in transferredPlaces) MilitaryOrders.SettlementChanged(world, place, military, events);
            foreach (var unit in transferred) MilitaryOrders.UnitChanged(world, unit.Id, military, events);
        }
        ForceTransfer.ReconcileOrders(world, rules.Military, events);
        world.Replace(world.Forces[survivor] with { BaseTrust = Math.Min(1000,
            Math.Max(source.BaseTrust, target.BaseTrust) + Math.Min(source.BaseTrust, target.BaseTrust) / 2 + 10) });
        foreach (var place in world.Settlements.Values.Where(s => s.OwnerId == survivor && !s.IsDestroyed).OrderBy(s => s.Id).ToArray())
        {
            uint soldiers = place.Kind == SettlementKind.City ? (uint)Math.Min((long)place.Soldiers + 10000,
                Math.Max(place.Soldiers, place.SoldierCapacity)) : place.Soldiers;
            world.Replace(place with { Soldiers = soldiers, Morale = 100 });
            if (place.Kind == SettlementKind.City) ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, place.Id),
                ResourceKind.Soldiers, Reason, survivor, 10000, (long)soldiers - place.Soldiers, events);
        }
        foreach (var unit in world.Units.Values.Where(u => u.OwnerId == survivor).OrderBy(u => u.Id).ToArray())
            world.Replace(unit with { Morale = 100 });
        CorpsDelegations.Cleanup(world, rules.Military, events);
        world.MaintainLeadership(events);
        events.Add(new ForcesConsolidatedEvent(world.Clock, survivor, absorbed, money.Removed, money.Received, food.Removed, food.Received));
    }

    private static int Place(WorldMutation world, OfficerState person, SimulationRules rules)
    {
        if (person.SettlementId is int home) return home;
        if (person.RegionId is int region) return region;
        if (person.UnitId is int id)
        {
            var position = world.Units[id].Position;
            if (rules.Military?.RegionAt(position) is int current && rules.Personnel.Places.ContainsKey(current)) return current;
            return rules.Personnel.Places.Values.OrderBy(p => MapCoordinates.Distance(position, p.Position)).ThenBy(p => p.Id).First().Id;
        }
        return world.Settlements.Values.Where(s => s.OwnerId == person.OwnerId && s.CanHostPersons && !s.IsDestroyed).OrderBy(s => s.Id).First().Id;
    }
}
