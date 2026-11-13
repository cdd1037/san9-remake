using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SearchOutcomeTests
{
    private static SimulationEngine Engine(bool person, bool item, bool automatic = false, int politics = 500, ulong seed = 190,
        bool probeGrowth = false, ulong fundsSeed = 52, bool secondSearcher = false, bool featured = false, bool adviser = false)
    {
        var equipment = new EquipmentRulebook(featured ? [new EquipmentDefinition("featured-spear", EquipmentSlot.MainHand,
            EquipmentRarity.Precious, new(2, 0, 0, 0), new(1, 0, 0, 0)) { Name = "玄纹长戟" },
            .. item ? new[] { new EquipmentDefinition("legacy-10", EquipmentSlot.Treasure, EquipmentRarity.Precious,
                new(5, 0, 0, 0), default) { LegacyTreasureId = 10 } } : []] : []);
        var world = WorldState.Create(new(190, 1, 1), seed,
            [new(1, 0) { LeaderId = 1, AdviserId = adviser ? 5 : null }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(50, 50, politics, 50), false),
             new(3, null, null, new(50, 50, 50, 50), false) { Status = person ? OfficerStatus.Undiscovered : OfficerStatus.Dead,
                 RegionId = 30, ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 40) },
             .. secondSearcher ? new OfficerState[] { new(4,1,1,new(50,50,politics,50),false) } : [],
             .. adviser ? new OfficerState[] { new(5,1,1,new(50,100,50,50),false) { Status = OfficerStatus.Strategist } } : []],
            treasures: item ? [new(10, null, TreasureDiscovery.Buried, 30)] : [],
            controls: [new("player", 1, automatic ? ControllerKind.Automatic : ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }],
            equipment: featured ? [new EquipmentInstance(100, "featured-spear", null, null, false) { IsLost = true, SearchPlaceId = 30 },
                .. item ? new[] { new EquipmentInstance(10, "legacy-10", null, null, false) { LegacyTreasureId = 10 } } : []] : null);
        if (probeGrowth)
        {
            var mutation = world.BeginMutation();
            mutation.SetSimulation(mutation.Simulation with { RandomStreams =
                [new("personnel-politics-growth", 52), new("personnel-search-funds", fundsSeed)] });
            world.Commit(mutation);
        }
        return new(world, new SimulationRules { Personnel = new([new(1, new(0, 0)), new(30, new(300, 0))]),
            Treasures = item ? new([new(10, new(5, 0, 0, 0), 0)], equipment) : TreasureRulebook.Empty, Equipment = equipment });
    }

    [Fact]
    public void SearchDiscoversIndependentEquipmentOnceAndDoesNotEquipItAutomatically()
    {
        var engine = Engine(false, false, featured: true);
        var result = Search(engine);
        Assert.Equal(SearchResultKind.Equipment, result.Result);
        Assert.Equal(1, engine.Snapshot.Officers.Single(o => o.Id == 2).ServiceMeritFor(1));
        Assert.Equal(100, result.EquipmentId);
        var found = Assert.Single(engine.Snapshot.Equipment);
        Assert.Equal(1, found.OfficerId);
        Assert.False(found.IsLost);
        Assert.False(found.Equipped);
        Assert.Null(found.SearchPlaceId);
        var restored = WorldState.Restore(engine.CaptureStableState());
        Assert.Equal(found, Assert.Single(restored.CreateSnapshot().Equipment));
    }

    [Fact]
    public void AutomaticSearchCanDiscoverIndependentEquipmentWithoutDiscoveringLegacyTreasure()
    {
        var engine = Engine(false, true, automatic: true, featured: true);
        var result = Search(engine);
        Assert.Equal(SearchResultKind.Equipment, result.Result);
        Assert.Equal(100, result.EquipmentId);
        Assert.Equal(1, engine.Snapshot.Equipment.Single(item => item.Id == 100).OfficerId);
        Assert.Equal(TreasureDiscovery.Buried, Assert.Single(engine.Snapshot.Treasures).Discovery);
        Assert.Equal(1, engine.Snapshot.Officers.Single(o => o.Id == 2).ServiceMeritFor(1));
    }

    [Fact]
    public void SearchAdviceCountsIndependentEquipmentInSingleAndBatchQueriesWithoutMutatingWorld()
    {
        var engine = Engine(false, false, featured: true, adviser: true);
        var before = engine.CaptureStableState();
        var single = engine.GetPersonnelJourney(1, 2, 30, PersonnelJourneyKind.Search).Advice;
        var batch = engine.GetPersonnelAdvice(1, PersonnelJourneyKind.Search, [(2, 30)])[(2, 30)].Advice;
        Assert.Equal(AdviserOpinion.Favorable, single.Opinion);
        Assert.Equal(single, batch);
        San9.Tests.StateAssert.Equal(before, engine.CaptureStableState());
    }

    [Theory]
    [InlineData(false, AdviserOpinion.Favorable)]
    [InlineData(true, AdviserOpinion.Unfavorable)]
    public void SearchAdviceOnlyCountsLegacyTreasureWhenTheActorCanDiscoverIt(bool automatic, AdviserOpinion expected)
    {
        var engine = Engine(false, true, automatic: automatic, adviser: true);
        var single = engine.GetPersonnelJourney(1, 2, 30, PersonnelJourneyKind.Search).Advice;
        var batch = engine.GetPersonnelAdvice(1, PersonnelJourneyKind.Search, [(2, 30)])[(2, 30)].Advice;
        Assert.Equal(expected, single.Opinion);
        Assert.Equal(single, batch);
    }

    [Fact]
    public void BatchSearchClaimsOneTreasureOnlyOnce()
    {
        var engine = Engine(false,true,secondSearcher:true);
        Assert.True(engine.Execute(new("player"),new SearchCommand(Guid.NewGuid(),1,0,[4,2],30)).Submission.Accepted);
        engine.EndControl(new("player"));
        var events = engine.AdvanceXun().Events;
        Assert.Equal(2,events.OfType<SearchResolvedEvent>().Count());
        var treasure = Assert.Single(events.OfType<SearchResolvedEvent>(),e => e.Result == SearchResultKind.Treasure);
        Assert.Equal(2,treasure.OfficerId);
        Assert.Equal(1,Assert.Single(engine.Snapshot.Treasures).HolderId);
    }

    private static SearchResolvedEvent Search(SimulationEngine engine)
    {
        Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, [2], 30)).Submission.Accepted);
        engine.EndControl(new("player"));
        List<DomainEvent> events = [.. engine.AdvanceXun().Events];
        if (engine.GetSearchRecruitmentPrompt() is { } prompt)
        {
            var result = engine.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), prompt.ForceId,
                engine.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, false));
            Assert.True(result.Submission.Accepted);
            events.AddRange(result.Events);
        }
        return Assert.Single(events.OfType<SearchResolvedEvent>());
    }

    [Fact]
    public void DecliningDiscoveredPersonKeepsInterestAndDoesNotDrawRecruitmentOrLowerPriorityResults()
    {
        var engine = Engine(true, true);
        Assert.Equal(SearchResultKind.Person, Search(engine).Result);
        var person = engine.Snapshot.Officers.Single(p => p.Id == 3);
        Assert.Equal(OfficerStatus.Free, person.Status);
        Assert.Equal(60, person.LoyaltyTo(1));
        Assert.Equal(TreasureDiscovery.Buried, Assert.Single(engine.Snapshot.Treasures).Discovery);
        Assert.DoesNotContain(engine.Snapshot.Simulation.RandomStreams,
            s => s.Name is "personnel-search-recruitment" or "personnel-search-treasure" or "personnel-search-funds");
        Assert.Single(engine.Snapshot.Simulation.Tasks.Where(t => t.Kind == "personnel-return"));
    }

    [Fact]
    public void TreasurePrecedesMoneyAndBecomesUniqueRulerHoldingWithAbilityBonus()
    {
        var engine = Engine(false, true);
        var result = Search(engine);
        Assert.Equal(SearchResultKind.Treasure, result.Result);
        Assert.Equal(1, engine.Snapshot.Officers.Single(o => o.Id == 2).ServiceMeritFor(1));
        Assert.Equal(10, result.TreasureId);
        Assert.Equal(1, Assert.Single(engine.Snapshot.Treasures).HolderId);
        Assert.Equal(engine.Snapshot.Officers.Single(p => p.Id == 1).BaseAbilities.War + 5, engine.GetAbilities(engine.Snapshot)[1].War);
        Assert.DoesNotContain(engine.Snapshot.Simulation.RandomStreams, s => s.Name == "personnel-search-funds");
    }

    [Fact]
    public void AutomaticSearchUsesLimitedResultsAndCreditsFundsWithoutPoliticsGrowth()
    {
        var engine = Engine(false, true, true);
        uint before = engine.Snapshot.Corps.Single(c => c.IsRoot).Treasury.Money;
        var result = Search(engine);
        Assert.Equal(SearchResultKind.Money, result.Result);
        Assert.True(result.Money > 0);
        Assert.Equal(1, engine.Snapshot.Officers.Single(o => o.Id == 2).ServiceMeritFor(1));
        Assert.Equal((long)before + result.Money - 1, engine.Snapshot.Corps.Single(c => c.IsRoot).Treasury.Money); // Normal xun salary remains payable.
        Assert.Null(Assert.Single(engine.Snapshot.Treasures).HolderId);
        Assert.DoesNotContain(engine.Snapshot.Simulation.RandomStreams, s => s.Name is "personnel-search-treasure" or "personnel-politics-growth");
    }

    [Fact]
    public void EmptySearchCanFinishWithoutRewardAndStillReturn()
    {
        var engine = Engine(false, false, politics: 1);
        uint before = engine.Snapshot.Corps.Single(c => c.IsRoot).Treasury.Money;
        Assert.Equal(SearchResultKind.None, Search(engine).Result);
        Assert.Equal(before - 1, engine.Snapshot.Corps.Single(c => c.IsRoot).Treasury.Money);
        Assert.Single(engine.Snapshot.Simulation.Tasks);
    }

    [Theory]
    [InlineData(SearchResultKind.Person, 99)]
    [InlineData(SearchResultKind.Treasure, 99)]
    [InlineData(SearchResultKind.Money, 99)]
    [InlineData(SearchResultKind.None, 99)]
    [InlineData(SearchResultKind.Person, 100)]
    [InlineData(SearchResultKind.Treasure, 120)]
    public void SearchPoliticsGrowthBelongsOnlyToPersonAndTreasureResults(SearchResultKind expected, int politics)
    {
        ulong seed = Enumerable.Range(0, 1000).Select(value => (ulong)value).First(value =>
            ConditionalRandom.Percentile(value, ConditionalRandomPurpose.SearchDiscovery, new(190, 1, 1), 2, 30) == 0);
        var engine = Engine(expected == SearchResultKind.Person, expected == SearchResultKind.Treasure,
            politics: politics, seed: seed, probeGrowth: true, fundsSeed: expected == SearchResultKind.None ? 18UL : 52UL);

        Assert.Equal(expected, Search(engine).Result);

        bool grows = politics < 100 && expected is SearchResultKind.Person or SearchResultKind.Treasure;
        Assert.Equal(grows ? 100 : politics, engine.Snapshot.Officers.Single(p => p.Id == 2).BaseAbilities.Politics);
        var random = new StableRandom(52);
        random.NextUInt64();
        Assert.Equal(grows ? random.State : 52UL,
            engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "personnel-politics-growth").State);
        Assert.Equal(50, engine.Snapshot.Officers.Single(p => p.Id == 1).BaseAbilities.Politics);
    }
}
