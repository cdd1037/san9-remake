using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Transfers an army without conflating its physical location with its service settlement.</summary>
internal static class UnitAffiliation
{
    internal static void Transfer(WorldMutation world, UnitState unit, int recipient, int? servicePlace,
        IReadOnlyCollection<int> followers, int geographicPlace, bool recruited,
        SimulationRules rules, List<DomainEvent> events)
    {
        string reason = recruited ? "personnel-recruited" : "unit-defected";
        int owner = unit.OwnerId;
        var people = world.Officers.Values.Where(p => p.IsServing && p.OwnerId == owner && p.UnitId == unit.Id).OrderBy(p => p.Id).ToArray();
        int? root = world.Corps.Values.SingleOrDefault(c => c.OwnerId == recipient && c.IsRoot)?.Id;
        BattleTreasury.TransferTo(world, owner, unit.CorpsId, (long)unit.Soldiers + unit.Wounded,
            ResourceAccounts.Resolve(world, recipient, root), new(unit.Id, false), reason, events);
        // End the old construction under its old ownership before transferring the surviving army.
        FacilityConstruction.Cancel(world, unit.Id, events);
        unit = world.Units[unit.Id];
        foreach (var person in people.Where(p => !followers.Contains(p.Id)))
            PersonnelReturns.FromField(world, person.Id, unit.Position, rules, events, recruited);
        world.Replace(unit with { OwnerId = recipient, CorpsId = root, ServicePlaceId = servicePlace ?? unit.ServicePlaceId,
            EngagementTargetId = null, ContactTarget = null, Lookahead = null });
        var ruler = world.Officers[world.Forces[recipient].LeaderId!.Value];
        foreach (int id in followers.Order())
        {
            int loyalty = 75 + (50 - PersonnelRulebook.AffinityDistance(world.Officers[id], ruler)) / 2;
            if (recruited) RecruitmentRules.SetLoyalty(world, id, owner, 45, "recruited-away", events);
            PersonnelTransitions.TransferAllegiance(world, id, recipient, loyalty, geographicPlace,
                reason, rules.Personnel, events, recruited);
        }
        foreach (var captive in world.Officers.Values.Where(p => p.Status == OfficerStatus.Captive && p.UnitId == unit.Id)
            .OrderBy(p => p.Id).ToArray())
        {
            long sequence = world.Simulation.NextSequence;
            if (PrisonerRelease.Depart(world, captive.Id, geographicPlace, sequence,
                PrisonerDepartureKind.Displaced, null, rules.Personnel, rules.Economy, events, unit.Position) == CommandRejection.None)
                world.SetSimulation(world.Simulation with { NextSequence = checked(sequence + 1) });
        }
        MilitaryPolicies.AfterTransfer(world, unit.Id, rules.Military, events);
        if (rules.Military is { } military) MilitaryOrders.UnitChanged(world, unit.Id, military, events);
        events.Add(new UnitTransferredEvent(world.Clock, unit.Id, owner, recipient));
    }
}
