using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Population forecasts use the same participating population and troops as quarterly updates.</summary>
public static class PopulationOutlook
{
    public static long DraftGrowth(WorldSnapshot snapshot, SettlementState city)
    {
        var world = new WorldQuery(snapshot);
        if (!world.Settlements.TryGetValue(city.Id, out var current) || current.IsDestroyed || current.Kind != SettlementKind.City)
            return 0;
        var totals = Totals(world);
        long growth = EconomyMath.DraftIncrease(totals.Population, totals.Military, totals.Order, city.PublicOrder);
        return Math.Min(Math.Max(500000L, city.DraftPopulation), city.DraftPopulation + growth) - city.DraftPopulation;
    }

    internal static (long Population, long Military, long Order) Totals(IWorldQuery world)
    {
        var cities = world.Settlements.Values.Where(s => !s.IsDestroyed && s.Kind == SettlementKind.City).ToArray();
        return (cities.Sum(s => (long)s.Population), cities.Sum(s => (long)s.DraftPopulation) + SoldierTotals.Living(world),
            cities.Sum(s => (long)s.PublicOrder));
    }
}
