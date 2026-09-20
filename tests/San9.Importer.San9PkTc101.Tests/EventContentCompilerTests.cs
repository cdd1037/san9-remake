using San9.Importer.San9PkTc101.Scenarios;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class EventContentCompilerTests
{
    [Fact]
    public void SunMarriageUsesSourceFamilyRatherThanHardcodedDonorRulerAndKeepsExactUpperDate()
    {
        var people = new[] { Person(538), Person(62) with { BloodlineRootPersonCode = 613 }, Person(36) };
        var definition = EventContentCompiler.SunMarriage(0, people, [0, 1, 2])!;
        Assert.Equal((2, 0, 613), (definition.RecipientRulerId, definition.BrideId, definition.DonorBloodlineId));
        Assert.Equal((3, 70, 20, 150), (definition.MinimumActiveForces, definition.RequiredRelationExclusive, definition.RelationBonus, definition.Loyalty));
        Assert.Equal(new San9.Core.Simulation.GameClock(209, 12, 30), definition.LatestDate);
        Assert.Equal(new[] { 13, 1 }, definition.GrantedTacticIds);
        Assert.NotNull(EventContentCompiler.SunMarriage(1, people, [0, 1, 2]));
        Assert.Null(EventContentCompiler.SunMarriage(2, people, [])); Assert.Null(EventContentCompiler.SunMarriage(9, people, []));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.SunMarriage(0, people, [0, 2]));
    }

    [Fact]
    public void QiaoMarriageConvertsBiographiesAndKeepsScenarioModeSeparateFromHistoricalSetting()
    {
        var people = new[] { Person(568), Person(63), Person(60), Person(91), Person(567) };
        var definition = EventContentCompiler.QiaoMarriage(0, people, [0, 1, 2, 3, 4])!;
        Assert.Equal((1, 3, 2), (definition.RulerId, definition.CompanionId, definition.RivalRulerId));
        Assert.Equal(new[] { 4, 0 }, definition.RequiredLivingOfficerIds);
        Assert.Equal(10, definition.ProvinceId); Assert.Equal(21, Assert.Single(definition.ExcludedCityIds));
        Assert.Equal(new San9.Core.Simulation.GameClock(199, 12, 30), definition.LatestScenarioStart);
        Assert.Equal(new San9.Core.Simulation.GameClock(209, 12, 30), definition.LatestDate);
        Assert.Equal((3, 7, 20, 25), (definition.LifespanBonus, definition.LifespanCap, definition.BaseTrustBonus, definition.RelationPenalty));
        Assert.NotNull(EventContentCompiler.QiaoMarriage(1, people, [0, 1, 2, 3, 4]));
        Assert.Null(EventContentCompiler.QiaoMarriage(2, people, []));
        Assert.Null(EventContentCompiler.QiaoMarriage(9, people, []));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.QiaoMarriage(0, people, [0, 1, 2, 3]));
    }

    [Fact]
    public void HistoricalRecognitionConvertsReferencesAndExactRewardsWithoutFilteringPeople()
    {
        var people = new[] { Person(45), Person(23), Person(9), Person(36), Person(1), Person(4), Person(2) };
        int[] retained = [0, 1, 2, 3, 4, 5, 6];
        var king = EventContentCompiler.HanzhongKing(0, people, retained)!;
        Assert.Equal((3, 38, 1, 30, 600, 20), (king.RulerId, king.CityId, king.RankId, king.MinimumElapsedDays, king.RequiredTrust, king.BaseTrustBonus));
        var tigers = EventContentCompiler.FiveTigers(0, people, retained)!;
        Assert.Equal(new[] { 6, 4, 2, 5, 1 }, tigers.OfficerIds);
        Assert.Equal((30, 15, 200), (tigers.MinimumElapsedDays, tigers.BaseTrustBonus, tigers.LoyaltyBonus));
        Assert.Equal(new[] { 21, 28 }, tigers.GrantedTacticIds);
        var memorial = EventContentCompiler.ExpeditionMemorial(0, people, retained)!;
        Assert.Equal((0, 1, 12, 15, 10, 15), (memorial.OfficerId, memorial.CountryTitleId, memorial.ProvinceId,
            memorial.OpposingCityId, memorial.BaseTrustBonus, memorial.LoyaltyBonus));
        Assert.Equal(new[] { 11, 15 }, memorial.GrantedTacticIds);
        Assert.NotNull(EventContentCompiler.HanzhongKing(1, people, retained));
        Assert.NotNull(EventContentCompiler.FiveTigers(1, people, retained));
        Assert.NotNull(EventContentCompiler.ExpeditionMemorial(1, people, retained));
        foreach (byte mode in new byte[] { 2, 9 })
        {
            Assert.Null(EventContentCompiler.HanzhongKing(mode, people, []));
            Assert.Null(EventContentCompiler.FiveTigers(mode, people, []));
            Assert.Null(EventContentCompiler.ExpeditionMemorial(mode, people, []));
        }
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.HanzhongKing(0, people, [0]));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.FiveTigers(0, people, [0]));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.ExpeditionMemorial(0, people, [1]));
    }

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
    [InlineData(8, "hanzhong-king")]
    [InlineData(10, "expedition-memorial")]
    [InlineData(11, "five-tigers")]
    [InlineData(9, "shanyang-gong")]
    [InlineData(12, "qiao-marriage")]
    [InlineData(13, "sun-marriage")]
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
        Assert.Empty(EventContentCompiler.ConsumedKeys([~((7u << 17) | (1u << 8) | (1u << 10) | (1u << 11) | (1u << 9) | (1u << 12) | (1u << 13)), uint.MaxValue, uint.MaxValue, uint.MaxValue]));
        Assert.Equal(["shenshou.jianan", "shenshou.wanwen", "shenshou.xihe"],
            EventContentCompiler.ConsumedKeys([7u << 17, 0, 0, 0]).Select(k => k.Value));
    }
}
