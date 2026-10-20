using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Settles force survival, succession, corps and service attachments after a domain transition.</summary>
internal static class GovernanceLifecycle
{
    // Force checks are scoped; corps and service repair remain global and ordered.
    internal static void Reconcile(WorldMutation world, IReadOnlySet<int> forcesToCheck, SimulationRules rules, List<DomainEvent> events, bool afterDailyCommands, string reason = "battle")
    {
        foreach (var force in world.Forces.Values.Where(f => !f.IsEliminated && forcesToCheck.Contains(f.Id)).OrderBy(f => f.Id).ToArray())
        {
            var serving = world.Officers.Values.Where(p => p.OwnerId == force.Id && p.IsServing).OrderBy(p => p.Id).ToArray();
            if (EliminateIfDefeated(world, force.Id, rules, events, afterDailyCommands)) continue;
            if (force.LeaderId is not int leader || !world.Officers.TryGetValue(leader, out var ruler) ||
                ruler.OwnerId != force.Id || !ruler.IsServing && ruler.Status != OfficerStatus.Captive)
            {
                var successor = rules.Governance.SelectSuccessor(serving, force.LeaderId ?? serving[0].Id, world.AbilitiesFor(serving)) ?? serving[0];
                RulerSuccession.Appoint(world, force.Id, force.LeaderId, successor.Id, events);
            }
        }
        foreach (var original in world.Corps.Values.OrderByDescending(c => c.IsRoot).ThenBy(c => c.Id).ToArray())
        {
            if (!world.Corps.TryGetValue(original.Id, out var corps)) continue;
            var people = GovernanceMaintenance.Members(world, corps);
            if (corps.IsRoot)
            {
                if (world.Forces[corps.OwnerId].LeaderId is int ruler) world.Replace(corps with { LeaderId = ruler });
                if (!GovernanceMaintenance.CanSurvive(world, corps, people))
                {
                    var child = world.Corps.Values.Where(c => c.OwnerId == corps.OwnerId && !c.IsRoot).OrderBy(c => c.Id).FirstOrDefault();
                    if (child is not null) GovernanceMaintenance.DissolveForced(world, child.Id, reason, events);
                }
                continue;
            }
            if (GovernanceMaintenance.CanSurvive(world, corps, people))
            {
                if (GovernanceMaintenance.RepairCorps(world, corps.Id, rules.Governance, events) != CommandRejection.None)
                    throw new InvalidOperationException("Cannot repair surviving corps.");
                continue;
            }
            GovernanceMaintenance.DissolveForced(world, corps.Id, reason, events);
        }
        UnitServicePlaces.Repair(world, rules);
    }

    internal static bool EliminateIfDefeated(WorldMutation world, int forceId, SimulationRules rules,
        List<DomainEvent> events, bool afterDailyCommands, int? victorForceId = null)
    {
        if (world.Forces[forceId].IsEliminated) return true;
        if (ForceLifecycle.HasCity(world, forceId) && world.Officers.Values.Any(p => p.OwnerId == forceId && p.IsServing)) return false;
        ForceElimination.Apply(world, forceId, rules, events, afterDailyCommands, victorForceId);
        return true;
    }
}
