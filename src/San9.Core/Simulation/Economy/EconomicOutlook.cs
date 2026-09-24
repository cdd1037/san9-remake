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
        var force = world.Forces.Single(f => f.Id == forceId);
        var corps = world.Corps.Where(c => c.OwnerId == forceId).ToDictionary(c => c.Id);
        if (corpsId is int requested && !corps.ContainsKey(requested)) throw new ArgumentOutOfRangeException(nameof(corpsId));
        int? Account(int? id) => id is int key && corps.TryGetValue(key, out var c) && c.Treasury is not null ? key : null;
        var officerCorps = world.Officers.ToDictionary(o => o.Id, o => o.UnitId is int unit ? world.Units.Single(u => u.Id == unit).CorpsId
            : o.SettlementId is int place ? world.Settlements.Single(s => s.Id == place).CorpsId : null);
        int?[] accounts = corpsId is not null ? [Account(corpsId)] : [null, .. corps.Values.Where(c => c.Treasury is not null).Select(c => (int?)c.Id)];
        int dayOfYear = (world.Clock.Month - 1) * 30 + world.Clock.Day - 1;
        int moneyDays = 90 - dayOfYear % 90;
        int foodDays = (180 - dayOfYear + 359) % 360 + 1;
        int salaryPayments = (moneyDays + 9) / 10;
        List<ResourceOutlook> money = [], food = [];
        foreach (int? account in accounts)
        {
            bool Own(int? owner, int? id) => owner == forceId && Account(id) == account;
            var places = world.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && Own(s.OwnerId,s.CorpsId)).ToArray();
            var cities = places.Where(s => s.Kind == SettlementKind.City).ToArray();
            long salary = world.Officers.Where(o => o.IsServing && o.Status != OfficerStatus.Ruler && Own(o.OwnerId,officerCorps[o.Id]))
                .Sum(o => (long)(o.OfficeId is int office && salaries.TryGetValue(office, out int amount) ? amount : 1));
            long rations = places.Where(s => s.ConsumesFood).Sum(s => DailyRations.Garrison(s.Soldiers,s.Wounded)) +
                world.Units.Where(u => Own(u.OwnerId,u.CorpsId)).Sum(u => DailyRations.Field(u.Soldiers,u.Wounded));
            foreach (var task in world.Simulation.Tasks.Where(t => t.ForceId == forceId && t.Reinforcements is not null))
            {
                var origin = world.Settlements.FirstOrDefault(s => s.Id == task.OriginSettlementId && !s.IsDestroyed && s.OwnerId == forceId);
                if (Account(origin?.CorpsId) == account) rations += DailyRations.Field(task.Reinforcements!.Soldiers, task.Reinforcements.Wounded);
            }
            var treasury = account is int id ? corps[id].Treasury : null;
            money.Add(Project(treasury?.Money ?? force.Money, salary * salaryPayments, cities.Sum(EconomyMath.Revenue)));
            food.Add(Project(treasury?.Food ?? force.Food, rations * foodDays, cities.Sum(EconomyMath.Harvest)));
        }
        return new(Sum(money), Sum(food));
    }
    private static ResourceOutlook Project(long balance, long costs, long income) => new(balance,costs,income,
        Math.Min(1000000, Math.Max(0, balance-costs)+income));
    private static ResourceOutlook Sum(IEnumerable<ResourceOutlook> values) => new(values.Sum(v=>v.Balance), values.Sum(v=>v.Expenditure), values.Sum(v=>v.Income), values.Sum(v=>v.Forecast));
}
