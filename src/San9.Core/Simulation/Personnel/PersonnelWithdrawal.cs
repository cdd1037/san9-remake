using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record OfficerWithdrawnEvent(GameClock Clock, int OfficerId, int? PreviousForceId) : DomainEvent;

internal static class PersonnelWithdrawal
{
    internal static void Resolve(WorldMutation world, int officerId, SimulationRules rules, List<DomainEvent> events)
    {
        var previous = world.Officers[officerId];
        PersonnelTransitions.CancelMissions(world, officerId, "person-withdrawn", events, rules.Personnel, true);
        foreach (var force in world.Forces.Values.Where(f => f.AdviserId == officerId).ToArray())
            world.Replace(force with { AdviserId = null });
        // Preserve authored identity and attributes while ending active participation.
        world.Replace(world.Officers[officerId] with { Status = OfficerStatus.Special,
            OwnerId = null, SettlementId = null, RegionId = null, UnitId = null, OfficeId = null,
            IsCommander = false, Acted = true, EquippedTacticId = null, TacticCooldown = 0 });
        foreach (var treasure in world.Treasures.Values.Where(t => t.HolderId == officerId).OrderBy(t => t.Id).ToArray())
        {
            world.Replace(treasure with { HolderId = null, Discovery = TreasureDiscovery.Discovered });
            events.Add(new TreasureDroppedEvent(world.Clock, treasure.Id, officerId));
        }
        if (previous.UnitId is int unit && world.Units.ContainsKey(unit) && !world.Officers.Values.Any(p => p.UnitId == unit && p.IsServing))
            BattleAftermath.TerminateUnit(world, unit, null, rules, events);
        if (previous.OwnerId is int owner)
        {
            if (world.Forces[owner].LeaderId == officerId && RulerSuccession.Automatic(world, owner, previous) is { } successor)
                RulerSuccession.Commit(world, owner, officerId, successor.Id, events);
            BattleAftermath.Reconcile(world, new HashSet<int> { owner }, rules, events, "person-withdrawn");
        }
        world.MaintainLeadership(events);
        world.Replace(world.Officers[officerId] with { IsDormant = true });
        events.Add(new OfficerWithdrawnEvent(world.Clock, officerId, previous.OwnerId));
    }
}
