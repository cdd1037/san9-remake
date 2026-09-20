using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class HistoricalRecognitionTests
{
    private static WorldState World() => WorldState.Create(new(190, 1, 1), 17,
        [new(1, 10000, 10000, 600) { LeaderId = 11, BaseTrust = 600, RankId = 500, CountryTitleId = 42 },
         new(2, 10000, 10000, 100) { LeaderId = 21, BaseTrust = 100, RankId = 500 }],
        Enumerable.Range(1, 3).Select(id => new SettlementState(id, id == 3 ? 2 : 1, 100, 100, 100, 50, 100, 100, 100, 100, 100)
            { Kind = SettlementKind.City, ConsumesFood = false }),
        Enumerable.Range(11, 8).Select(id => new OfficerState(id, 1, id == 18 ? 3 : 1, new(50, 50, 50, 50), false)
            { Status = id == 11 ? OfficerStatus.Ruler : id == 18 ? OfficerStatus.Captive : OfficerStatus.Serving }.WithLoyalty(1, 40))
            .Append(new(21, 2, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
        controls: [new("player", 1, ControllerKind.Player), new("ai", 2, ControllerKind.Automatic)]);
    private static WorldMutation Quarter(WorldState? state = null)
    { var world = (state ?? World()).BeginMutation(); world.SetClock(new(190, 4, 1)); return world; }
    private static SimulationRules Rules() => new()
    {
        HanzhongKing = new(11, 1, 200, 30, 600, 20, new("hanzhong-king")),
        FiveTigers = new([12, 13, 14, 15, 16], 30, 15, 200, [101, 102], new("five-tigers")),
        ExpeditionMemorial = new(17, 42, 12, 3, 10, 15, [103, 104], new("expedition-memorial")),
        Politics = new(null, [new(500, 10, 0, 0), new(200, 30, 900, 50), new(100, 50, 1000, 60)],
            [new(42, true, true, false, [], [])], new Dictionary<int, int>()),
        Personnel = new(Enumerable.Range(1, 3).Select(id => new PersonPlace(id, new(id, id))),
            cityProvinces: new Dictionary<int, int> { [1] = 12, [2] = 12, [3] = 13 })
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KingUsesCurrentTrustAndRankPrecedenceAndRewardsCaptivesWithoutPlayerRestriction(bool player)
    {
        var state = World(); var world = Quarter(state); List<DomainEvent> events = [];
        if (!player) world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] });
        world.Replace(world.Forces[1] with { BaseTrust = 995 });
        Assert.True(HistoricalRecognition.ApplyHanzhongKing(world, Rules(), events));
        var reward = Assert.Single(events.OfType<PeerageChangedEvent>());
        Assert.Equal((500, 200, PeerageChangeKind.HanzhongKing), (reward.PreviousRankId, reward.RankId, reward.Kind));
        Assert.Equal(1000, world.Forces[1].BaseTrust); Assert.Equal(42, world.Forces[1].CountryTitleId);
        Assert.Equal(90, world.Officers[18].Loyalty); Assert.Equal(40, world.Officers[11].Loyalty);
        Assert.Empty(world.Simulation.RandomStreams); state.Commit(world);
        Assert.False(HistoricalRecognition.ApplyHanzhongKing(state.BeginMutation(), Rules(), events));
        Assert.Single(events.OfType<PeerageChangedEvent>());
    }

    [Theory]
    [InlineData("early")]
    [InlineData("court-ended")]
    [InlineData("not-ruler")]
    [InlineData("trust")]
    [InlineData("equal-rank")]
    [InlineData("higher-rank")]
    [InlineData("lost-city")]
    [InlineData("destroyed-city")]
    public void KingEligibilityFailureIsRetryableAndWritesNothing(string failure)
    {
        var world = Quarter(); var rules = Rules();
        switch (failure)
        {
            case "early": world.SetSimulation(world.Simulation with { Started = new(190, 3, 2) }); break;
            case "court-ended": world.SetSimulation(world.Simulation with { ImperialCourtEnded = true }); break;
            case "not-ruler": world.Replace(world.Officers[11] with { Status = OfficerStatus.Serving }); break;
            case "trust": world.Replace(world.Forces[1] with { Trust = 599 }); break;
            case "equal-rank": world.Replace(world.Forces[1] with { RankId = 200 }); break;
            case "higher-rank": world.Replace(world.Forces[1] with { RankId = 100 }); break;
            case "lost-city": world.Replace(world.Settlements[1] with { OwnerId = 2 }); break;
            case "destroyed-city": world.Replace(world.Settlements[1] with { IsDestroyed = true }); break;
        }
        List<DomainEvent> events = []; Assert.False(HistoricalRecognition.ApplyHanzhongKing(world, rules, events));
        Assert.Empty(events); Assert.Empty(world.Simulation.ConsumedEventKeys); Assert.Empty(world.Simulation.RandomStreams);
    }

    [Fact]
    public void KingAndTigersAllowExactThirtyDayBoundaryWithoutYearRequirement()
    {
        var world = Quarter(); world.SetSimulation(world.Simulation with { Started = new(190, 3, 1) });
        Assert.True(HistoricalRecognition.ApplyHanzhongKing(world, Rules(), []));
        Assert.True(HistoricalRecognition.ApplyFiveTigers(world, Rules(), []));
    }

    [Fact]
    public void TigersIncludeCaptiveAndAddLoyaltyRatherThanSettingItAndNeedNoKingOrCourt()
    {
        var state = World(); var world = Quarter(state); List<DomainEvent> events = [];
        world.Replace(world.Officers[12] with { Status = OfficerStatus.Captive, SettlementId = 3 });
        world.Replace(world.Officers[13].WithLoyalty(1, 254));
        world.SetSimulation(world.Simulation with { ImperialCourtEnded = true });
        Assert.True(HistoricalRecognition.ApplyFiveTigers(world, Rules(), events));
        Assert.Equal(240, world.Officers[12].Loyalty); Assert.Equal(255, world.Officers[13].Loyalty);
        Assert.Equal(new[] { 101, 102 }, world.Officers[12].LearnedTactics);
        Assert.Empty(world.Officers[11].LearnedTactics); Assert.Empty(world.Officers[17].LearnedTactics);
        Assert.Equal(500, world.Forces[1].RankId); Assert.Equal(615, world.Forces[1].BaseTrust);
        Assert.Single(events.OfType<FiveTigersRecognizedEvent>()); Assert.Empty(world.Simulation.RandomStreams);
        state.Commit(world); Assert.False(HistoricalRecognition.ApplyFiveTigers(state.BeginMutation(), Rules(), events));
    }

    [Theory]
    [InlineData("automatic")]
    [InlineData("foreign")]
    [InlineData("ruler")]
    [InlineData("dead")]
    [InlineData("early")]
    public void TigersRequirePlayerAndAllAffiliatedNonRulers(string failure)
    {
        var world = Quarter();
        switch (failure)
        {
            case "automatic": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
            case "foreign": world.Replace(world.Officers[16] with { OwnerId = 2 }); break;
            case "ruler": world.Replace(world.Officers[16] with { Status = OfficerStatus.Ruler }); break;
            case "dead": world.Replace(world.Officers[16] with { Status = OfficerStatus.Dead }); break;
            case "early": world.SetSimulation(world.Simulation with { Started = new(190, 3, 2) }); break;
        }
        List<DomainEvent> events = []; Assert.False(HistoricalRecognition.ApplyFiveTigers(world, Rules(), events));
        Assert.Empty(events); Assert.Empty(world.Simulation.ConsumedEventKeys);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MemorialHasNoElapsedGateAndRewardsOnlyServingPeopleWithPlayerOnlyLearning(bool player)
    {
        var state = World(); var world = state.BeginMutation(); List<DomainEvent> events = [];
        if (!player) world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] });
        world.SetSimulation(world.Simulation with { ImperialCourtEnded = true });
        Assert.True(HistoricalRecognition.ApplyExpeditionMemorial(world, Rules(), events));
        var result = Assert.Single(events.OfType<ExpeditionMemorialEvent>());
        Assert.Equal(player, result.GrantedTactics); Assert.Equal(Enumerable.Range(11, 7), result.RewardedOfficerIds);
        Assert.Equal(55, world.Officers[17].Loyalty); Assert.Equal(40, world.Officers[11].Loyalty);
        Assert.Equal(40, world.Officers[18].Loyalty); Assert.Empty(world.Officers[18].LearnedTactics);
        Assert.Equal(player ? new[] { 103, 104 } : [], world.Officers[11].LearnedTactics);
        Assert.Equal(610, world.Forces[1].BaseTrust); Assert.Empty(world.Simulation.RandomStreams);
        state.Commit(world); Assert.False(HistoricalRecognition.ApplyExpeditionMemorial(state.BeginMutation(), Rules(), events));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("captive-author")]
    [InlineData("ruler-author")]
    [InlineData("lost-province-city")]
    [InlineData("destroyed-province-city")]
    [InlineData("empty-opposing-city")]
    [InlineData("own-opposing-city")]
    [InlineData("destroyed-opposing-city")]
    [InlineData("non-quarter")]
    [InlineData("terminal")]
    public void MemorialFailuresDoNotConsumeOrReward(string failure)
    {
        var world = Quarter();
        switch (failure)
        {
            case "title": world.Replace(world.Forces[1] with { CountryTitleId = null }); break;
            case "captive-author": world.Replace(world.Officers[17] with { Status = OfficerStatus.Captive }); break;
            case "ruler-author": world.Replace(world.Officers[17] with { Status = OfficerStatus.Ruler }); break;
            case "lost-province-city": world.Replace(world.Settlements[2] with { OwnerId = 2 }); break;
            case "destroyed-province-city": world.Replace(world.Settlements[2] with { IsDestroyed = true }); break;
            case "empty-opposing-city": world.Replace(world.Settlements[3] with { OwnerId = null }); break;
            case "own-opposing-city": world.Replace(world.Settlements[3] with { OwnerId = 1 }); break;
            case "destroyed-opposing-city": world.Replace(world.Settlements[3] with { IsDestroyed = true }); break;
            case "non-quarter": world.SetClock(new(190, 2, 1)); break;
            case "terminal": world.SetSimulation(world.Simulation with { TerminalReason = "ended" }); break;
        }
        List<DomainEvent> events = []; Assert.False(HistoricalRecognition.ApplyExpeditionMemorial(world, Rules(), events));
        Assert.Empty(events); Assert.Empty(world.Simulation.ConsumedEventKeys);
    }

    [Fact]
    public void AuthoredRecognitionRosterCanExceedFiveOfficersAndFailedCityGateCanRecover()
    {
        var world = Quarter(); var rules = Rules(); List<DomainEvent> events = [];
        world.Replace(world.Settlements[1] with { OwnerId = 2 });
        Assert.False(HistoricalRecognition.ApplyHanzhongKing(world, rules, events));
        Assert.Empty(world.Simulation.ConsumedEventKeys);
        world.Replace(world.Settlements[1] with { OwnerId = 1 });
        Assert.True(HistoricalRecognition.ApplyHanzhongKing(world, rules, events));
        rules = new SimulationRules { FiveTigers = rules.FiveTigers! with { OfficerIds = [12, 13, 14, 15, 16, 17] } };
        rules.FiveTigers.Validate(world.Officers.Keys.ToHashSet(), new HashSet<int> { 101, 102 });
        Assert.True(HistoricalRecognition.ApplyFiveTigers(world, rules, events));
        Assert.Equal(6, Assert.Single(events.OfType<FiveTigersRecognizedEvent>()).OfficerIds.Length);
        Assert.Equal(new[] { 101, 102 }, world.Officers[17].LearnedTactics);
    }

    [Fact]
    public void FailedCandidateContinuesAndSaturatedRewardsStillStopTheQuarterTable()
    {
        var world = Quarter(); var rules = Rules(); List<DomainEvent> events = [];
        world.Replace(world.Forces[1] with { RankId = 200, BaseTrust = 1000 });
        foreach (int id in rules.FiveTigers!.OfficerIds)
            world.Replace(world.Officers[id].WithLoyalty(1, 255) with { LearnedTactics = [101, 102] });
        QuarterHistoricalEvents.Begin(world, rules, events);
        Assert.Equal(0, Assert.Single(events.OfType<FiveTigersRecognizedEvent>()).BaseTrustGain);
        Assert.Empty(events.OfType<ExpeditionMemorialEvent>());
        Assert.Equal("five-tigers", Assert.Single(world.Simulation.ConsumedEventKeys).Value);
        events.Clear(); world.SetClock(new(190, 7, 1)); QuarterHistoricalEvents.Begin(world, rules, events);
        Assert.Equal(0, Assert.Single(events.OfType<ExpeditionMemorialEvent>()).BaseTrustGain);
        Assert.Equal(2, world.Simulation.ConsumedEventKeys.Length);
    }
}
