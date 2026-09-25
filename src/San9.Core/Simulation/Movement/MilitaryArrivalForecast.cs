using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum ArrivalEstimateStatus { Known, NotApplicable, Waiting, Unreachable, Incomplete }
public sealed record ArrivalEstimate(ArrivalEstimateStatus Status, int? Days = null);

/// <summary>Predicts navigation against a frozen world, without publishing forecast paths or consuming randomness.</summary>
public sealed class MilitaryArrivalForecast(WorldSnapshot snapshot, MilitaryRulebook rules,
    IReadOnlyDictionary<int, int>? regionCities = null, TreasureRulebook? treasures = null)
{
    private WorldMutation? world;

    public ArrivalEstimate Estimate(int unitId, int maximumDays = 4096, int maximumExpanded = 100000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDays);
        var work = new NavigationWorkBudget(maximumExpanded);
        var unit = snapshot.Units.FirstOrDefault(u => u.Id == unitId);
        if (unit is not { FormationId: int formation, EffectiveOrder: not null } || !rules.Formations.ContainsKey(formation)) return new(ArrivalEstimateStatus.NotApplicable);
        world ??= WorldState.Restore(snapshot).BeginMutation();
        if (!Valid(unit)) return new(ArrivalEstimateStatus.NotApplicable);
        if (MilitaryNavigation.Advance(world, unit, rules, 0).Arrived) return new(ArrivalEstimateStatus.Known, 0);
        for (int day = 1; day <= maximumDays; day++)
        {
            // BeforeDailyCommands expires conditions before that day's map movement.
            if (day > 1 || snapshot.Simulation.PendingDailyTask is not { Phase: TaskResolutionPhase.DailyCommands })
            {
                int remaining = Math.Max(0, unit.ConditionDaysRemaining - 1);
                var previousOrder = unit.EffectiveOrder;
                unit = unit with { ConditionDaysRemaining = remaining,
                    Condition = remaining == 0 ? UnitCondition.Normal : unit.Condition };
                // Do not label arrival at a different future order as arrival at the displayed target.
                if (unit.EffectiveOrder != previousOrder) return new(ArrivalEstimateStatus.Incomplete);
            }
            if (!Valid(unit)) return new(ArrivalEstimateStatus.NotApplicable);
            if (unit.Condition == UnitCondition.Confused ||
                unit.Condition == UnitCondition.Routed && unit.EffectiveOrder!.Kind == MilitaryOrderKind.Construct)
            {
                unit = unit with { MovementRemainder = 0 };
                continue;
            }
            var step = MilitaryNavigation.Advance(world, unit, rules, MilitaryNavigation.Budget(unit, rules), work);
            if (work.Exhausted || step.Status == NavigationStatus.Planning) return new(ArrivalEstimateStatus.Incomplete);
            if (step.Arrived) return new(ArrivalEstimateStatus.Known, day);
            if (step.Status == NavigationStatus.Unreachable) return new(ArrivalEstimateStatus.Unreachable);
            // Search limits and transient obstructions are not proof of no route.
            if (step.Status == NavigationStatus.Waiting && step.Remainder == 0)
                return new(ArrivalEstimateStatus.Waiting);
            unit = MilitaryNavigation.Apply(unit, step);
        }
        return new(ArrivalEstimateStatus.Incomplete);
    }

    private bool Valid(UnitState unit) => unit.EffectiveOrder is { } order &&
        (order.Construction is not null
            ? regionCities is not null && treasures is not null && FacilityConstruction.CanContinue(world!, unit, rules, regionCities, treasures)
            : MilitaryMovement.ValidOrder(world!, unit.OwnerId, order, rules.Formations[unit.FormationId!.Value], rules, unit.NavalFormationId));
}
