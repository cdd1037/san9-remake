using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>One canonical unordered pair owns both directions of a diplomatic relationship.</summary>
internal static class ForceRelations
{
    internal static ForceRelation Get(WorldMutation world, int first, int second)
    {
        if (first == second || !world.Forces.ContainsKey(first) || !world.Forces.ContainsKey(second))
            throw new ArgumentException("A relation requires two different known forces.");
        return world.Relations.TryGetValue((Math.Min(first, second), Math.Max(first, second)), out var relation)
            ? relation : throw new InvalidOperationException("Missing force relation.");
    }

    internal static void Set(WorldMutation world, int first, int second, int value, List<DomainEvent> events)
    {
        _ = DiplomacyMath.RelationBand(value);
        var previous = Get(world, first, second);
        if (previous.Value == value) return;
        world.Replace(previous with { Value = value });
        events.Add(new ForceRelationChangedEvent(world.Clock, previous.FirstId, previous.SecondId, previous.Value, value));
    }

    internal static void Change(WorldMutation world, int first, int second, int delta, List<DomainEvent> events) =>
        Set(world, first, second, (int)Math.Clamp((long)Get(world, first, second).Value + delta, 0, 100), events);
}
