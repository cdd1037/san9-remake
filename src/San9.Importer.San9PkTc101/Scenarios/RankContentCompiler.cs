using San9.Content.Scenarios;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class RankContentCompiler
{
    // The lowest rank is a real definition, not an absent rank reference.
    public static RankDefinition[] Compile(IEnumerable<San9ScenarioRankRecord> records, Func<int, string> nameKey) =>
        records.Select(r => new RankDefinition(r.Id, nameKey(r.Id), r.CommandCapacity,
            r.PrestigeRequirement, r.PromotionLoyaltyBonus)).ToArray();
}
