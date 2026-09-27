using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public enum NavigationStatus { Moving, Arrived, Waiting, Unreachable, Planning }

/// <summary>A stage destination, independent of the player waypoint. No route is retained.</summary>
public sealed record NavigationLookahead([property: JsonRequired] WorldPosition Position, [property: JsonRequired] MapOccupancy Target);

/// <summary>A daily result, not a persistent navigation plan.</summary>
public sealed record NavigationStep(WorldPosition Position, int Spent, NavigationStatus Status)
{
    public int Remainder { get; init; }
    public ImmutableArray<WorldPosition> Steps { get; init; } = [];
    public NavigationLookahead? Lookahead { get; init; }
    public bool Arrived => Status == NavigationStatus.Arrived;
}
