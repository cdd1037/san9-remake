namespace San9.Core.Simulation;

internal static class PersonnelLoyaltyCycle
{
    internal static void Apply(WorldMutation world, List<DomainEvent> events)
    {
        if (world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30) return;
        foreach (var person in world.Officers.Values.OrderBy(p => p.Id).ToArray())
            foreach (int forceId in person.ForceLoyalties.Keys.Order())
            {
                if (person.OwnerId == forceId) continue;
                int loyalty = person.LoyaltyTo(forceId);
                if (loyalty != 50)
                    RecruitmentRules.SetLoyalty(world, person.Id, forceId, loyalty + Math.Sign(50 - loyalty),
                        "quarterly-affiliation-interest", events);
            }
    }
}
