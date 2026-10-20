namespace San9.Core.Simulation;

internal static class PersonnelLoyaltyCycle
{
    internal static void Apply(WorldMutation world, List<DomainEvent> events)
    {
        if (world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30) return;
        var forces = world.Forces.Values.Where(f => !f.IsEliminated).OrderBy(f => f.Id).ToArray();
        foreach (var person in world.Officers.Values.OrderBy(p => p.Id).ToArray())
            foreach (var force in forces)
            {
                int forceId = force.Id;
                if (person.OwnerId == forceId) continue;
                int loyalty = person.LoyaltyTo(forceId);
                if (loyalty != 50)
                    RecruitmentRules.SetLoyalty(world, person.Id, forceId, loyalty + Math.Sign(50 - loyalty),
                        "quarterly-affiliation-interest", events);
            }
    }
}
