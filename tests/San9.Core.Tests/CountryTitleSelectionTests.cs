using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class CountryTitleSelectionTests
{
    private static PoliticalRulebook Rules() => new(null, [],
        [new(8, true, true, false, [], [5000]), new(4000, true, true, false, [60], []),
         new(99, true, true, false, [], []), new(5, false, false, false, [61], [])],
        new Dictionary<int, int> { [10] = 5, [20] = 99 });

    [Fact]
    public void RulerThenBloodlineThenControlledHomeProvinceUseNoRandomAndRespectOccupation()
    {
        ulong Never() => throw new Exception("Unexpected random draw.");
        var rules = Rules();
        Assert.Equal(4000, rules.SelectAutomatic(60, 5000, 20, [20], new HashSet<int>(), Never));
        Assert.Equal(8, rules.SelectAutomatic(60, 5000, 20, [20], new HashSet<int> { 4000 }, Never));
        Assert.Equal(99, rules.SelectAutomatic(60, 5000, 20, [20], new HashSet<int> { 4000, 8 }, Never));
        Assert.Equal(new[] { 8, 99 }, rules.ManualCandidates(new HashSet<int> { 4000 }));
    }

    [Fact]
    public void FailedHomeProvinceStaysInRandomPoolAndUnavailableProvinceConsumesDraw()
    {
        int draws = 0; ulong Roll() { draws++; return 0; }
        Assert.Equal(99, Rules().SelectAutomatic(999, null, 10, [20, 10], new HashSet<int>(), Roll));
        Assert.Equal(2, draws);
    }

    [Fact]
    public void UncontrolledHomeProvinceIsNotPreferredAndFallbackIsStableUnderPermutation()
    {
        var rules = Rules(); int draws = 0;
        Assert.Equal(8, rules.SelectAutomatic(999, null, 20, [], new HashSet<int>(), () => { draws++; return 0; }));
        Assert.Equal(1, draws);
        var reversed = new PoliticalRulebook(null, [], rules.Titles.Values.Reverse(), rules.ProvinceTitles);
        Assert.Equal(rules.SelectAutomatic(999, null, null, [10, 20], new HashSet<int>(), () => 1),
            reversed.SelectAutomatic(999, null, null, [20, 10], new HashSet<int>(), () => 1));
    }

    [Fact]
    public void NoAutomaticTitlesReturnsWithoutRandomEvenWhenProvincesExist()
    {
        Assert.Null(Rules().SelectAutomatic(61, null, null, [10, 20], new HashSet<int> { 8, 99, 4000 },
            () => throw new Exception("Unexpected random draw.")));
    }

    [Fact]
    public void AuthoredCatalogIsExpandableBeyondSourceSlots()
    {
        var rules = new PoliticalRulebook(null, [], Enumerable.Range(1000, 70).Select(id =>
            new CountryTitleRule(id, true, true, false, [], [])), ImmutableDictionary<int, int>.Empty);
        Assert.Equal(70, rules.ManualCandidates(new HashSet<int>()).Length);
        Assert.Equal(1069, rules.SelectAutomatic(1, null, null, [], new HashSet<int>(), () => 69));
    }

    [Fact]
    public void AutomaticOnlyTitleIsNotARandomFallbackAndAmbiguousPreferencesAreRejected()
    {
        var rules = new PoliticalRulebook(null, [], [new(9000, false, true, false, [], [])], ImmutableDictionary<int, int>.Empty);
        Assert.Null(rules.SelectAutomatic(1, null, null, [], new HashSet<int>(), () => throw new Exception()));
        Assert.Throws<ArgumentException>(() => new PoliticalRulebook(null, [],
            [new(1, true, true, false, [50], []), new(2, true, true, false, [50], [])], ImmutableDictionary<int, int>.Empty));
    }
}
