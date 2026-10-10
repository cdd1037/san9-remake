using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Population forecasts use the same participating population and troops as quarterly updates.</summary>
public sealed class PopulationOutlook
{
    private readonly IReadOnlyDictionary<int, SettlementState> _places;
    private readonly (long Population, long Military, long Order) _totals;

    public PopulationOutlook(WorldSnapshot snapshot)
    {
        var world = new WorldQuery(snapshot);
        _places = world.Settlements;
        _totals = Totals(world);
    }

    public long DraftGrowth(int cityId, int? publicOrder = null)
    {
        if (!_places.TryGetValue(cityId, out var city) || city.IsDestroyed || city.Kind != SettlementKind.City)
            return 0;
        long growth = EconomyMath.DraftIncrease(_totals.Population, _totals.Military, _totals.Order, publicOrder ?? city.PublicOrder);
        return Math.Min(Math.Max(500000L, city.DraftPopulation), city.DraftPopulation + growth) - city.DraftPopulation;
    }

    internal static (long Population, long Military, long Order) Totals(IWorldQuery world)
    {
        var cities = world.Settlements.Values.Where(s => !s.IsDestroyed && s.Kind == SettlementKind.City).ToArray();
        return (cities.Sum(s => (long)s.Population), cities.Sum(s => (long)s.DraftPopulation) + SoldierTotals.Living(world),
            cities.Sum(s => (long)s.PublicOrder));
    }
}
