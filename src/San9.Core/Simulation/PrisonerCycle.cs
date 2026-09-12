using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class PrisonerCycle
{
    internal static int LoyaltyThreshold(int affinityDistance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(affinityDistance);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(affinityDistance, 50);
        return 15 * (int)Math.Sqrt(affinityDistance) + 10;
    }

    internal static void Apply(WorldMutation world, List<DomainEvent> events)
    {
        foreach (var person in world.Officers.Values.Where(p => p.Status == OfficerStatus.Captive).OrderBy(p => p.Id))
        {
            // Captivity retains allegiance; the detention location determines the captor independently.
            int distance = person.OwnerId is int original && world.Forces[original].LeaderId is int leader
                ? PersonnelRulebook.AffinityDistance(person, world.Officers[leader]) : 50;
            int roll = (int)(world.NextRandom("personnel-prisoner-loyalty") % 100);
            if (roll < LoyaltyThreshold(distance) && person.OwnerId is int forceId)
                RecruitmentRules.SetLoyalty(world, person.Id, forceId, Math.Max(0, person.LoyaltyTo(forceId) - 1),
                    "prisoner-xun", events);
        }
    }
}
