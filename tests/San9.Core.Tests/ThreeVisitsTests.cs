using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ThreeVisitsTests
{
    private static ThreeVisitsDefinition Definition => new(11, 15, 14, [12, 13], 1, 95, 360, 150, 10,
        new("visits.start"), new("visits.first"), new("visits.end"));
    private static SimulationRules Rules => new()
    {
        ThreeVisits = Definition,
        Personnel = new([new(1, new(0, 0)), new(2, new(200, 0))], searchEvents:
            [new HealingSearchEventDefinition(14, [], 1) { Id = "healing" }])
    };
    private static WorldState World() => WorldState.Create(new(190, 2, 1), 17,
        [new(1, 100) { LeaderId = 11, BaseTrust = 100 }, new(2, 100) { LeaderId = 21 }],
        [new(1, 1, 100, 100, 100, 50, 100, 100, 100, 100, 100) { Kind = SettlementKind.City, ConsumesFood = false },
         new(2, 2, 100, 100, 100, 50, 100, 100, 100, 100, 100) { Kind = SettlementKind.City, ConsumesFood = false }],
        Enumerable.Range(11, 4).Select(id => new OfficerState(id, 1, 1, new(100, 100, 100, 100), false)
            { Status = id == 11 ? OfficerStatus.Ruler : OfficerStatus.Serving }.WithLoyalty(1, 96))
            .Append(new(15, null, 1, new(100, 100, 100, 100), false) { Status = OfficerStatus.Undiscovered })
            .Append(new(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
        controls: [new("player", 1, ControllerKind.Player), new("ai", 2, ControllerKind.Automatic)],
        annualAppearancesEnabled: false, naturalDeathsEnabled: false, freeOfficerMigrationEnabled: false,
        officeRequestsEnabled: false, forceConsolidationEnabled: false, forceEmergenceEnabled: false, cityIndependenceEnabled: false, corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }]);

    private static WorldMutation Stable(WorldState? state = null)
    {
        var world = (state ?? World()).BeginMutation();
        world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.Stable });
        return world;
    }
    private static AdvanceXunResult Advance(SimulationEngine engine)
    {
        foreach (var seat in engine.Snapshot.Simulation.Controls.Skip(engine.Snapshot.Simulation.ActiveControl))
            engine.EndControl(new(seat.ControllerId));
        return engine.AdvanceXun();
    }

    [Theory]
    [InlineData("loyalty")]
    [InlineData("captive-recommender")]
    [InlineData("captive-companion")]
    [InlineData("other-companion")]
    [InlineData("discovered")]
    [InlineData("lost-place")]
    [InlineData("computer")]
    public void RecommendationChecksCurrentQualificationWithoutConsumingOnFailure(string failure)
    {
        var world = Stable();
        switch (failure)
        {
            case "loyalty": world.Replace(world.Officers[14].WithLoyalty(1, 95)); break;
            case "captive-recommender": world.Replace(world.Officers[14] with { Status = OfficerStatus.Captive }); break;
            case "captive-companion": world.Replace(world.Officers[12] with { Status = OfficerStatus.Captive }); break;
            case "other-companion": world.Replace(world.Officers[12] with { OwnerId = 2 }); break;
            case "discovered": world.Replace(world.Officers[15] with { Status = OfficerStatus.Free }); break;
            case "lost-place": world.Replace(world.Settlements[1] with { OwnerId = 2 }); break;
            case "computer": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
        }
        List<DomainEvent> events = [];
        ThreeVisits.EnterControl(world, Rules, events);
        Assert.Empty(events); Assert.Empty(world.Simulation.ConsumedEventKeys); Assert.Null(world.Simulation.ThreeVisitsStarted);
        Assert.Empty(world.Simulation.RandomStreams);
    }

    [Fact]
    public void RecommendationOccursAtTheRelevantControlEntryAndRestoreDoesNotRepeatIt()
    {
        var state = World(); var mutation = state.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { Controls = [new("ai", 2, ControllerKind.Automatic), new("player", 1, ControllerKind.Player)] });
        state.Commit(mutation);
        var engine = new SimulationEngine(state, Rules);
        Assert.Empty(engine.InitialEvents.OfType<ThreeVisitsEvent>());
        var result = engine.EndControl(new("ai"));
        Assert.Equal(ThreeVisitsStep.Recommendation, Assert.Single(result.Events.OfType<ThreeVisitsEvent>()).Step);
        Assert.Equal(engine.Snapshot.Clock, engine.Snapshot.Simulation.ThreeVisitsStarted);
        Assert.Equal(OfficerStatus.Special, engine.Snapshot.Officers.Single(p => p.Id == 15).Status);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), Rules);
        Assert.Empty(restored.InitialEvents);
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
    }

    [Fact]
    public void TwoSearchesFinishWithoutOrdinaryRewardsAndRecruitThroughSharedReturn()
    {
        var state = World(); var engine = new SimulationEngine(state, Rules);
        var started = engine.Snapshot.Simulation.ThreeVisitsStarted;
        Assert.Equal(ThreeVisitsStep.Recommendation, Assert.Single(engine.InitialEvents.OfType<ThreeVisitsEvent>()).Step);
        for (int visit = 0; visit < 2; visit++)
        {
            Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1,
                engine.Snapshot.Simulation.NextSequence, 11, 1)).Submission.Accepted);
            var result = Advance(engine);
            Assert.Equal(visit == 0 ? ThreeVisitsStep.FirstVisit : ThreeVisitsStep.Recruitment,
                Assert.Single(result.Events.OfType<ThreeVisitsEvent>()).Step);
            Assert.DoesNotContain(result.Events, e => e is SearchResolvedEvent or SearchHealingResolvedEvent);
            Assert.Null(engine.GetSearchRecruitmentPrompt());
            Assert.Equal(started, engine.Snapshot.Simulation.ThreeVisitsStarted);
            if (visit == 1)
                Assert.Contains(result.Events.OfType<TaskStartedEvent>(), e => e.Task.Kind == "personnel-return" && e.Task.OfficerId == 15);
            else
            {
                var world = state.BeginMutation();
                world.Replace(world.Officers[14].WithLoyalty(1, 0)); state.Commit(world);
            }
        }
        var recruit = engine.Snapshot.Officers.Single(p => p.Id == 15);
        Assert.Equal((OfficerStatus.Serving, 1, 1, 150, 190), (recruit.Status, recruit.OwnerId, recruit.SettlementId, recruit.Loyalty, recruit.StatusStartYear));
        Assert.Equal(110, engine.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
        Assert.Equal(3, engine.Snapshot.Simulation.ConsumedEventKeys.Length);
        Assert.DoesNotContain(engine.Snapshot.Simulation.RandomStreams, s => s.Name.StartsWith("event-", StringComparison.Ordinal) ||
            s.Name.StartsWith("personnel-search", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpiryUsesRecommendationDateRegardlessOfFirstVisitAndCompanions(bool firstVisited)
    {
        var world = Stable(); List<DomainEvent> events = [];
        ThreeVisits.EnterControl(world, Rules, events); events.Clear();
        if (firstVisited) world.SetSimulation(world.Simulation with { ConsumedEventKeys = [Definition.FirstVisitKey, Definition.StartedKey] });
        world.Replace(world.Officers[12] with { Status = OfficerStatus.Dead });
        world.Replace(world.Officers[11] with { Status = OfficerStatus.Dead });
        world.SetClock(new(191, 1, 30)); ThreeVisits.Expire(world, Rules, events);
        Assert.Empty(events); Assert.Equal(OfficerStatus.Special, world.Officers[15].Status);
        world.SetClock(new(191, 2, 1)); ThreeVisits.Expire(world, Rules, events);
        Assert.Equal(ThreeVisitsStep.Expired, Assert.Single(events.OfType<ThreeVisitsEvent>()).Step);
        Assert.Equal(OfficerStatus.Undiscovered, world.Officers[15].Status); Assert.Equal(1, world.Officers[15].SettlementId);
        Assert.Contains(Definition.FirstVisitKey, world.Simulation.ConsumedEventKeys); Assert.Contains(Definition.CompletedKey, world.Simulation.ConsumedEventKeys);
        ThreeVisits.Expire(world, Rules, events); Assert.Single(events);
    }

    [Fact]
    public void UnbuiltVisitSiteUsesItsCityOwnershipAndRetainsRecruitsGeography()
    {
        var world = Stable();
        var rules = new SimulationRules { ThreeVisits = Definition with { PlaceId = 30 },
            Personnel = new([new(1, new(0, 0)), new(2, new(200, 0)), new(30, new(100, 0))],
                new Dictionary<int, int> { [30] = 1 }) };
        world.Replace(world.Officers[15] with { SettlementId = null, RegionId = 30 });
        List<DomainEvent> events = []; ThreeVisits.EnterControl(world, rules, events);
        Assert.Equal(ThreeVisitsStep.Recommendation, Assert.Single(events.OfType<ThreeVisitsEvent>()).Step);
        Assert.Equal((OfficerStatus.Special, (int?)null, (int?)30),
            (world.Officers[15].Status, world.Officers[15].SettlementId, world.Officers[15].RegionId));
        world.SetClock(new(191, 2, 1)); ThreeVisits.Expire(world, rules, events);
        Assert.Equal((OfficerStatus.Undiscovered, (int?)null, (int?)30),
            (world.Officers[15].Status, world.Officers[15].SettlementId, world.Officers[15].RegionId));
    }

    [Theory]
    [InlineData("wrong-searcher")]
    [InlineData("wrong-place")]
    [InlineData("captive-companion")]
    [InlineData("changed-target")]
    public void FailedVisitDoesNotConsumeOrRewardAndCanRetry(string failure)
    {
        var world = Stable(); ThreeVisits.EnterControl(world, Rules, []);
        var task = new PersistentTask(Guid.NewGuid(), "personnel-search", "player", 1, 11, 1,
            world.Clock, world.Clock, 0, 0) { Journey = new(1, null, 0) { Route = new(new(0, 0), new(0, 0), false) } };
        switch (failure)
        {
            case "wrong-searcher": task = task with { OfficerId = 12 }; break;
            case "wrong-place": task = task with { Journey = task.Journey with { DestinationPlaceId = 2 } }; break;
            case "captive-companion": world.Replace(world.Officers[12] with { Status = OfficerStatus.Captive }); break;
            case "changed-target": world.Replace(world.Officers[15] with { Status = OfficerStatus.Dead }); break;
        }
        var before = world.Simulation; List<DomainEvent> events = [];
        Assert.False(ThreeVisits.Search(world, task, Definition, Rules.Personnel, Rules.Economy, events));
        Assert.Empty(events); Assert.Equal(before, world.Simulation); Assert.Equal(100, world.Forces[1].BaseTrust);
    }

    [Fact]
    public void MissingImportedTimerExpiresAndChangedTargetIsNotResurrected()
    {
        var world = Stable(); ThreeVisits.EnterControl(world, Rules, []);
        world.SetSimulation(world.Simulation with { ThreeVisitsStarted = null });
        world.Replace(world.Officers[15] with { Status = OfficerStatus.Dead });
        List<DomainEvent> events = []; ThreeVisits.Expire(world, Rules, events); Assert.Empty(events);
        world.Replace(world.Officers[15] with { Status = OfficerStatus.Special });
        ThreeVisits.Expire(world, Rules, events); Assert.Single(events);
    }
}
