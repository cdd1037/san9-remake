using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Known account values only; no task outcomes, world handles or random state.</summary>
public sealed record FoodSupplyInput(GameClock Clock, GameClock Started, long Balance, long DailyConsumption,
    long Harvest, long? ReplacementSupply);

public static class FoodSupplyForecast
{
    public static ResourceOutlook Evaluate(FoodSupplyInput input)
    {
        int dayOfYear = (input.Clock.Month - 1) * 30 + input.Clock.Day - 1;
        int days = (180 - dayOfYear + 359) % 360 + 1;
        if (input.Clock.DayNumber - input.Started.DayNumber + days < 30) days += 360;
        long expenditure = checked(input.DailyConsumption * days);
        long remaining = Math.Max(0, input.Balance - expenditure);
        return input.ReplacementSupply is long replacement
            ? new(input.Balance, expenditure, replacement - remaining, replacement)
            : new(input.Balance, expenditure, input.Harvest, Math.Min(1000000, remaining + input.Harvest));
    }

    /// <summary>Projection of the observer's own account; all carried values have public economic meaning.</summary>
    public static FoodSupplyInput Capture(WorldSnapshot world, int forceId, int accountId)
    {
        var force = world.Forces.Single(f => f.Id == forceId && !f.IsDormant && !f.IsEliminated);
        var account = world.Corps.Single(c => c.Id == accountId && c.OwnerId == forceId);
        int root = world.Corps.Single(c => c.OwnerId == forceId && c.IsRoot).Id;
        int Account(int? id) => id ?? root;
        var query = new WorldQuery(world);
        var places = query.Settlements.Values.Where(s => !s.IsDestroyed && s.OwnerId == forceId && Account(s.CorpsId) == accountId).ToArray();
        long rations = places.Where(s => s.ConsumesFood).Sum(s => DailyRations.Garrison(s.Soldiers, s.Wounded)) +
            query.Units.Values.Where(u => u.OwnerId == forceId && Account(u.CorpsId) == accountId)
                .Sum(u => DailyRations.Field(u.Soldiers, u.Wounded)) +
            world.Simulation.Tasks.Where(t => t.ForceId == forceId && t.Reinforcements is not null &&
                Account(ReinforcementTransport.Corps(query, t)) == accountId)
                .Sum(t => DailyRations.Field(t.Reinforcements!.Soldiers, t.Reinforcements.Wounded));
        var cities = places.Where(s => s.Kind == SettlementKind.City).ToArray();
        return new(world.Clock, world.Simulation.Started, account.Treasury.Food, rations, cities.Sum(EconomyMath.Harvest),
            force.IsBarbarian && cities.Length > 0 ? EconomyMath.BarbarianAnnualFood(SoldierTotals.Living(query, forceId)) : null);
    }
}
