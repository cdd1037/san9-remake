using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Explicit public topology and frozen occupancy; no world, command, task or random handles.</summary>
public sealed record NavigationArrivalInput(UnitTravel Travel, WorldPosition Start, MapOccupancy Target,
    ImmutableArray<MapOccupancy> Occupancy, bool Adjacent = true, int PreparationDays = 0,
    int MovementRemainder = 0, bool RectangularReach = false, int Range = 1,
    WorldPosition? Waypoint = null, NavigationLookahead? Lookahead = null, bool AllowTargetOverlap = false);

public static class NavigationArrivalForecast
{
    public static ArrivalEstimate Evaluate(NavigationArrivalInput input, NavigationWorkBudget work, int maximumDays = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDays);
        ArgumentOutOfRangeException.ThrowIfNegative(input.PreparationDays);
        if (input.AllowTargetOverlap && input.Waypoint is null && new MapOccupancy(input.Start, input.Travel.Shape).Overlaps(input.Target))
            return new(ArrivalEstimateStatus.Known, input.PreparationDays);
        var position = input.Start;
        int remainder = input.MovementRemainder;
        NavigationLookahead? lookahead = input.Lookahead;
        WorldPosition? waypoint = input.Waypoint;
        var initial = Advance(input.Travel, position, input.Target, input.Adjacent, input.Occupancy, 0,
            work, lookahead, input.RectangularReach, input.Range);
        if (initial.Arrived && waypoint is null) return new(ArrivalEstimateStatus.Known, input.PreparationDays);
        for (int day = input.PreparationDays + 1; day <= maximumDays; day++)
        {
            int budget = checked(input.Travel.DailyBudget + remainder);
            NavigationStep step;
            if (waypoint is { } via)
            {
                step = Advance(input.Travel, position, new(via, new(1, 1)), false, input.Occupancy, budget, work, lookahead);
                if (step.Arrived)
                {
                    waypoint = null;
                    step = Advance(input.Travel, step.Position, input.Target, input.Adjacent, input.Occupancy,
                        Math.Max(0, budget - step.Spent), work, null, input.RectangularReach, input.Range);
                }
            }
            else step = Advance(input.Travel, position, input.Target, input.Adjacent, input.Occupancy,
                budget, work, lookahead, input.RectangularReach, input.Range);
            if (work.Exhausted || step.Status == NavigationStatus.Planning) return new(ArrivalEstimateStatus.Incomplete);
            if (step.Arrived && waypoint is null) return new(ArrivalEstimateStatus.Known, day);
            if (step.Status == NavigationStatus.Unreachable) return new(ArrivalEstimateStatus.Unreachable);
            if (step.Status == NavigationStatus.Waiting && step.Remainder == 0 && step.Spent == 0)
                return new(ArrivalEstimateStatus.Waiting);
            position = step.Position;
            remainder = step.Remainder;
            lookahead = step.Lookahead;
        }
        return new(ArrivalEstimateStatus.Incomplete);
    }

    // Both the full world forecast and authoritative daily movement use this same navigation operation.
    internal static NavigationStep Advance(UnitTravel travel, WorldPosition position, MapOccupancy target, bool adjacent,
        IReadOnlyList<MapOccupancy> occupancy, int budget, NavigationWorkBudget? work, NavigationLookahead? lookahead,
        bool rectangularReach = false, int maximumRange = 1, Func<WorldPosition, int>? range = null, int lookaheadCells = 25) =>
        travel.Map.Navigate(position, target, adjacent, travel.Shape, travel.Mode, occupancy, budget,
            range: range ?? (_ => maximumRange), rectangularReach: rectangularReach, maximumRange: maximumRange,
            work: work, lookahead: lookahead, lookaheadCells: lookaheadCells);
}
