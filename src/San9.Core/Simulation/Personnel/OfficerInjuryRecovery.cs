namespace San9.Core.Simulation;

public sealed record OfficerInjuryRecoveredEvent(GameClock Clock, int OfficerId, int Previous, int Current) : DomainEvent;

internal static class OfficerInjuryRecovery
{
    internal static void Apply(WorldMutation world, List<DomainEvent> events)
    {
        foreach (var officer in world.Officers.Values.OrderBy(p => p.Id).ToArray())
        {
            if (officer.Injury == 0) continue;
            int next = officer.Injury - 1;
            world.Replace(officer with { Injury = next });
            events.Add(new OfficerInjuryRecoveredEvent(world.Clock, officer.Id, officer.Injury, next));
        }
    }
}
