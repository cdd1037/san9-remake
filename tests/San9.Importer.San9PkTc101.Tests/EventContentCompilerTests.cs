using San9.Importer.San9PkTc101.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class EventContentCompilerTests
{
    [Fact]
    public void TongqueTowerBindsTreasureRankAndSuccessOnlyConsumption()
    {
        foreach (byte mode in new byte[] { 0, 1 })
        {
            var events = EventContentCompiler.SearchEvents(mode, [Person(497), Person(569)], [0, 1], new HashSet<int> { 5, 73, 178, 128, 96, 82 });
            var d = Assert.Single(events.OfType<TreasureMonumentSearchEventDefinition>());
            Assert.Equal((5, 81, 2, 25000u, 10000u, 5), (d.PlaceId, d.TreasureId, d.MinimumRankId, d.MinimumMoneyExclusive, d.Cost, d.BaseTrustReward));
            Assert.Equal(EventConsumption.OnSuccess, d.Consumption);
            Assert.True(Assert.Single(events.OfType<BeastSearchEventDefinition>()).Priority > d.Priority);
            Assert.Contains(d.ConsumptionKey, EventContentCompiler.ConsumedKeys([1u << 20, 0, 0, 0], new(190, 1, 1)));
            Assert.DoesNotContain(d.ConsumptionKey, EventContentCompiler.ConsumedKeys([0, 0, 0, 0], new(300, 1, 1)));
        }
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.SearchEvents(0, [Person(497), Person(569)], [0, 1], new HashSet<int> { 73, 178, 128, 96, 82 }));
    }

    [Fact]
    public void FengshanConvertsSiteRankAndGlobalConsumptionWithoutDatePreset()
    {
        foreach (byte mode in new byte[] { 0, 1 })
        {
            var events = EventContentCompiler.SearchEvents(mode, [Person(497), Person(569)], [0, 1], new HashSet<int> { 5, 73, 178, 128, 96, 82 });
            var d = Assert.Single(events.OfType<ImperialCeremonySearchEventDefinition>());
            Assert.Equal((82, 0, 5, 50, 10), (d.PlaceId, d.RequiredRankId, d.AbilityReward, d.BaseTrustReward, d.LoyaltyReward));
            Assert.Equal(d, events.OrderByDescending(e => e.Priority).First());
            Assert.Contains(d.ConsumptionKey, EventContentCompiler.ConsumedKeys([1u << 7, 0, 0, 0], new(190, 1, 1)));
            Assert.DoesNotContain(d.ConsumptionKey, EventContentCompiler.ConsumedKeys([0, 0, 0, 0], new(300, 1, 1)));
        }
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.SearchEvents(0, [Person(497), Person(569)], [0, 1], new HashSet<int> { 5, 73, 178, 128, 96 }));
    }

    [Fact]
    public void ScholarReturnBindsRetainedIdentityOrderedRewardsAndIndependentForceHistory()
    {
        var people = new[] { Person(569), Person(497) };
        var definitions = EventContentCompiler.SearchEvents(0, people, [0, 1], new HashSet<int> { 5, 73, 178, 128, 96, 82 });
        var d = Assert.Single(definitions.OfType<ScholarReturnSearchEventDefinition>());
        Assert.Equal((0, 96, 115, 3, 300, 1000, 3000u), (d.ScholarOfficerId, d.PlaceId, d.MaximumBaseAbilityExclusive,
            d.PoliticsChanceDivisor, d.MinimumTrustExclusive, d.MaximumBaseTrustExclusive, d.MinimumMoneyExclusive));
        Assert.Equal(new GameClock(195, 1, 1), d.EarliestDate); Assert.Equal(new GameClock(207, 12, 30), d.LatestDate);
        Assert.Equal(new[] { 10, 25, 22, 29 }, d.RewardTacticIds);
        Assert.Equal(new[] { "fengshan", "hua-tuo.healing", "cai-yan", "shenshou", "tongque-tower" }, definitions.OrderByDescending(e => e.Priority).Select(e => e.Id));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.SearchEvents(0, people, [1], new HashSet<int> { 5, 73, 178, 128, 96, 82 }));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.SearchEvents(0, [.. people, Person(569)], [0, 1, 2], new HashSet<int> { 5, 73, 178, 128, 96, 82 }));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.SearchEvents(0, people, [0, 1], new HashSet<int> { 73, 178, 128 }));
        var force = new San9ScenarioForceRecord(0, 0, 0, 1u << 22, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);
        Assert.Equal(d.SuccessHistoryKey, Assert.Single(EventContentCompiler.ForceEventHistory(force)));
        Assert.Empty(EventContentCompiler.ForceEventHistory(force with { Flags = ~(1u << 22) }));
        Assert.Contains(d.ConsumptionKey, EventContentCompiler.ConsumedKeys([1u << 16, 0, 0, 0], new(195, 1, 1)));
    }

    [Fact]
    public void ShanyangGongUsesUniqueBiographyAndInitialDateConsumption()
    {
        var people = new[] { Person(36), Person(565), Person(3) };
        var d = EventContentCompiler.ShanyangGong(0, people, [0, 1, 2])!;
        Assert.Equal(1, d.EmperorId); Assert.Equal(95, d.PlaceId); Assert.Equal("刘", d.ImperialSurname);
        Assert.NotNull(EventContentCompiler.ShanyangGong(1, people, [0, 1, 2]));
        Assert.Null(EventContentCompiler.ShanyangGong(2, people, [])); Assert.Null(EventContentCompiler.ShanyangGong(9, people, []));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.ShanyangGong(0, people, [0, 2]));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.ShanyangGong(0, [.. people, Person(565)], [0, 1, 2, 3]));
        Assert.DoesNotContain(d.ConsumptionKey, EventContentCompiler.ConsumedKeys([0, 0, 0, 0], new(234, 8, 30)));
        Assert.Contains(d.ConsumptionKey, EventContentCompiler.ConsumedKeys([0, 0, 0, 0], new(234, 9, 1)));
        Assert.Contains(d.ConsumptionKey, EventContentCompiler.ConsumedKeys([1u << 9, 0, 0, 0], new(190, 1, 1)));
    }

    [Fact]
    public void RedCliffsBindsOfficerAndExcludedRulerAndPreservesTheRegionSet()
    {
        var people = new[] { Person(36), Person(1), Person(91) };
        var definition = EventContentCompiler.RedCliffs(0, people, [0, 1, 2])!;
        Assert.Equal((2, 29, 134), (definition.OfficerId, definition.OpposingCityId, definition.OfficerRegionId));
        Assert.Equal(0, Assert.Single(definition.ExcludedRulerIds));
        Assert.Equal(new[] { 143, 32, 134, 139, 142, 31, 149, 130 }, definition.RegionIds);
        Assert.Equal((20, 1, 30000L, 90, 50, 15, 15), (definition.RequiredRelationExclusive, definition.MinimumFriendlyWaterUnits,
            definition.MinimumEnemySoldiers, definition.SoldierLossPercent, definition.BaseTrustBonus, definition.EnemyBaseTrustPenalty, definition.LoyaltyBonus));
        Assert.NotNull(EventContentCompiler.RedCliffs(1, people, [0, 1, 2]));
        Assert.Null(EventContentCompiler.RedCliffs(2, people, [])); Assert.Null(EventContentCompiler.RedCliffs(9, people, []));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.RedCliffs(0, people, [0, 1]));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.RedCliffs(0, [.. people, Person(91)], [0, 1, 2, 3]));
    }

    [Fact]
    public void ChainedPlotBindsRolesAndExpandableLoyaltyListsByBiography()
    {
        ushort[] biographies = [3, 171, 68, 266, 292, 42, 417, 160, 392, 236, 69];
        var people = biographies.Select(Person).ToArray();
        var retained = Enumerable.Range(0, people.Length).ToArray();
        var plot = EventContentCompiler.ChainedPlot(0, people, retained)!;
        Assert.Equal((2, 0, 1), (plot.RulerId, plot.SuccessorId, plot.InstigatorId));
        Assert.Equal(new[] { 1, 3 }, plot.SupporterIds);
        Assert.Equal(new[] { 10, 9, 8, 7, 6, 5, 4 }, plot.OpponentIds);
        Assert.Equal(new San9.Core.Simulation.GameClock(192, 4, 1), plot.EarliestDate);
        Assert.Equal((95, 1000, 96, 80, 2), (plot.LoyaltyThreshold, plot.TrustThreshold,
            plot.SupporterMinimumLoyalty, plot.OpponentMaximumLoyalty, plot.AffinityDivisor));
        Assert.NotNull(EventContentCompiler.ChainedPlot(1, people, retained));
        Assert.Null(EventContentCompiler.ChainedPlot(2, people, []));
        Assert.Null(EventContentCompiler.ChainedPlot(9, people, []));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.ChainedPlot(0, people, retained.Skip(1)));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.ChainedPlot(0, [.. people, Person(68)], [.. retained, 11]));
    }

    [Fact]
    public void ThreeVisitsResolvesBiographiesToReferencesWithoutAssumingPersonSlots()
    {
        var people = new[] { Person(45), Person(1), Person(39), Person(36), Person(2) };
        var definition = EventContentCompiler.ThreeVisits(0, people, [0, 1, 2, 3, 4])!;
        Assert.Equal((3, 0, 2, 138, 95, 360, 150, 10), (definition.RulerId, definition.RecruitId, definition.RecommenderId,
            definition.PlaceId, definition.RequiredLoyaltyExclusive, definition.DurationDays, definition.RecruitmentLoyalty, definition.BaseTrustBonus));
        Assert.Equal(new[] { 4, 1 }, definition.CompanionIds);
        Assert.NotNull(EventContentCompiler.ThreeVisits(1, people, [0, 1, 2, 3, 4]));
        Assert.Null(EventContentCompiler.ThreeVisits(2, people, [])); Assert.Null(EventContentCompiler.ThreeVisits(9, people, []));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.ThreeVisits(0, people, [0, 1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.ThreeVisits(0, [.. people, Person(36)], [0, 1, 2, 3, 4, 5]));
    }

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
        var people = new[] { Person(60), Person(497), Person(60), Person(569) };
        var definition = Assert.Single(EventContentCompiler.SearchEvents(0, people, [1, 2, 3], new HashSet<int> { 5, 73, 178, 128, 96, 82 }).OfType<HealingSearchEventDefinition>());
        Assert.Equal(1, definition.HealerOfficerId);
        Assert.Equal(2, Assert.Single(definition.ExcludedSearchingOfficerIds));
        Assert.Equal(6, definition.WarChanceDivisor);
        Assert.Empty(EventContentCompiler.SearchEvents(2, people, [1, 2, 3], new HashSet<int> { 5, 73, 178, 128, 96, 82 }));
        Assert.Empty(EventContentCompiler.SearchEvents(9, people, [1, 2, 3], new HashSet<int> { 5, 73, 178, 128, 96, 82 }));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.SearchEvents(0, people, [0, 2], new HashSet<int> { 5, 73, 178, 128, 96, 82 }));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void BeastSitesKeepSourceRewardOrderIndependentKeysAndModeLifespanRule(byte mode, bool loseLifespan)
    {
        var definitions = EventContentCompiler.SearchEvents(mode, [Person(497), Person(569)], [0, 1], new HashSet<int> { 5, 73, 178, 128, 96, 82 });
        var beast = Assert.Single(definitions.OfType<BeastSearchEventDefinition>());
        Assert.Equal(loseLifespan, beast.ReduceLifespanOnDefeat);
        Assert.True(Assert.Single(definitions.OfType<HealingSearchEventDefinition>()).Priority > beast.Priority);
        Assert.Equal(new[] { 1, 28 }, beast.BattleTacticIds);
        Assert.Equal(new[] { 11, 24, 22, 30, 26 }, beast.Sites.Single(s => s.PlaceId == 73).RewardTacticIds);
        Assert.Equal(new[] { 10, 23, 18, 26, 29 }, beast.Sites.Single(s => s.PlaceId == 178).RewardTacticIds);
        Assert.Equal(new[] { 13, 24, 8, 29, 30 }, beast.Sites.Single(s => s.PlaceId == 128).RewardTacticIds);
        Assert.Equal(EventContentCompiler.ConsumedKeys([7u << 17, 0, 0, 0], new(190, 1, 1)).Select(k => k.Value),
            beast.Sites.Select(s => s.ConsumptionKey!.Value.Value).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(1, "chained-plot.started")]
    [InlineData(2, "chained-plot.completed")]
    [InlineData(3, "three-visits.started")]
    [InlineData(4, "three-visits.first-visit")]
    [InlineData(5, "three-visits.completed")]
    [InlineData(7, "fengshan")]
    [InlineData(20, "tongque-tower")]
    [InlineData(8, "hanzhong-king")]
    [InlineData(10, "expedition-memorial")]
    [InlineData(11, "five-tigers")]
    [InlineData(9, "shanyang-gong")]
    [InlineData(12, "qiao-marriage")]
    [InlineData(13, "sun-marriage")]
    [InlineData(15, "red-cliffs")]
    [InlineData(16, "cai-yan")]
    [InlineData(17, "shenshou.xihe")]
    [InlineData(18, "shenshou.wanwen")]
    [InlineData(19, "shenshou.jianan")]
    public void BeastSitesRetainIndependentInitialConsumption(int bit, string key)
    {
        Assert.Equal(key, Assert.Single(EventContentCompiler.ConsumedKeys([1u << bit, 0, 0, 0], new(190, 1, 1))).Value);
    }

    [Fact]
    public void LateScenarioStartPresetsThreeVisitsCompletionWithoutClearingFileFlags()
    {
        Assert.DoesNotContain(EventContentCompiler.ConsumedKeys([0, 0, 0, 0], new(207, 9, 30)), k => k.Value.StartsWith("three-visits.", StringComparison.Ordinal));
        Assert.Equal(new[] { "chained-plot.completed", "chained-plot.started", "shenshou.xihe", "three-visits.completed", "three-visits.first-visit", "three-visits.started" },
            EventContentCompiler.ConsumedKeys([1u << 17, 0, 0, 0], new(207, 10, 1)).Select(k => k.Value));
    }

    [Fact]
    public void UnrelatedFlagsDoNotBecomeAuthoredEventKeys()
    {
        Assert.Empty(EventContentCompiler.ConsumedKeys([~((3u << 1) | (7u << 3) | (7u << 17) | (1u << 7) | (1u << 8) | (1u << 10) | (1u << 11) | (1u << 9) | (1u << 12) | (1u << 13) | (1u << 15) | (1u << 16) | (1u << 20)), uint.MaxValue, uint.MaxValue, uint.MaxValue], new(190, 1, 1)));
        Assert.Equal(["shenshou.jianan", "shenshou.wanwen", "shenshou.xihe"],
            EventContentCompiler.ConsumedKeys([7u << 17, 0, 0, 0], new(190, 1, 1)).Select(k => k.Value));
    }

    [Theory]
    [InlineData(3, 30, false, false)]
    [InlineData(4, 1, true, false)]
    [InlineData(4, 30, true, false)]
    [InlineData(5, 1, true, true)]
    public void ChainedPlotInitialDatePresetsStagesIndependently(int month, int day, bool started, bool completed)
    {
        var keys = EventContentCompiler.ConsumedKeys([0, 0, 0, 0], new(192, (byte)month, (byte)day));
        Assert.Equal(started, keys.Contains(new("chained-plot.started")));
        Assert.Equal(completed, keys.Contains(new("chained-plot.completed")));
        Assert.Contains(new("chained-plot.completed"), EventContentCompiler.ConsumedKeys([1u << 2, 0, 0, 0], new(190, 1, 1)));
    }

    [Fact]
    public void RedCliffsInitialCutoffStartsAt209AndDoesNotClearAnEarlierFileFlag()
    {
        var key = new PersistentEventKey("red-cliffs");
        Assert.DoesNotContain(key, EventContentCompiler.ConsumedKeys([0, 0, 0, 0], new(208, 12, 30)));
        Assert.Contains(key, EventContentCompiler.ConsumedKeys([0, 0, 0, 0], new(209, 1, 1)));
        Assert.Contains(key, EventContentCompiler.ConsumedKeys([1u << 15, 0, 0, 0], new(190, 1, 1)));
    }
}
