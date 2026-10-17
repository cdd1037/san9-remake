using System.Collections.Immutable;
using San9.Importer.San9PkTc101.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class EventContentCompilerTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void ConflictEventsBindSemanticActorsSitesRewardsAndHistory(byte mode)
    {
        var people = new ushort[] { 296, 500, 496, 497, 3, 4, 36, 62 }.Select(Person).ToArray();
        var places = new HashSet<int> { 13, 23, 29, 40, 70, 90, 115, 122 };
        var definitions = EventContentCompiler.Conflicts(mode, people, Enumerable.Range(0, people.Length), places);
        Assert.Equal(7, definitions.Length);
        Assert.Equal(new[] { -5, -12, -19, -23, -24, -25, -26 }, definitions.Select(d => d.Priority));
        var demand = Assert.IsType<MoneyDemandSearchEventDefinition>(definitions[0]);
        Assert.Equal(new[] { 0, 1 }, demand.DemandingOfficerIds); Assert.Equal(20, demand.ChanceDivisor);
        var scholar = Assert.IsType<ScholarPunishmentSearchEventDefinition>(definitions[1]);
        Assert.Equal(new[] { 23, 29, 13, 40 }, scholar.Sites.Select(s => s.PlaceId));
        Assert.Equal(new[] { 23, 14, 26, 30 }, scholar.Sites[0].RewardTacticIds);
        Assert.Equal(new[] { 21, 22, 29, 25 }, scholar.Sites[1].RewardTacticIds);
        Assert.Equal(new[] { 27, 24, 19, 22 }, scholar.Sites[2].RewardTacticIds);
        Assert.Equal(new[] { 20, 23, 26, 30 }, scholar.Sites[3].RewardTacticIds);
        for (int i = 0; i < scholar.Sites.Length; i++)
        {
            var force = new San9ScenarioForceRecord(1, 0, 0, 1u << (18 + i), 1, 1, 1, 1, 100, 100, 0, 0, 0, 0, []);
            Assert.Equal(scholar.Sites[i].HistoryKey, Assert.Single(EventContentCompiler.ForceEventHistory(force)));
        }
        var mediation = Assert.IsType<BanditMediationSearchEventDefinition>(definitions[6]);
        Assert.Equal(new[] { 4, 5, 6, 7 }, mediation.GuaranteedOfficerIds);
        Assert.Equal(new[] { 70, 115, 90, 122 }, mediation.Sites.Select(s => s.PlaceId));
        Assert.All(definitions.Cast<ConflictSearchEventDefinition>(), d => { d.Validate(places); d.ValidateReferences(Enumerable.Range(0, people.Length).ToHashSet(), Enumerable.Range(0, 41).ToHashSet()); });
        Assert.Empty(EventContentCompiler.Conflicts(2, [], [], new HashSet<int>())); Assert.Empty(EventContentCompiler.Conflicts(9, [], [], new HashSet<int>()));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.Conflicts(mode, people, Enumerable.Range(0, people.Length), new HashSet<int>()));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.Conflicts(mode, people, [0, 1], places));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MerchantCategoriesKeepAllLocationsResourcesAndForceBanMarkers(byte mode)
    {
        var d = Assert.IsType<MerchantInvestmentDefinition>(Assert.Single(EventContentCompiler.MerchantInvestments(mode, Enumerable.Range(0, 200).ToHashSet())));
        Assert.Equal(8, d.Categories.Length); Assert.Equal(67, d.Categories.Sum(c => c.PlaceIds.Length));
        Assert.Equal(new[] { "salt", "textiles", "distribution", "trade", "livestock", "iron", "farmland", "orchard" }, d.Categories.Select(c => c.Id));
        Assert.All(d.Categories.Take(6), c => Assert.Equal(ResourceKind.Money, c.RewardResource));
        Assert.All(d.Categories.Skip(6), c => Assert.Equal(ResourceKind.Food, c.RewardResource));
        Assert.Equal((12, 50, 1000u, 100u, 5u, 20, 3, 2, 4, 5, -33), (d.PoliticsChanceDivisor, d.MinimumBaseTrustExclusive,
            d.MinimumMoneyExclusive, d.MinimumInvestment, d.InvestmentBalanceDivisor, d.MinimumSuccessChance, d.ReturnNumerator,
            d.ReturnDenominator, d.PoliticsReturnMultiplier, d.ExecutionTrustPenalty, d.Priority));
        for (int i = 0; i < 8; i++)
        {
            var force = new San9ScenarioForceRecord(1, 0, 0, 1u << (23 + i), 1, 1, 1, 1, 100, 100, 0, 0, 0, 0, []);
            Assert.Equal(d.Categories[i].BanHistoryKey, Assert.Single(EventContentCompiler.ForceEventHistory(force)));
        }
        Assert.Empty(EventContentCompiler.MerchantInvestments(2, new HashSet<int>())); Assert.Empty(EventContentCompiler.MerchantInvestments(9, new HashSet<int>()));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.MerchantInvestments(mode, new HashSet<int>()));
        var market = EventContentCompiler.FoodMarket(); market.Validate();
        Assert.Equal(new[] { 10, 8, 9, 6, 7 }, market.MonthlyRates[0]); Assert.Equal(new[] { 15, 13, 11, 14, 12 }, market.MonthlyRates[6]);
        Assert.Equal(new[] { 6, 5, 5, 5, 6 }, market.MonthlyRates[5]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TigerPreservesProvinceGateAndIsOrderedBeforeBeast(byte mode)
    {
        var tiger = Assert.IsType<TigerSearchEventDefinition>(Assert.Single(EventContentCompiler.Tigers(mode)));
        Assert.Equal(new[] { 0, 1, 2, 4, 9, 11, 12, 13 }, tiger.ProvinceIds);
        Assert.Equal((20, 260, 5, 20, 1, 1, 2, 7, -27), (tiger.WarChanceDivisor, tiger.MaximumBaseTrustExclusive,
            tiger.MaximumInjuryExclusive, tiger.MinimumBattleChance, tiger.WarReward, tiger.BaseTrustReward,
            tiger.BaseTrustPenalty, tiger.DefeatInjury, tiger.Priority));
        Assert.Equal(EventConsumption.None, tiger.Consumption);
        var events = EventContentCompiler.SearchEvents(mode, [Person(497), Person(569)], [0, 1], new HashSet<int> { 5, 82, 96, 73, 178, 128 });
        var beast = Assert.Single(events.OfType<BeastSearchEventDefinition>());
        Assert.True(tiger.Priority > beast.Priority); Assert.Equal(mode != 1, beast.ReduceLifespanOnDefeat);
        Assert.Equal(new[] { 73, 178, 128 }, beast.Sites.Select(s => s.PlaceId));
        Assert.Equal(3, beast.Sites.Select(s => s.ConsumptionKey).Distinct().Count());
        Assert.Empty(EventContentCompiler.Tigers(2)); Assert.Empty(EventContentCompiler.Tigers(9));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MysticBooksBindBothBranchesAndPreserveTheirDistinctRules(byte mode)
    {
        var people = new[] { Person(1), Person(500) }; var places = new HashSet<int> { 172 };
        var definitions = EventContentCompiler.MysticBooks(mode, people, [0, 1], places);
        var offer = Assert.IsType<MysticBookOfferSearchEventDefinition>(definitions[0]);
        var bookReturn = Assert.IsType<MysticBookReturnSearchEventDefinition>(definitions[1]);
        Assert.Equal((172, 1, 76, 3, 1500u, 500u, 100u, -13),
            (offer.PlaceId, offer.MysticOfficerId, offer.TreasureId, offer.LeadershipChanceDivisor, offer.MinimumMoneyExclusive, offer.AcceptCost, offer.DeclineCost, offer.Priority));
        Assert.Equal((172, 1, 76, 6, -14), (bookReturn.PlaceId, bookReturn.MysticOfficerId, bookReturn.TreasureId, bookReturn.LeadershipChanceDivisor, bookReturn.Priority));
        Assert.Equal(new[] { 26, 30, 29, 22 }, bookReturn.RewardTacticIds);
        Assert.All(definitions, d => Assert.Equal(EventConsumption.None, d.Consumption));
        Assert.Empty(EventContentCompiler.MysticBooks(2, [], [], new HashSet<int>())); Assert.Empty(EventContentCompiler.MysticBooks(9, [], [], new HashSet<int>()));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.MysticBooks(mode, people, [0], places));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.MysticBooks(mode, [.. people, Person(500)], [0, 1, 2], places));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.MysticBooks(mode, people, [0, 1], new HashSet<int>()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ScholarInsultBindsNiHengWithoutRequiringSpecialStatus(byte mode)
    {
        var people = new[] { Person(1), Person(296) };
        var d = Assert.IsType<ScholarInsultSearchEventDefinition>(Assert.Single(EventContentCompiler.ScholarInsults(mode, people, [0, 1])));
        Assert.Equal((1, 30, 200, 5, 115, 2, 2, 7, 2, -15), (d.ScholarOfficerId, d.IntelligenceChanceDivisor, d.MaximumBaseTrustExclusive,
            d.MaximumInjuryExclusive, d.TrustRewardIntelligenceThreshold, d.IntelligenceReward, d.BaseTrustReward, d.RetaliationInjury, d.BaseTrustPenalty, d.Priority));
        Assert.Equal(EventConsumption.None, d.Consumption);
        Assert.Empty(EventContentCompiler.ScholarInsults(2, [], [])); Assert.Empty(EventContentCompiler.ScholarInsults(9, [], []));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.ScholarInsults(mode, people, [0]));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.ScholarInsults(mode, [.. people, Person(296)], [0, 1, 2]));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void LongevityBindsHistoryAccountThresholdAndModeGate(byte mode, bool enabled)
    {
        var people = new[] { Person(1), Person(498) }; var places = new HashSet<int> { 34 };
        var d = Assert.IsType<LongevitySearchEventDefinition>(Assert.Single(EventContentCompiler.Longevity(mode, people, [0, 1], places)));
        Assert.Equal((34, 1, 3, 2500u, 500u, 7, -11), (d.PlaceId, d.DivinerOfficerId, d.IntelligenceChanceDivisor, d.MinimumMoneyExclusive, d.Cost, d.MinimumLifespanTier, d.Priority));
        Assert.Equal(enabled, d.LifespanChangesAllowed); Assert.Equal(new PersistentEventKey("guan-lu.visited"), d.RequiredHistoryKey);
        Assert.Equal(EventConsumption.None, d.Consumption);
        Assert.Empty(EventContentCompiler.Longevity(2, [], [], new HashSet<int>()));
        Assert.Empty(EventContentCompiler.Longevity(9, [], [], new HashSet<int>()));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.Longevity(mode, people, [0], places));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.Longevity(mode, [.. people, Person(498)], [0, 1, 2], places));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.Longevity(mode, people, [0, 1], new HashSet<int>()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DivinerVisitPreservesModeOneAndIndependentForceHistory(byte mode)
    {
        var people = new[] { Person(1), Person(498) }; var places = new HashSet<int> { 65 };
        var d = Assert.IsType<DivinerVisitSearchEventDefinition>(Assert.Single(EventContentCompiler.DivinerVisits(mode, people, [0, 1], places)));
        Assert.Equal((65, 1, 3, 50, -6), (d.PlaceId, d.DivinerOfficerId, d.PoliticsChanceDivisor, d.MinimumTrustExclusive, d.Priority));
        Assert.Equal(EventConsumption.None, d.Consumption);
        var force = new San9ScenarioForceRecord(0, 0, 0, 1u << 17, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);
        Assert.Equal(d.HistoryKey, Assert.Single(EventContentCompiler.ForceEventHistory(force)));
        Assert.Equal(new[] { "cai-yan.returned", "guan-lu.visited" }, EventContentCompiler.ForceEventHistory(force with { Flags = (1u << 17) | (1u << 22) }).Select(k => k.Value));
        Assert.Empty(EventContentCompiler.ForceEventHistory(force with { Flags = 0 }));
        Assert.Empty(EventContentCompiler.DivinerVisits(2, [], [], new HashSet<int>()));
        Assert.Empty(EventContentCompiler.DivinerVisits(9, [], [], new HashSet<int>()));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.DivinerVisits(mode, people, [0], places));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.DivinerVisits(mode, [.. people, Person(498)], [0, 1, 2], places));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.DivinerVisits(mode, people, [0, 1], new HashSet<int>()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PhysicianVisitsBindTwoDistinctDoctorsSitesAndBooks(byte mode)
    {
        var people = new[] { Person(497), Person(496) }; var places = new HashSet<int> { 124, 125 };
        var d = Assert.IsType<PhysicianVisitSearchEventDefinition>(Assert.Single(EventContentCompiler.PhysicianVisits(mode, people, [0, 1], places)));
        Assert.Equal((6, 75, -9, EventConsumption.None), (d.PoliticsChanceDivisor, d.MinimumBaseTrustExclusive, d.Priority, d.Consumption));
        Assert.Equal(new PhysicianVisitSite(124, 1, 73), d.Sites[0]);
        Assert.Equal(new PhysicianVisitSite(125, 0, 74), d.Sites[1]);
        Assert.Empty(EventContentCompiler.PhysicianVisits(2, [], [], new HashSet<int>()));
        Assert.Empty(EventContentCompiler.PhysicianVisits(9, [], [], new HashSet<int>()));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.PhysicianVisits(mode, people, [0], places));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.PhysicianVisits(mode, [.. people, Person(497)], [0, 1, 2], places));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.PhysicianVisits(mode, people, [0, 1], new HashSet<int> { 124 }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SageVisitsBindTheMentorAndEveryRecommendedBiography(byte mode)
    {
        var people = new[] { Person(243), Person(501), Person(45), Person(114), Person(39), Person(298), Person(353), Person(250) };
        var retained = Enumerable.Range(0, people.Length).ToArray();
        var places = new HashSet<int> { 29 };
        var d = Assert.IsType<SageVisitSearchEventDefinition>(Assert.Single(EventContentCompiler.SageVisits(mode, people, retained, places)));
        Assert.Equal((29, 1, 6, 75, 80, 3, -8), (d.PlaceId, d.SageOfficerId, d.PoliticsChanceDivisor,
            d.MinimumBaseTrustExclusive, d.MaximumRecipientLoyaltyInclusive, d.LoyaltyReward, d.Priority));
        Assert.Equal(new[] { 2, 3, 4, 5, 6, 7, 0 }, d.RecommendedOfficerIds);
        Assert.Equal(EventConsumption.None, d.Consumption);
        Assert.Empty(EventContentCompiler.SageVisits(2, [], [], new HashSet<int>()));
        Assert.Empty(EventContentCompiler.SageVisits(9, [], [], new HashSet<int>()));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.SageVisits(mode, people, retained.Skip(1), places));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.SageVisits(mode, people, retained.Where(id => id != 1), places));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.SageVisits(mode, [.. people, Person(501)], [.. retained, 8], places));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.SageVisits(mode, people, retained, new HashSet<int>()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void WomenVisitsBindFourSitesAndDistinctDateHistoryPrerequisites(byte mode)
    {
        var people = new[] { Person(569), Person(568), Person(64), Person(566), Person(63), Person(567) };
        var retained = Enumerable.Range(0, people.Length).ToArray();
        var places = new HashSet<int> { 11, 15, 23, 26 };
        var d = Assert.IsType<WomenVisitSearchEventDefinition>(Assert.Single(EventContentCompiler.WomenVisits(mode, people, retained, places)));
        Assert.Equal((-10, 8, 50, 1000, EventConsumption.None), (d.Priority, d.PoliticsChanceDivisor,
            d.MinimumBaseTrustExclusive, d.MaximumTrustExclusive, d.Consumption));
        Assert.Equal(new[] { 3, 0, 5, 1 }, d.Sites.Select(s => s.VisitorOfficerId));
        Assert.Equal(new[] { 12, 11, 24, 22 }, d.Sites[0].RewardTacticIds);
        Assert.Equal(new[] { 23, 10, 24, 17 }, d.Sites[1].RewardTacticIds);
        Assert.Equal(new[] { 13, 27, 24, 21 }, d.Sites[2].RewardTacticIds);
        Assert.Equal(new[] { 15, 19, 24, 28 }, d.Sites[3].RewardTacticIds);
        Assert.Null(d.Sites[0].EarliestScenarioStart);
        Assert.Equal(new GameClock(208, 1, 1), d.Sites[1].EarliestScenarioStart);
        Assert.Equal(new PersistentEventKey("cai-yan.returned"), d.Sites[1].AlternativeForceHistoryKey);
        Assert.Null(d.Sites[1].RequiredWorldKey);
        foreach (var qiao in d.Sites.Skip(2))
        {
            Assert.Equal(new[] { 4, 2 }, qiao.RequiredLeaderIds);
            Assert.Equal(new GameClock(200, 1, 1), qiao.EarliestScenarioStart);
            Assert.Equal(new PersistentEventKey("qiao-marriage"), qiao.RequiredWorldKey);
            Assert.Null(qiao.AlternativeForceHistoryKey);
        }
        Assert.Empty(EventContentCompiler.WomenVisits(2, [], [], new HashSet<int>()));
        Assert.Empty(EventContentCompiler.WomenVisits(9, [], [], new HashSet<int>()));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.WomenVisits(mode, people, retained.Skip(1), places));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.WomenVisits(mode, [.. people, Person(569)], [.. retained, 6], places));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.WomenVisits(mode, people, retained, new HashSet<int> { 11, 15, 23 }));
    }

    [Fact]
    public void GuanduBindsDistinctHistoricalRolesRegionsAndStartDateConsumption()
    {
        var people = new[] { Person(177), Person(5), Person(60) };
        var d = EventContentCompiler.Guandu(0, people, [0, 1, 2])!;
        Assert.Equal((2, 1, 0, 98), (d.PatronId, d.OpponentId, d.RecruitId, d.RequiredPlaceId));
        Assert.Equal(new[] { 69, 98 }, d.PatronRegionIds);
        Assert.Equal(new[] { 69, 98, 9, 65, 80 }, d.EnemyRegionIds);
        Assert.Equal((90, 20, 30000L, 90, 70), (d.LoyaltyExclusive, d.RelationExclusive, d.MinimumEnemySoldiers, d.SoldierLossPercent, d.FoodLossPercent));
        Assert.NotNull(EventContentCompiler.Guandu(1, people, [0, 1, 2]));
        Assert.Null(EventContentCompiler.Guandu(2, people, [])); Assert.Null(EventContentCompiler.Guandu(9, people, []));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.Guandu(0, people, [0, 1]));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.Guandu(0, [.. people, Person(60)], [0, 1, 2, 3]));
        Assert.DoesNotContain(d.ConsumptionKey, EventContentCompiler.ConsumedKeys([0, 0, 0, 0], new(200, 10, 30)));
        Assert.Contains(d.ConsumptionKey, EventContentCompiler.ConsumedKeys([0, 0, 0, 0], new(200, 11, 1)));
        Assert.Contains(d.ConsumptionKey, EventContentCompiler.ConsumedKeys([1u << 14, 0, 0, 0], new(190, 1, 1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PenglaiRetainsEveryBiographyGroupMemberAndUsesSemanticForceIdentity(byte mode)
    {
        var people = new[] { Person(675), Person(676), Person(677), Person(677), Person(677), Person(677), Person(1) };
        var forces = new[] { new San9.Content.Scenarios.ForceInitialState(99, "wa", 0, 100)
            { LeaderId = 0, CountryTitleId = 4 } };
        var definition = EventContentCompiler.Penglai(mode, people, Enumerable.Range(0, people.Length), forces)!;
        Assert.Equal(99, definition.ForceId);
        Assert.Equal(Enumerable.Range(0, 6), definition.OfficerIds);
        Assert.Equal((86, 45, 700, 2, 500000u, 10), (definition.PlaceId, definition.CityId,
            definition.MinimumTrust, definition.MinimumProvinces, definition.CitySoldiers, definition.BaseTrustReward));
        Assert.Equal(184, Assert.Single(definition.AttachedSettlementIds));
        Assert.Equal(-3, definition.Priority);
        Assert.Null(EventContentCompiler.Penglai(2, people, Enumerable.Range(0, people.Length), forces));
        Assert.Null(EventContentCompiler.Penglai(9, people, Enumerable.Range(0, people.Length), forces));
        Assert.Throws<InvalidDataException>(() => EventContentCompiler.Penglai(0, people, [0, 1], forces));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void PenglaiNewGameInitializationPreservesDefinitionsAndResources(int player, bool dormant)
    {
        var activation = new ForceActivationSearchEventDefinition(86, 2, 45, [184], [5], 700, 2, 500000, 10, new("penglai"))
            { Id = "penglai", Consumption = EventConsumption.OnSuccess };
        var initial = new San9.Content.Scenarios.ScenarioInitialState(San9.Content.Scenarios.ScenarioInitialState.CurrentSchemaVersion,
            "fixture", 190, 1, 1, player, [new(1, "force", 1, 10), new(2, "wa", 2, 100)],
            [new(45, "city", 2, 1, 1, 100000, 450, 700, 60, []), new(184, "port", null, 2, 2, 0, 300, 300, 0, [])], [], [])
        {
            Officers = [new("ruler", new(5, 2, 45, new(10, 20, 30, 40), false) { Status = OfficerStatus.Ruler })],
            SearchEvents = [activation], ConsumedEventKeys = [activation.ConsumptionKey]
        , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, null, new(10, 10)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(10000, 300000)) { IsRoot = true }], []) };
        var result = EventContentCompiler.InitializePenglai(initial);
        Assert.Equal(initial.Forces[1] with { IsDormant = dormant }, result.Forces[1]);
        Assert.Equal(initial.Settlements.Select(s => s with { IsDormant = dormant }), result.Settlements);
        Assert.Equal(initial.Officers[0].State with { IsDormant = dormant }, result.Officers[0].State);
        Assert.Equal(!dormant, result.ConsumedEventKeys.Contains(activation.ConsumptionKey));
    }

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
        Assert.DoesNotContain(d.SuccessHistoryKey, EventContentCompiler.ForceEventHistory(force with { Flags = ~(1u << 22) }));
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
    public void OriginalAiMapsFamilyAndRulerPreferencesIntoIndependentReferences()
    {
        var people = new[] { Person(60) with { BloodlineRootPersonCode = 77 }, Person(36) with { BloodlineRootPersonCode = 88 },
            Person(62) with { BloodlineRootPersonCode = 99 }, Person(1) };
        var policy = OriginalAiContentCompiler.Compile(people, [0, 1, 2, 3]);
        Assert.Equal(200, policy.FamilyRankBonuses[77]);
        Assert.Equal(100, policy.FamilyRankBonuses[88]);
        Assert.Equal(100, policy.FamilyRankBonuses[99]);
        Assert.Equal(new[] { 0, 1, 2 }, policy.PreferredStrategyRulerIds);
        Assert.Equal(6, policy.DiplomacyFacilityPriorities[2]);
        Assert.Throws<InvalidDataException>(() => policy.Validate(new HashSet<int> { 0, 1, 2, 3 }, null));
        (policy with { DiplomacyFacilityPriorities = ImmutableDictionary<int, int>.Empty }).Validate(new HashSet<int> { 0, 1, 2, 3 }, null);
        Assert.Equal(new[] { 0, 2 }, OriginalAiContentCompiler.Compile(people, [0, 2, 3]).PreferredStrategyRulerIds);
    }

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
    [InlineData(14, "guandu")]
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
        Assert.Equal(new[] { "chained-plot.completed", "chained-plot.started", "guandu", "shenshou.xihe", "three-visits.completed", "three-visits.first-visit", "three-visits.started" },
            EventContentCompiler.ConsumedKeys([1u << 17, 0, 0, 0], new(207, 10, 1)).Select(k => k.Value));
    }

    [Fact]
    public void UnrelatedFlagsDoNotBecomeAuthoredEventKeys()
    {
        Assert.Empty(EventContentCompiler.ConsumedKeys([~((3u << 1) | (7u << 3) | (7u << 17) | (1u << 7) | (1u << 8) | (1u << 10) | (1u << 11) | (1u << 9) | (1u << 12) | (1u << 13) | (1u << 14) | (1u << 15) | (1u << 16) | (1u << 20) | (0x7ffu << 21)), 0xfffe0000u, uint.MaxValue, uint.MaxValue], new(190, 1, 1)));
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
