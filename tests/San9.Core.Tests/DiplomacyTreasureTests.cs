using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class DiplomacyTreasureTests
{
    private static TreasureRule Item(int id, int value, int loyalty = 10,
        TreasureRetentionGroup group = TreasureRetentionGroup.War, params int[] favored) =>
        new(id, new(value, value, value, value), loyalty) { RetentionGroup = group, FavoredOwnerIds = [.. favored] };

    private static int[] Select(params TreasureRule[] rules) => DiplomacyTreasures.Candidates(
        rules.Reverse().Select(r => new TreasureState(r.Id, 100, TreasureDiscovery.Discovered, null)),
        new(rules), 100, 200).ToArray();

    [Fact]
    public void OrdinaryCategoriesKeepStrongestAndOfferLowestLoyaltyFirst()
    {
        Assert.Equal(new[] { 2, 1 }, Select(Item(1, 10, 20), Item(2, 5, 5), Item(3, 20, 30),
            Item(4, 1, 1, TreasureRetentionGroup.Leadership)));
        Assert.Empty(Select(Item(4, 0, 10, TreasureRetentionGroup.War), Item(5, 0, 1, TreasureRetentionGroup.Other)));
    }

    [Fact]
    public void NonAbilityCategoryUsesLoyaltyRatherThanUnrelatedBonuses()
    {
        Assert.Equal(new[] { 9 }, Select(Item(3, 1, 20, TreasureRetentionGroup.Other),
            Item(9, 100, 10, TreasureRetentionGroup.Other)));
    }

    [Fact]
    public void SourceFavoritesRemainReservedAndTakePriorityOverTargetFavorites()
    {
        Assert.Equal(new[] { 3 }, Select(Item(1, 20), Item(2, 1, 10, TreasureRetentionGroup.War, 100),
            Item(3, 100), Item(4, 1, 10, TreasureRetentionGroup.War, 100, 200)));
        Assert.Equal(new[] { 2, 3 }, Select(Item(1, 1, 10, TreasureRetentionGroup.War, 100), Item(2, 20), Item(3, 30)));
    }

    [Fact]
    public void TargetFavoritesCanBeOfferedEvenWhenOnlyOrStrongestInCategory()
    {
        Assert.Equal(new[] { 1 }, Select(Item(1, 100, 10, TreasureRetentionGroup.War, 200), Item(2, 10)));
        Assert.Equal(new[] { 8 }, Select(Item(8, 100, 10, TreasureRetentionGroup.Politics, 200)));
    }

    [Fact]
    public void EqualLoyaltyPreservesCandidateInsertionOrderAfterCategoryReplacement()
    {
        // Item 2 enters the offer list before a stronger item 3 releases item 1.
        Assert.Equal(new[] { 2, 1, 4 }, Select(Item(1, 20), Item(2, 10), Item(3, 30), Item(4, 30)));
    }

    [Fact]
    public void ExcludedHiddenAndOtherHoldersNeverParticipateInRetentionOrOffers()
    {
        TreasureRule[] rules = [Item(1, 100) with { AutomaticDiplomacyOfferAllowed = false },
            Item(2, 100), Item(3, 100), Item(4, 100), Item(5, 20), Item(6, 10)];
        TreasureState[] states = [new(1, 100, TreasureDiscovery.Discovered, null),
            new(2, 200, TreasureDiscovery.Discovered, null), new(3, null, TreasureDiscovery.Buried, 1),
            new(4, null, TreasureDiscovery.Disabled, null), new(5, 100, TreasureDiscovery.Discovered, null),
            new(6, 100, TreasureDiscovery.Discovered, null)];
        Assert.Equal(new[] { 6 }, DiplomacyTreasures.Candidates(states, new(rules), 100, 200));
        Assert.Throws<ArgumentException>(() => DiplomacyTreasures.Candidates(states, new(rules), 100, 100));
        Assert.Throws<ArgumentException>(() => new TreasureRulebook([Item(1, 1) with { RetentionGroup = (TreasureRetentionGroup)99 }]));
    }
}
