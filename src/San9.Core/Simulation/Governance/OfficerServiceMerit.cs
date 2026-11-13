using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record OfficerServiceMeritChangedEvent(GameClock Clock, int OfficerId, int ForceId, int Previous, int Current, string Reason) : DomainEvent;

internal static class OfficerServiceMerit
{
    internal static void Credit(WorldMutation world, int officerId, int forceId, int amount, string reason, List<DomainEvent> events)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Invalid service merit credit.");
        if (world.Simulation.NewGame.PersonalPeerageEnabled == false || !world.Officers.TryGetValue(officerId, out var officer) ||
            !officer.IsServing || officer.OwnerId != forceId) return;
        int previous = officer.ServiceMeritFor(forceId);
        int current = (int)Math.Min(1000000, (long)previous + amount);
        if (previous == current) return;
        var values = officer.ServiceMerit ?? ImmutableSortedDictionary<int, int>.Empty;
        world.Replace(officer with { ServiceMerit = values.SetItem(forceId, current) });
        events.Add(new OfficerServiceMeritChangedEvent(world.Clock, officerId, forceId, previous, current, reason));
    }
}
