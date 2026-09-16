using System.Collections.Immutable;

namespace San9.Core.Domain;

/// <summary>Current ruler possessions eligible for automatic compensation offers.</summary>
public static class DiplomacyTreasures
{
    public static ImmutableArray<int> Candidates(IEnumerable<TreasureState> treasures, TreasureRulebook rules,
        int sourceRulerId, int targetRulerId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceRulerId);
        ArgumentOutOfRangeException.ThrowIfNegative(targetRulerId);
        if (sourceRulerId == targetRulerId) throw new ArgumentException("Compensation requires distinct rulers.");
        Dictionary<TreasureRetentionGroup, TreasureRule> retained = [];
        List<TreasureRule> offered = [];
        foreach (var item in treasures.Where(t => t.Discovered && t.HolderId == sourceRulerId).OrderBy(t => t.Id))
        {
            var rule = rules.Items[item.Id];
            if (!rule.AutomaticDiplomacyOfferAllowed) continue;
            if (rule.FavoredOwnerIds.Contains(sourceRulerId))
            {
                // A later favored possession replaces the category anchor without releasing it.
                // Thus both it and the earlier anchor remain reserved.
                retained[rule.RetentionGroup] = rule;
                continue;
            }
            var previous = retained.GetValueOrDefault(rule.RetentionGroup);
            if (rule.FavoredOwnerIds.Contains(targetRulerId) || previous is not null &&
                (previous.FavoredOwnerIds.Contains(sourceRulerId) || Value(rule) <= Value(previous)))
            {
                offered.Add(rule);
                continue;
            }
            if (previous is not null) offered.Add(previous);
            retained[rule.RetentionGroup] = rule;
        }
        // Stable ordering preserves candidate insertion order for equal loyalty values.
        return offered.OrderBy(t => t.LoyaltyBonus).Select(t => t.Id).ToImmutableArray();
    }

    private static int Value(TreasureRule rule) => rule.RetentionGroup switch
    {
        TreasureRetentionGroup.War => rule.AbilityBonuses.War,
        TreasureRetentionGroup.Intelligence => rule.AbilityBonuses.Intelligence,
        TreasureRetentionGroup.Politics => rule.AbilityBonuses.Politics,
        TreasureRetentionGroup.Leadership => rule.AbilityBonuses.Leadership,
        _ => rule.LoyaltyBonus
    };
}
