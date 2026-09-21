using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>One canonical unordered pair owns both directions of a diplomatic relationship.</summary>
internal static class ForceRelations
{
    internal static void AdvanceQuarter(WorldMutation world, List<DomainEvent> events)
    {
        if (world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30) return;
        var forces = world.Forces.Values.Where(f => !f.IsEliminated).OrderBy(f => f.Id).ToArray();
        for (int first = 0; first < forces.Length; first++)
            for (int second = first + 1; second < forces.Length; second++)
            {
                var a = forces[first];
                var b = forces[second];
                bool barbarian = a.IsBarbarian || b.IsBarbarian;
                int value = Get(world, a.Id, b.Id).Value;
                int step = barbarian ? 4 : 1;
                if (value > 50) Change(world, a.Id, b.Id, -step, events);
                else if (value < (barbarian ? 36 : 39)) Change(world, a.Id, b.Id, step, events);
            }
    }

    internal static ForceRelation Get(WorldMutation world, int first, int second)
    {
        if (first == second || !world.Forces.ContainsKey(first) || !world.Forces.ContainsKey(second))
            throw new ArgumentException("A relation requires two different known forces.");
        return world.Relations.TryGetValue((Math.Min(first, second), Math.Max(first, second)), out var relation)
            ? relation : new(Math.Min(first, second), Math.Max(first, second), 0);
    }

    internal static void Set(WorldMutation world, int first, int second, int value, List<DomainEvent> events)
    {
        _ = DiplomacyMath.RelationBand(value);
        var previous = Get(world, first, second);
        if (previous.Value == value) return;
        world.SetRelation(previous with { Value = value });
        events.Add(new ForceRelationChangedEvent(world.Clock, previous.FirstId, previous.SecondId, previous.Value, value));
    }

    internal static void Change(WorldMutation world, int first, int second, int delta, List<DomainEvent> events) =>
        Set(world, first, second, (int)Math.Clamp((long)Get(world, first, second).Value + delta, 0, 100), events);
}
