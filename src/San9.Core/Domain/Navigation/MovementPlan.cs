using System.Collections.Immutable;

namespace San9.Core.Domain;

public enum NavigationStatus { Moving, Arrived, Waiting, Unreachable, Planning }

/// <summary>Inputs to position-dependent attack approach rules; terrain and elevation belong to immutable military rules.</summary>
public sealed record AttackApproachState(OrdinaryAttack? LandAttack, Footprint LandShape,
    OrdinaryAttack? NavalAttack, Footprint? NavalShape, OrdinaryAttack? ReplyAttack,
    bool TargetIsSettlement, bool TargetIsHarbor, bool CloseSiege, bool Rockfall)
{
    public ImmutableArray<MapOccupancy> RockfallBlockers { get; init; } = [];
    public bool Matches(AttackApproachState other) => LandAttack == other.LandAttack && LandShape == other.LandShape &&
        NavalAttack == other.NavalAttack && NavalShape == other.NavalShape && ReplyAttack == other.ReplyAttack &&
        TargetIsSettlement == other.TargetIsSettlement && TargetIsHarbor == other.TargetIsHarbor &&
        CloseSiege == other.CloseSiege && Rockfall == other.Rockfall && RockfallBlockers.SequenceEqual(other.RockfallBlockers);
}

/// <summary>A goal proven blocked by the recorded occupancy, rather than an unfinished local search.</summary>
public sealed record BlockedNavigation(WorldPosition Origin, MapOccupancy Target, bool IntoRange, int Range, bool Rectangular)
{
    public AttackApproachState? Approach { get; init; }
    public bool Matches(BlockedNavigation other) => Origin == other.Origin && Target == other.Target && IntoRange == other.IntoRange &&
        Range == other.Range && Rectangular == other.Rectangular &&
        (Approach is null ? other.Approach is null : other.Approach is not null && Approach.Matches(other.Approach));
}

/// <summary>The chosen short route is deterministic simulation state, not an evictable cache.</summary>
public sealed record MovementPlan(WorldPosition Target, Footprint Shape, TravelMode Mode,
    ImmutableArray<WorldPosition> Steps, int BlockedAttempts = 0)
{
    public ImmutableArray<MapOccupancy> Blockers { get; init; } = [];
    public int RetryDelay { get; init; }
    public NavigationStatus FailureStatus { get; init; } = NavigationStatus.Waiting;
    public BlockedNavigation? BlockedSearch { get; init; }
    public bool IsValidFrom(WorldPosition start)
    {
        if (!Shape.IsValid || !Enum.IsDefined(Mode) || Target.X < 0 || Target.Y < 0 || Steps.IsDefault ||
            BlockedAttempts is < 0 or > 16 || RetryDelay is < 0 or > 3 ||
            FailureStatus is not (NavigationStatus.Waiting or NavigationStatus.Unreachable or NavigationStatus.Planning) || Blockers.IsDefault ||
            Blockers.Any(b => !b.Footprint.IsValid) ||
            BlockedSearch is { } blocked && (!Steps.IsEmpty || FailureStatus != NavigationStatus.Waiting ||
                blocked.Origin != start || blocked.Target.Position != Target || !blocked.Target.Footprint.IsValid || blocked.Range <= 0 ||
                blocked.Approach is { } approach && (!approach.LandShape.IsValid || approach.NavalShape is { IsValid: false } ||
                    approach.RockfallBlockers.IsDefault || approach.RockfallBlockers.Any(b => !b.Footprint.IsValid)))) return false;
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

internal sealed record PathSegment(ImmutableArray<WorldPosition> Steps, NavigationStatus Status, bool FullyBlocked = false);
