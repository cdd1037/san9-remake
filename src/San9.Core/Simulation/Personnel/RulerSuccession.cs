using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class RulerSuccession
{
    internal static ImmutableArray<int> Candidates(WorldMutation world, int rulerId) => world.Officers.Values
        .Where(p => p.Id != rulerId && p.IsServing && p.OwnerId == world.Officers[rulerId].OwnerId)
        .OrderBy(p => p.Id).Select(p => p.Id).ToImmutableArray();

    internal static OfficerState? Automatic(WorldMutation world, int forceId, OfficerState previous) => world.Officers.Values
        .Where(p => p.OwnerId == forceId && p.IsServing && p.Id != previous.Id)
        .OrderByDescending(p => (p.ParentId == previous.Id || previous.ParentId == p.Id) && p.BirthYear > previous.BirthYear)
        .ThenByDescending(p => p.ParentId is not null && p.ParentId == previous.ParentId)
        .ThenByDescending(p => p.BloodlineId is not null && p.BloodlineId == previous.BloodlineId ? (long)world.Clock.Year + 1 - p.BirthYear : 0)
        .ThenByDescending(p => p.SuccessionPriority)
        .ThenByDescending(p => Sum(world.Abilities(p)))
        .ThenBy(p => p.Id).FirstOrDefault();

    private static long Sum(OfficerAbilities abilities) => (long)abilities.War + abilities.Intelligence + abilities.Politics + abilities.Leadership;

    internal static void Commit(WorldMutation world, int forceId, int previousId, int successorId, List<DomainEvent> events)
    {
        Appoint(world, forceId, previousId, successorId, events);
        var successor = world.Officers[successorId];
        foreach (var person in world.Officers.Values.Where(p => p.OwnerId == forceId && p.Id != successorId).OrderBy(p => p.Id).ToArray())
            RecruitmentRules.SetLoyalty(world, person.Id, forceId,
                Math.Max(0, person.LoyaltyTo(forceId) - PersonnelRulebook.AffinityDistance(person, successor) / 2), "ruler-succession", events);
    }

    // Leadership and administrative succession are shared; historical events can own a different loyalty policy.
    internal static void Appoint(WorldMutation world, int forceId, int? previousId, int successorId, List<DomainEvent> events)
    {
        var successor = world.Officers[successorId];
        if (!successor.IsServing || successor.OwnerId != forceId || successorId == previousId)
            throw new InvalidOperationException("Invalid ruler successor.");
        int? corpsId = PersonnelLocations.Corps(world, successor);
        var force = world.Forces[forceId];
        world.Replace(force with { LeaderId = successorId, AdviserId = force.AdviserId == successorId ? null : force.AdviserId });
        world.Replace(successor with { Status = OfficerStatus.Ruler, OfficeId = null, StatusStartYear = world.Clock.Year });
        if (corpsId is int id)
        {
            var corps = world.Corps[id];
            if (corps.LeaderId is int corpsLeader && corpsLeader != successorId && corps.LeaderId != previousId && world.Officers[corpsLeader].Status == OfficerStatus.Governor)
                world.Replace(world.Officers[corpsLeader] with { Status = OfficerStatus.Serving });
            var root = world.Corps.Values.SingleOrDefault(c => c.OwnerId == forceId && c.IsRoot);
            if (root is not null && root.Id != id)
                GovernanceMaintenance.DissolveForced(world, id, "ruler-succession", events);
            int destination = root?.Id ?? id;
            world.Replace(world.Corps[destination] with { LeaderId = successorId, IsRoot = true,
                ControlMode = CorpsControlMode.Direct, Delegation = new GeneralDelegation() });
        }
        events.Add(new ForceLeaderChangedEvent(world.Clock, forceId, previousId, successorId));
    }
}
