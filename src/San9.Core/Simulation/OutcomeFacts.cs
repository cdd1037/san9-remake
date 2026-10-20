using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record OfficerInjuryWorsenedEvent(GameClock Clock, int OfficerId, int Previous, int Current,
    PersonOccurrence Occurrence) : DomainEvent;
public sealed record UnitCommanderChangedEvent(GameClock Clock, BattleObjectContext Unit, int PreviousId, int OfficerId) : DomainEvent;
public sealed record TreasuryCapturedEvent(GameClock Clock, int PreviousForceId, int? PreviousCorpsId,
    int ForceId, int? CorpsId, int ObjectId, bool IsSettlement, WorldPosition? Position, int? PlaceId,
    long Money, long Food) : DomainEvent;
public sealed record BattlePersonnelResolvedEvent(GameClock Clock, BattleObjectContext Defeated, BattleObjectContext Captor,
    ImmutableArray<int> CapturedIds, ImmutableArray<int> RetreatedIds, int? LastCapturedId) : DomainEvent;

internal static class OfficerInjuries
{
    internal static void Apply(WorldMutation world, OfficerState next, List<DomainEvent> events)
    {
        var previous = world.Officers[next.Id];
        var occurrence = PersonOccurrence.Capture(world, previous);
        world.Replace(next);
        if (next.Injury > previous.Injury)
            events.Add(new OfficerInjuryWorsenedEvent(world.Clock, next.Id, previous.Injury, next.Injury, occurrence));
    }
}
