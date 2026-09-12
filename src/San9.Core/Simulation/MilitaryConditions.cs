using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record BattleConditionChangedEvent(GameClock Clock, int ObjectId, bool IsSettlement, UnitCondition Condition,
    int DaysRemaining) : DomainEvent;

internal static class MilitaryConditions
{
    internal static void Tick(WorldMutation world, List<DomainEvent> events)
    {
        foreach (var unit in world.Units.Values.Where(u => u.ConditionDaysRemaining > 0).OrderBy(u => u.Id).ToArray())
        {
            int days = unit.ConditionDaysRemaining - 1;
            var condition = days == 0 ? UnitCondition.Normal : unit.Condition;
            world.Replace(unit with { Condition = condition, ConditionDaysRemaining = days });
            if (days == 0) events.Add(new BattleConditionChangedEvent(world.Clock, unit.Id, false, condition, 0));
        }
        foreach (var place in world.Settlements.Values.Where(s => s.ConfusionDaysRemaining > 0).OrderBy(s => s.Id).ToArray())
        {
            int days = place.ConfusionDaysRemaining - 1;
            world.Replace(place with { ConfusionDaysRemaining = days });
            if (days == 0) events.Add(new BattleConditionChangedEvent(world.Clock, place.Id, true, UnitCondition.Normal, 0));
        }
    }

    internal static void Confuse(WorldMutation world, MilitaryOrder target, int days, List<DomainEvent> events)
    {
        if (days <= 0) return;
        if (target.UnitId is int id)
        {
            var unit = world.Units[id];
            if (unit.Condition == UnitCondition.Confused) days = Math.Max(days, unit.ConditionDaysRemaining);
            world.Replace(unit with { Condition = UnitCondition.Confused, ConditionDaysRemaining = days });
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
