using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record DomesticPair(DomesticActivity Project, int FirstId, int SecondId, bool FirstRequiresRuler, bool SecondRequiresRuler);
public sealed record OfficeSalary(int OfficeId, int Amount);
public sealed record EconomyRulebook(ImmutableArray<OfficeSalary> Salaries, ImmutableArray<DomesticPair> Pairs,
    int? ImperialSealId = null)
{
    public FoodMarketDefinition? FoodMarket { get; init; }
    public ImperialCourtDefinition ImperialCourt { get; init; } = ImperialCourtDefinition.Empty;
    public ImmutableDictionary<int, int> RegionCityIds { get; init; } = ImmutableDictionary<int, int>.Empty;
    public static EconomyRulebook Empty { get; } = new([], []);
    public const long BarbarianQuarterlyFunds = 30000;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(ImperialCourt);
        ImperialCourt.Validate();
        FoodMarket?.Validate();
        if (Salaries.IsDefault || Pairs.IsDefault || Salaries.Any(s => s.OfficeId < 0 || s.Amount < 0) ||
            Salaries.Select(s => s.OfficeId).Distinct().Count() != Salaries.Length ||
            Pairs.Any(p => !Enum.IsDefined(p.Project) || p.Project == DomesticActivity.Recruit || p.FirstId < 0 || p.SecondId < 0 || p.FirstId == p.SecondId) ||
            Pairs.Select(p => (p.Project, p.FirstId, p.SecondId)).Distinct().Count() != Pairs.Length || ImperialSealId < 0)
            throw new ArgumentException("Invalid economy definitions.");
    }
}

public static class EconomyMath
{
    public static long BarbarianAnnualFood(long soldiers) => Math.Clamp(soldiers, 100000, 1000000);
    public static int SalaryLoyaltyChance(bool paid, int trust, int affinityDistance, int righteousness) =>
        paid ? (trust / 10 - affinityDistance + 50) / 20 : 5 * (16 - righteousness);

    public static long SquareRoot(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        long low = 0, high = Math.Min(value, 3037000499L) + 1;
        while (low + 1 < high)
        {
            long middle = low + (high - low) / 2;
            if (middle <= value / middle) low = middle; else high = middle;
        }
        return low;
    }

    public static long ExecutedAbility(int ability) => Math.Max(1, 2L * (Math.Min(ability, 60) / 3)) +
        Math.Max(0, Math.Min(ability, 80) - 60) + Math.Max(0, 2L * ability - 160);
    public static int AffinityDistance(int first, int second) => Math.Min(Math.Abs(first - second), 150 - Math.Abs(first - second));
    public static int RecruitMorale(int trust) => (int)Math.Min(100, SquareRoot(trust) + 40);
    public static long DraftIncrease(long population, long militaryPopulation, long totalOrder, int cityOrder) =>
        totalOrder == 0 ? 0 : (Math.Max(0, population - militaryPopulation + 99999) / 100000 * (population / 100) / 10) * cityOrder / totalOrder;
    public static long Revenue(SettlementState city) =>
        ((SquareRoot((long)city.Population + city.DraftPopulation) + 600) * city.Commerce * 6 / 1000) * (2500 - city.Commerce) / 2500;
    public static long Harvest(SettlementState city) =>
        ((SquareRoot((long)city.Population + city.DraftPopulation) + 300) * city.Cultivation / 10) * (4000 - city.Cultivation) / 4000;
}
