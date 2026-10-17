using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public enum NavigationStatus { Moving, Arrived, Waiting, Unreachable, Planning }

/// <summary>A stage destination, independent of the player waypoint. No route is retained.</summary>
public sealed record NavigationLookahead([property: JsonRequired] WorldPosition Position);

/// <summary>Stable domain identity; geometry alone cannot identify a collision participant.</summary>
public sealed record MapObjectReference(int Id, bool IsSettlement);

/// <summary>A daily result, not a persistent navigation plan.</summary>
public sealed record NavigationStep(WorldPosition Position, int Spent, NavigationStatus Status)
{
    public int Remainder { get; init; }
    public ImmutableArray<WorldPosition> Steps { get; init; } = [];
    public NavigationLookahead? Lookahead { get; init; }
    public MapOccupancy? Collision { get; init; }
    public bool Arrived => Status == NavigationStatus.Arrived;
}
