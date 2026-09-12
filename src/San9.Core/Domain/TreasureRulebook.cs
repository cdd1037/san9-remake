using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record TreasureRule(int Id, OfficerAbilities AbilityBonuses, int LoyaltyBonus)
{
    public bool EscapeMount { get; init; }
}

public sealed class TreasureRulebook
{
    public static TreasureRulebook Empty { get; } = new([]);

    public TreasureRulebook(IEnumerable<TreasureRule> items)
    {
        Items = items.ToImmutableDictionary(i => i.Id);
        if (Items.Values.Any(i => i.Id < 0 || !i.AbilityBonuses.IsNonnegative || i.LoyaltyBonus < 0))
            throw new ArgumentException("Invalid treasure rule.");
    }

    public ImmutableDictionary<int, TreasureRule> Items { get; }

    public static OfficerAbilities MaximumBonuses(IEnumerable<OfficerAbilities> bonuses)
    {
        OfficerAbilities result = default;
        foreach (var value in bonuses)
            result = new(Math.Max(result.War, value.War), Math.Max(result.Intelligence, value.Intelligence),
                Math.Max(result.Politics, value.Politics), Math.Max(result.Leadership, value.Leadership));
        return result;
    }

    public OfficerAbilities HeldBonuses(IEnumerable<TreasureState> treasures, int officerId) =>
        MaximumBonuses(treasures.Where(t => t.HolderId == officerId && t.Discovery != TreasureDiscovery.Disabled)
            .Select(t => Items[t.Id].AbilityBonuses));

}
