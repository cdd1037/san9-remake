using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record BattleConditionChangedEvent(GameClock Clock, int ObjectId, bool IsSettlement, UnitCondition Condition,
    int DaysRemaining) : DomainEvent
{
    // Standalone effects retain their own context instead of inheriting the preceding tactic.
    public int? SourceForceId { get; init; }
    public BattleObjectContext? Target { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public UnitCondition? PreviousCondition { get; init; }
}

internal static class MilitaryConditions
{
    internal static UnitState Apply(UnitState unit, UnitCondition condition, int days) => unit.Condition == condition ? unit :
        unit with { Condition = condition, ConditionDaysRemaining = Math.Max(unit.ConditionDaysRemaining, days),
            HighSpiritsMoralePenaltyPending = condition == UnitCondition.HighSpirits };

    internal static UnitState Tick(UnitState unit)
    {
        if (unit.ConditionDaysRemaining == 0 && unit.Condition is not (UnitCondition.Disguised or UnitCondition.NoFormation or
            UnitCondition.Confused or UnitCondition.Routed or UnitCondition.HighSpirits)) return unit;
        int days = Math.Max(0, unit.ConditionDaysRemaining - 1);
        var condition = days == 0 ? UnitCondition.Normal : unit.Condition;
        int morale = unit.Condition == UnitCondition.HighSpirits && unit.HighSpiritsMoralePenaltyPending &&
            unit.ConditionDaysRemaining == 1 && unit.EffectiveOrderClass == UnitOrderClass.Military
            ? Math.Max(1, unit.Morale - 30) : unit.Morale;
        return unit with { Condition = condition, ConditionDaysRemaining = days, Morale = morale,
            HighSpiritsMoralePenaltyPending = days > 0 && condition == UnitCondition.HighSpirits && unit.HighSpiritsMoralePenaltyPending };
    }

    internal static void Tick(WorldMutation world, List<DomainEvent> events)
    {
        foreach (var unit in world.Units.Values.Where(u => u.ConditionDaysRemaining > 0 ||
            u.Condition is UnitCondition.Disguised or UnitCondition.NoFormation or UnitCondition.Confused or UnitCondition.Routed or UnitCondition.HighSpirits).OrderBy(u => u.Id).ToArray())
        {
            var next = Tick(unit);
            world.Replace(next);
            if (next.Morale != unit.Morale) events.Add(new HighSpiritsExpiredEvent(world.Clock, unit.Id, unit.Morale, next.Morale)
                { Unit = BattleObjectContext.Capture(unit) });
            if (next.ConditionDaysRemaining == 0 && unit.Condition != next.Condition)
                events.Add(new BattleConditionChangedEvent(world.Clock, unit.Id, false, next.Condition, 0)
                { Target = BattleObjectContext.Capture(world, new(unit.Id, false)), PreviousCondition = unit.Condition });
        }
        foreach (var place in world.Settlements.Values.Where(s => s.ConfusionDaysRemaining > 0).OrderBy(s => s.Id).ToArray())
        {
            int days = place.ConfusionDaysRemaining - 1;
            world.Replace(place with { ConfusionDaysRemaining = days });
            if (days == 0) events.Add(new BattleConditionChangedEvent(world.Clock, place.Id, true, UnitCondition.Normal, 0)
            { Target = BattleObjectContext.Capture(world, new(place.Id, true)) });
        }
    }

    internal static void Confuse(WorldMutation world, MilitaryOrder target, int days, List<DomainEvent> events)
    {
        if (days <= 0) return;
        if (target.UnitId is int id)
        {
            var unit = world.Units[id];
            var next = Apply(unit, UnitCondition.Confused, days);
            if (next == unit) return;
            days = next.ConditionDaysRemaining;
            world.Replace(next);
        }
        else
        {
            var place = world.Settlements[target.SettlementId!.Value];
            days = Math.Max(days, place.ConfusionDaysRemaining);
            world.Replace(place with { ConfusionDaysRemaining = days });
        }
        events.Add(new BattleConditionChangedEvent(world.Clock, target.UnitId ?? target.SettlementId!.Value,
            target.UnitId is null, UnitCondition.Confused, days));
    }
}

public sealed record HighSpiritsExpiredEvent(GameClock Clock, int UnitId, int PreviousMorale, int CurrentMorale) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired] public BattleObjectContext Unit { get; init; } = null!;
}
