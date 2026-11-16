using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>One canonical unordered pair owns both directions of a diplomatic relationship.</summary>
internal static class ForceRelations
{
    internal static void AdvanceQuarter(WorldMutation world, MilitaryRulebook? military, List<DomainEvent> events)
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
                if (value > 50) Change(world, a.Id, b.Id, -step, military, events);
                else if (value < (barbarian ? 36 : 39)) Change(world, a.Id, b.Id, step, military, events);
            }
    }

    internal static ForceRelation Get(WorldMutation world, int first, int second)
    {
        if (first == second || !world.Forces.ContainsKey(first) || !world.Forces.ContainsKey(second))
            throw new ArgumentException("A relation requires two different known forces.");
        return world.Relations.TryGetValue((Math.Min(first, second), Math.Max(first, second)), out var relation)
            ? relation : new(Math.Min(first, second), Math.Max(first, second), 0);
    }

    internal static void Set(WorldMutation world, int first, int second, int value, MilitaryRulebook? military, List<DomainEvent> events, bool notify = false)
    {
        _ = DiplomacyMath.RelationBand(value);
        Update(world, first, second, value, military, events, notify);
    }

    internal static void Change(WorldMutation world, int first, int second, int delta, MilitaryRulebook? military, List<DomainEvent> events, bool notify = false) =>
        Update(world, first, second, (long)Get(world, first, second).Value + delta, military, events, notify);

    private static void Update(WorldMutation world, int first, int second, long requestedValue, MilitaryRulebook? military, List<DomainEvent> events, bool notify)
    {
        var previous = Get(world, first, second);
        int value = (int)Math.Clamp(requestedValue, 0, 100);
        if (previous.Value != value)
        {
            world.SetRelation(previous with { Value = value });
            events.Add(new ForceRelationChangedEvent(world.Clock, previous.FirstId, previous.SecondId, previous.Value, value, notify));
        }
        if (requestedValue > previous.Value && !MilitaryDiplomacy.AutomaticEnemy(world, first, second))
            DiplomaticConflicts.End(world, first, second, military, events);
    }
}
