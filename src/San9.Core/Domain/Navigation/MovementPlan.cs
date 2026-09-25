using System.Collections.Immutable;

namespace San9.Core.Domain;

public enum NavigationStatus { Moving, Arrived, Waiting, Unreachable, Planning }

/// <summary>The chosen short route is deterministic simulation state, not an evictable cache.</summary>
public sealed record MovementPlan(WorldPosition Target, Footprint Shape, TravelMode Mode,
    ImmutableArray<WorldPosition> Steps, int BlockedAttempts = 0)
{
    public ImmutableArray<MapOccupancy> Blockers { get; init; } = [];
    public int RetryDelay { get; init; }
    public NavigationStatus FailureStatus { get; init; } = NavigationStatus.Waiting;
    public bool IsValidFrom(WorldPosition start)
    {
        if (!Shape.IsValid || !Enum.IsDefined(Mode) || Target.X < 0 || Target.Y < 0 || Steps.IsDefault ||
            BlockedAttempts is < 0 or > 16 || RetryDelay is < 0 or > 3 ||
            FailureStatus is not (NavigationStatus.Waiting or NavigationStatus.Unreachable or NavigationStatus.Planning) || Blockers.IsDefault ||
            Blockers.Any(b => !b.Footprint.IsValid)) return false;
        foreach (var step in Steps)
        {
            if (step.X < 0 || step.Y < 0 || Math.Abs((long)step.X - start.X) + Math.Abs((long)step.Y - start.Y) != 1) return false;
            start = step;
        }
        return true;
    }
}

public sealed record NavigationStep(WorldPosition Position, int Spent, NavigationStatus Status, MovementPlan? Plan)
{
    public int Remainder { get; init; }
    public bool Arrived => Status == NavigationStatus.Arrived;
}

internal sealed record PathSegment(ImmutableArray<WorldPosition> Steps, NavigationStatus Status);
