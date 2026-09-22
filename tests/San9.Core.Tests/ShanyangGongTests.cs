using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ShanyangGongTests
{
    private static ShanyangGongDefinition Definition => new(99, 1, "刘", 700, 2, 20, 10, 11, 20, 40, new("shanyang"));
    private static SimulationRules Rules => new()
    {
        ShanyangGong = Definition,
        Politics = new(null, [new(7, 0, 0, 0), new(9, 1, 500, 5), new(12, 2, 1000, 10)],
            [new(101, true, true, false, [], []), new(102, true, true, false, [], [])], ImmutableDictionary<int, int>.Empty),
        Personnel = new(Enumerable.Range(1, 4).Select(id => new PersonPlace(id, new(0, 0))),
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3, [4] = 4 }, [],
            new Dictionary<int, int> { [1] = 10, [2] = 20, [3] = 30, [4] = 40 })
    };
    private static WorldState World() => WorldState.Create(new(190, 2, 1), 17,
        [new(1, 10000, 10000, 700) { LeaderId = 11, RankId = 7, BaseTrust = 700 },
         new(2, 10000, 10000, 500) { LeaderId = 21, RankId = 12, BaseTrust = 500, CountryTitleId = 102 },
         new(3, 10000, 10000, 500) { LeaderId = 31, RankId = 7, BaseTrust = 500 }],
        Enumerable.Range(1, 4).Select(id => new SettlementState(id, id < 3 ? 1 : id - 1, 100, 100, 100, 50, 100, 100, 100, 100, 100)
            { Kind = SettlementKind.City, ConsumesFood = false }),
        [new(11, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, Surname = "刘", BloodlineId = 800 },
         new OfficerState(12, 1, 1, new(50, 50, 50, 50), false) { Righteousness = 11 }.WithLoyalty(1, 100),
         new OfficerState(13, 1, 1, new(50, 50, 50, 50), false) { Righteousness = 10 }.WithLoyalty(1, 100),
         new OfficerState(14, 1, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive, Righteousness = 15 }.WithLoyalty(1, 100),
         new(21, 2, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, Surname = "曹" },
         new(31, 3, 4, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, Surname = "刘", BloodlineId = 900 },
         new(99, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special, Surname = "刘" }],
        relations: [new(1, 2, 90), new(1, 3, 30), new(2, 3, 50)],
        controls: [new("player", 1, ControllerKind.Player), new("ai2", 2, ControllerKind.Automatic), new("ai3", 3, ControllerKind.Automatic)],
        annualAppearancesEnabled: false, naturalDeathsEnabled: false, freeOfficerMigrationEnabled: false,
        officeRequestsEnabled: false, forceConsolidationEnabled: false, forceEmergenceEnabled: false,
        cityIndependenceEnabled: false, imperialCourtEnded: true);
    private static PersistentTask Task() => new(Guid.NewGuid(), "personnel-search", "player", 1, 12, 1,
        new(190, 2, 1), new(190, 2, 1), 0, 0)
        { Journey = new(1, null, 0) { Route = new(new(0, 0), new(0, 0), false) }, SearchEncounter = new ShanyangGongEncounter() };
    private static WorldMutation Ready()
    {
        var world = World().BeginMutation(); world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.Stable }); return world;
    }

    [Fact]
    public void ShanyangPrecedesHealingAndConsumedHistoryAllowsTheNextCandidate()
    {
        var world = Ready(); world.Replace(world.Officers[12] with { Injury = 3, BaseAbilities = new(100, 50, 50, 50) });
        var rules = new SimulationRules { ShanyangGong = Definition, Politics = Rules.Politics,
            Personnel = new(Rules.Personnel.Places.Values, cityProvinces: Rules.Personnel.CityProvinces,
                searchEvents: [new HealingSearchEventDefinition(99, [], 1) { Id = "healing" }]) };
        var rule = new PersonnelMissionRule(rules, "personnel-search"); List<DomainEvent> events = [];
        var task = Task() with { SearchEncounter = null };
        var next = rule.Resolve(world, task, null, events);
        Assert.IsType<ShanyangGongEncounter>(next!.SearchEncounter);
        Assert.Empty(events); Assert.Equal(3, world.Officers[12].Injury); Assert.Empty(world.Simulation.RandomStreams);
        world.SetSimulation(world.Simulation with { ConsumedEventKeys = [Definition.ConsumptionKey] });
        Assert.Null(rule.Resolve(world, task, null, events));
        Assert.Single(events.OfType<SearchHealingResolvedEvent>()); Assert.Equal(0, world.Officers[12].Injury);
    }

    [Theory]
    [InlineData("trust")]
    [InlineData("province")]
    [InlineData("dead")]
    [InlineData("court")]
    [InlineData("consumed")]
    [InlineData("deposed")]
    [InlineData("non-liu-emperor")]
    [InlineData("ai")]
    [InlineData("place")]
    public void FailedQualificationDoesNotConsumeOrDraw(string failure)
    {
        var world = Ready(); var task = Task();
        Assert.True(ShanyangGong.Eligible(world, task, Rules));
        switch (failure)
        {
            case "trust": world.Replace(world.Forces[1] with { BaseTrust = 699 }); break;
            case "province": world.Replace(world.Settlements[2] with { OwnerId = 2 }); break;
            case "dead": world.Replace(world.Officers[99] with { Status = OfficerStatus.Dead }); break;
            case "court": world.SetSimulation(world.Simulation with { ImperialCourtEnded = false }); break;
            case "consumed": world.SetSimulation(world.Simulation with { ConsumedEventKeys = [Definition.ConsumptionKey] }); break;
            case "deposed": world.Replace(world.Forces[1] with { HasDeposedEmperor = true }); break;
            case "non-liu-emperor":
                world.Replace(world.Forces[1] with { RankId = 12 });
                world.Replace(world.Officers[11] with { Surname = "刘备" }); break;
            case "ai": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
            case "place": task = task with { Journey = task.Journey! with { DestinationPlaceId = 2 } }; break;
        }
        var before = world.Simulation;
        Assert.False(ShanyangGong.Eligible(world, task, Rules)); Assert.Equal(before, world.Simulation);
    }

    [Fact]
    public void SurnameUsesCurrentRulerAndWholeProvinceDoesNotMeanTwoCities()
    {
        var world = Ready(); world.Replace(world.Forces[1] with { RankId = 12 });
        Assert.True(ShanyangGong.Eligible(world, Task(), Rules));
        world.Replace(world.Officers[11] with { Surname = "曹", BloodlineId = world.Officers[99].BloodlineId });
        Assert.False(ShanyangGong.Eligible(world, Task(), Rules));
        world.Replace(world.Forces[1] with { RankId = 7 });
        Assert.True(ShanyangGong.Eligible(world, Task(), Rules));
        var incomplete = new SimulationRules { ShanyangGong = Definition, Politics = Rules.Politics,
            Personnel = new(Rules.Personnel.Places.Values, cityProvinces: new Dictionary<int, int> { [1] = 10, [2] = 20, [3] = 20 }) };
        Assert.False(ShanyangGong.Eligible(world, Task(), incomplete));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BranchesUseServingLoyaltyAndDifferentDiplomaticTargets(bool protect)
    {
        var world = Ready(); world.Replace(world.Forces[1] with { CountryTitleId = 101, BaseTrust = 995 });
        world.Replace(world.Officers[12].WithLoyalty(1, protect ? 250 : 10));
        List<DomainEvent> events = [];
        Assert.Null(ShanyangGong.Resolve(world, Task(), Rules, protect, null, events));
        Assert.Equal(protect ? 255 : 0, world.Officers[12].Loyalty);
        Assert.Equal(protect ? 120 : 100, world.Officers[13].Loyalty); Assert.Equal(100, world.Officers[14].Loyalty);
        Assert.Equal(protect ? 50 : 90, ForceRelations.Get(world, 1, 2).Value);
        Assert.Equal(protect ? 30 : 0, ForceRelations.Get(world, 1, 3).Value);
        Assert.Equal(protect ? OfficerStatus.Special : OfficerStatus.Dead, world.Officers[99].Status);
        Assert.Equal(protect ? 1000 : 995, world.Forces[1].BaseTrust);
        Assert.Equal(protect ? 12 : 7, world.Forces[1].RankId); Assert.Equal(101, world.Forces[1].CountryTitleId);
        Assert.True(world.Simulation.ImperialCourtEnded); Assert.Contains(Definition.ConsumptionKey, world.Simulation.ConsumedEventKeys);
        Assert.Empty(events.OfType<SearchResolvedEvent>()); Assert.Empty(world.Simulation.RandomStreams);
        Assert.Equal(protect, Assert.Single(events.OfType<ShanyangGongEvent>()).Protected);
        if (!protect) { Assert.Null(world.Officers[99].SettlementId); Assert.Single(events.OfType<OfficerDiedEvent>()); }
    }

    [Fact]
    public void ProtectingWithoutATitleStagesSelectionAndAnEmptyCatalogDoesNotDeadlock()
    {
        var world = Ready(); List<DomainEvent> events = [];
        var next = ShanyangGong.Resolve(world, Task(), Rules, true, null, events)!;
        Assert.Equal(new ShanyangGongEncounter(true), next.SearchEncounter);
        Assert.Empty(events); Assert.Empty(world.Simulation.ConsumedEventKeys); Assert.Equal(7, world.Forces[1].RankId);
        Assert.Equal(new[] { 101 }, ShanyangGong.Candidates(world, Rules, 1));
        ShanyangGong.Resolve(world, next, Rules, true, 101, events);
        Assert.Equal(101, world.Forces[1].CountryTitleId); Assert.Equal(12, world.Forces[1].RankId);
        world = Ready(); world.Replace(world.Forces[3] with { CountryTitleId = 101 });
        Assert.Null(ShanyangGong.Resolve(world, Task(), Rules, true, null, []));
        Assert.Null(world.Forces[1].CountryTitleId); Assert.Equal(12, world.Forces[1].RankId);
    }
}
