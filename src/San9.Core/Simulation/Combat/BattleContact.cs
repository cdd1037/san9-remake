using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>A participant entered close combat; captured before casualties and removals.</summary>
public sealed record BattleContactStartedEvent(GameClock Clock, BattleObjectContext Participant,
    BattleObjectContext Opponent, bool Initiated, uint Soldiers, uint OpponentSoldiers) : DomainEvent;

internal static class BattleContact
{
    internal static void PublishEntries(WorldMutation world, MilitaryRulebook military,
        EngagementRelations previous, List<DomainEvent> events)
    {
        var current = world.Engagements(military);
        foreach (var participant in current.Participants.OrderBy(p => p.IsSettlement).ThenBy(p => p.Id))
        {
            if (previous.Engaged(participant)) continue;
            var opponent = current.Opponent(participant);
            events.Add(new BattleContactStartedEvent(world.Clock, BattleObjectContext.Capture(world, participant),
                BattleObjectContext.Capture(world, opponent), current.Target(participant) == opponent,
                participant.Soldiers(world), opponent.Soldiers(world)));
        }
    }
}
