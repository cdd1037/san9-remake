using San9.Importer.San9PkTc101.Scenarios;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class EventContentCompilerTests
{
    private static San9ScenarioPersonRecord Person(ushort biography) => new(
        0, 6000, 0, 0, 0, 0, 0, biography, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        [], 0, 0, [], 0);

    [Fact]
    public void HealingReferencesUseRetainedPersonIdentityAndModesExcludeTheCandidate()
    {
        var people = new[] { Person(60), Person(497), Person(60) };
        var definition = Assert.Single(EventContentCompiler.SearchEvents(0, people, [1, 2], new HashSet<int> { 73, 178, 128 }).OfType<HealingSearchEventDefinition>());
        Assert.Equal(1, definition.HealerOfficerId);
        Assert.Equal(2, Assert.Single(definition.ExcludedSearchingOfficerIds));
        Assert.Equal(6, definition.WarChanceDivisor);
        Assert.Empty(EventContentCompiler.SearchEvents(2, people, [1, 2], new HashSet<int> { 73, 178, 128 }));
        Assert.Empty(EventContentCompiler.SearchEvents(9, people, [1, 2], new HashSet<int> { 73, 178, 128 }));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.SearchEvents(0, people, [0, 2], new HashSet<int> { 73, 178, 128 }));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void BeastSitesKeepSourceRewardOrderIndependentKeysAndModeLifespanRule(byte mode, bool loseLifespan)
    {
        var definitions = EventContentCompiler.SearchEvents(mode, [Person(497)], [0], new HashSet<int> { 73, 178, 128 });
        var beast = Assert.Single(definitions.OfType<BeastSearchEventDefinition>());
        Assert.Equal(loseLifespan, beast.ReduceLifespanOnDefeat);
        Assert.True(Assert.Single(definitions.OfType<HealingSearchEventDefinition>()).Priority > beast.Priority);
        Assert.Equal(new[] { 1, 28 }, beast.BattleTacticIds);
        Assert.Equal(new[] { 11, 24, 22, 30, 26 }, beast.Sites.Single(s => s.PlaceId == 73).RewardTacticIds);
        Assert.Equal(new[] { 10, 23, 18, 26, 29 }, beast.Sites.Single(s => s.PlaceId == 178).RewardTacticIds);
        Assert.Equal(new[] { 13, 24, 8, 29, 30 }, beast.Sites.Single(s => s.PlaceId == 128).RewardTacticIds);
        Assert.Equal(EventContentCompiler.ConsumedKeys([7u << 17, 0, 0, 0]).Select(k => k.Value),
            beast.Sites.Select(s => s.ConsumptionKey!.Value.Value).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(9, "shanyang-gong")]
    [InlineData(17, "shenshou.xihe")]
    [InlineData(18, "shenshou.wanwen")]
    [InlineData(19, "shenshou.jianan")]
    public void BeastSitesRetainIndependentInitialConsumption(int bit, string key)
    {
        Assert.Equal(key, Assert.Single(EventContentCompiler.ConsumedKeys([1u << bit, 0, 0, 0])).Value);
    }

    [Fact]
    public void UnrelatedFlagsDoNotBecomeAuthoredEventKeys()
    {
        Assert.Empty(EventContentCompiler.ConsumedKeys([~((7u << 17) | (1u << 9)), uint.MaxValue, uint.MaxValue, uint.MaxValue]));
        Assert.Equal(["shenshou.jianan", "shenshou.wanwen", "shenshou.xihe"],
            EventContentCompiler.ConsumedKeys([7u << 17, 0, 0, 0]).Select(k => k.Value));
    }
}
