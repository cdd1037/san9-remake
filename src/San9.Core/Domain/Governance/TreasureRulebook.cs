using System.Collections.Immutable;

namespace San9.Core.Domain;

public enum TreasureRetentionGroup { Other, War, Intelligence, Politics, Leadership }

public sealed record TreasureRule(int Id, OfficerAbilities AbilityBonuses, int LoyaltyBonus)
{
    public TreasureRetentionGroup RetentionGroup { get; init; }
    public bool EscapeMount { get; init; }
    public int? TacticId { get; init; }
    public bool AutomaticDiplomacyOfferAllowed { get; init; } = true;
    public ImmutableArray<int> FavoredOwnerIds { get; init; } = [];
}

public sealed class TreasureRulebook
{
    public static TreasureRulebook Empty { get; } = new([]);

    public TreasureRulebook(IEnumerable<TreasureRule> items, EquipmentRulebook? equipment = null)
    {
        Equipment = equipment ?? EquipmentRulebook.Empty;
        Items = items.ToImmutableDictionary(i => i.Id);
        if (Items.Values.Any(i => i.Id < 0 || i.TacticId < 0 || !Enum.IsDefined(i.RetentionGroup) || !i.AbilityBonuses.IsNonnegative || i.LoyaltyBonus < 0 ||
            i.FavoredOwnerIds.IsDefault || i.FavoredOwnerIds.Any(id => id < 0) || i.FavoredOwnerIds.Distinct().Count() != i.FavoredOwnerIds.Length))
            throw new ArgumentException("Invalid treasure rule.");
    }

    public ImmutableDictionary<int, TreasureRule> Items { get; }
    public EquipmentRulebook Equipment { get; }

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
