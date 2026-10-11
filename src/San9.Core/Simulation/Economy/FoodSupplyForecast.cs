using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Known account values only; no task outcomes, world handles or random state.</summary>
public sealed record FoodSupplyInput(GameClock Clock, GameClock Started, long Balance, long DailyConsumption,
    long Harvest, long? ReplacementSupply);
public sealed record FoodSupplyChange(int Days, long DailyConsumptionChange, long FoodDelivery = 0);

public static class FoodSupplyForecast
{
    public static uint SustainableRecruitment(uint soldiers, uint wounded, uint maximum, int days, long availableFood)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(days);
        if (availableFood < 0) return 0;
        uint low = 0, high = maximum;
        while (low < high)
        {
            uint middle = low + (uint)(((ulong)high - low + 1) / 2);
            long cost = (GarrisonConsumption(checked(soldiers + middle), wounded) - GarrisonConsumption(soldiers, wounded)) * days;
            if (cost <= availableFood) low = middle;
            else high = middle - 1;
        }
        return low;
    }
    public static long FieldConsumption(uint soldiers, uint wounded = 0) => DailyRations.Field(soldiers, wounded);
    public static long GarrisonConsumption(uint soldiers, uint wounded = 0) => DailyRations.Garrison(soldiers, wounded);

    /// <summary>Maximum cumulative deficit, so future deliveries cannot pay consumption before their arrival.</summary>
    public static long RequiredFood(long dailyConsumption, int horizonDays, IEnumerable<FoodSupplyChange> changes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dailyConsumption);
        ArgumentOutOfRangeException.ThrowIfNegative(horizonDays);
        long spent = 0, required = 0;
        int previous = 0;
        foreach (var change in changes.Where(c => c.Days <= horizonDays).OrderBy(c => c.Days))
        {
            ArgumentOutOfRangeException.ThrowIfNegative(change.Days);
            ArgumentOutOfRangeException.ThrowIfNegative(change.FoodDelivery);
            spent = checked(spent + dailyConsumption * (change.Days - previous));
            required = Math.Max(required, spent);
            spent -= change.FoodDelivery;
            dailyConsumption = checked(dailyConsumption + change.DailyConsumptionChange);
            ArgumentOutOfRangeException.ThrowIfNegative(dailyConsumption);
            previous = change.Days;
        }
        return Math.Max(required, checked(spent + dailyConsumption * (horizonDays - previous)));
    }
    /// <summary>Additional food needed while soldiers travel rather than remain in their garrison.</summary>
    public static long DeploymentExpenditure(uint sourceSoldiers, uint deployedSoldiers, int days, uint sourceWounded = 0, bool sourceConsumesFood = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(days);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(deployedSoldiers, sourceSoldiers);
        long before = sourceConsumesFood ? DailyRations.Garrison(sourceSoldiers, sourceWounded) : 0;
        long remaining = sourceConsumesFood ? DailyRations.Garrison(sourceSoldiers - deployedSoldiers, sourceWounded) : 0;
        return checked(Math.Max(0, remaining + DailyRations.Field(deployedSoldiers, 0) - before) * days);
    }

    public static long ReinforcementExpenditure(uint targetSoldiers, uint receivedSoldiers, int days, uint targetWounded = 0, bool targetConsumesFood = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(days);
        return targetConsumesFood ? checked((DailyRations.Garrison(checked(targetSoldiers + receivedSoldiers), targetWounded) -
            DailyRations.Garrison(targetSoldiers, targetWounded)) * days) : 0;
    }

    public static long TransferExpenditure(uint sourceSoldiers, uint targetSoldiers, uint soldiers, int arrivalDays, int horizonDays,
        uint sourceWounded = 0, uint targetWounded = 0, bool sourceConsumesFood = true, bool targetConsumesFood = true)
    {
        int travel = Math.Min(arrivalDays, horizonDays);
        long afterArrival = (sourceConsumesFood ? DailyRations.Garrison(sourceSoldiers - soldiers, sourceWounded) - DailyRations.Garrison(sourceSoldiers, sourceWounded) : 0) +
            (targetConsumesFood ? DailyRations.Garrison(checked(targetSoldiers + soldiers), targetWounded) - DailyRations.Garrison(targetSoldiers, targetWounded) : 0);
        return checked(DeploymentExpenditure(sourceSoldiers, soldiers, travel, sourceWounded, sourceConsumesFood) + Math.Max(0, afterArrival) * Math.Max(0, horizonDays - travel));
    }

    public static int DaysUntilHarvest(FoodSupplyInput input)
    {
        int dayOfYear = (input.Clock.Month - 1) * 30 + input.Clock.Day - 1;
        int days = (180 - dayOfYear + 359) % 360 + 1;
        return input.Clock.DayNumber - input.Started.DayNumber + days < 30 ? days + 360 : days;
    }
    public static ResourceOutlook Evaluate(FoodSupplyInput input)
    {
        int days = DaysUntilHarvest(input);
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
