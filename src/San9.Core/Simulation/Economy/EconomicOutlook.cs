using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ResourceOutlook(long Balance, long Expenditure, long Income, long Forecast)
{
    public bool Sufficient => Balance >= Expenditure;
}
public sealed record EconomicOutlook(ResourceOutlook Money, ResourceOutlook Food)
{
    /// <summary>Static current-state estimate to the next quarterly revenue / July harvest.
    /// Each account retains its own capacity and shortage floor. No simulation or RNG is advanced.</summary>
    public static EconomicOutlook Read(WorldSnapshot world, IReadOnlyDictionary<int, int> salaries, int forceId, int? corpsId = null)
    {
        var force = world.Forces.Single(f => f.Id == forceId && !f.IsDormant && !f.IsEliminated);
        var query = new WorldQuery(world);
        var corps = world.Corps.Where(c => c.OwnerId == forceId).ToDictionary(c => c.Id);
        if (corpsId is int requested && !corps.ContainsKey(requested)) throw new ArgumentOutOfRangeException(nameof(corpsId));
        int rootAccount = corps.Values.Single(c => c.IsRoot).Id;
        int Account(int? id) => id ?? rootAccount;
        var places = query.Settlements.Values.Where(s => !s.IsDestroyed && s.OwnerId == forceId)
            .ToLookup(s => Account(s.CorpsId));
        var payroll = query.Officers.Values.Where(o => o.IsServing && o.Status != OfficerStatus.Ruler && o.OwnerId == forceId)
            .ToLookup(o => Account(PersonnelLocations.Corps(query, o)),
                o => (long)(o.OfficeId is int office && salaries.TryGetValue(office, out int amount) ? amount : 1));
        int[] accounts = corpsId is int selected ? [selected] : [.. corps.Keys];
        int dayOfYear = (world.Clock.Month - 1) * 30 + world.Clock.Day - 1;
        int moneyDays = 90 - dayOfYear % 90;
        long elapsed = world.Clock.DayNumber - world.Simulation.Started.DayNumber;
        if (elapsed + moneyDays < 30) moneyDays += 90;
        int salaryPayments = (moneyDays + 9) / 10;
        List<ResourceOutlook> money = [], food = [];
        foreach (int account in accounts)
        {
            var cities = places[account].Where(s => s.Kind == SettlementKind.City).ToArray();
            long salary = payroll[account].Sum();
            var treasury = corps[account].Treasury;
            bool supplied = force.IsBarbarian && cities.Length > 0;
            money.Add(Project(treasury.Money, salary * salaryPayments, cities.Sum(EconomyMath.Revenue),
                supplied ? EconomyRulebook.BarbarianQuarterlyFunds : null));
            food.Add(FoodSupplyForecast.Evaluate(FoodSupplyForecast.Capture(world, forceId, account)));
        }
        return new(Sum(money), Sum(food));
    }
    private static ResourceOutlook Project(long balance, long costs, long income, long? replacement)
    {
        long remaining = Math.Max(0, balance - costs);
        return replacement is long target ? new(balance, costs, target - remaining, target) :
            new(balance, costs, income, Math.Min(1000000, remaining + income));
    }
    private static ResourceOutlook Sum(IEnumerable<ResourceOutlook> values) => new(values.Sum(v=>v.Balance), values.Sum(v=>v.Expenditure), values.Sum(v=>v.Income), values.Sum(v=>v.Forecast));
}
