using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class CommunitySearchTests
{
    internal static CommunitySearchEventDefinition Definition(string kind) => (kind switch
    {
        "relief" => (CommunitySearchEventDefinition)new CivicSearchEventDefinition(CivicSearchKind.DisasterRelief)
            { MinimumMoneyExclusive = 3000, MinimumFoodExclusive = 3000, CostAbilityCeiling = 120, MoneyCostMultiplier = 4, FoodCostMultiplier = 6, TrustReward = 2, TrustPenalty = 2 },
        "volunteers" => new CivicSearchEventDefinition(CivicSearchKind.Volunteers) { SoldierMultiplier = 20, MaximumForceSoldiersExclusive = 20000, Months = [1, 2, 3, 7, 8, 9] },
        "bribe" => new CivicSearchEventDefinition(CivicSearchKind.Bribe) { BaseMoneyReward = 50, MoneyAbilityMultiplier = 2, TrustReward = 2, TrustPenalty = 2 },
        "permit" => new CivicSearchEventDefinition(CivicSearchKind.TravelPermit) { BaseMoneyReward = 10, MoneyAbilityMultiplier = 2, TrustReward = 2, TrustPenalty = 2 },
        "festival" => new CivicSearchEventDefinition(CivicSearchKind.Festival) { TrustReward = 2, ExecutionTrustPenalty = 4, Months = [1, 2, 3, 7, 8, 9] },
        "monument" => new CommemorationSearchEventDefinition([new(1, null, CommunityAbility.Intelligence, [10, 11], new("monument.1"))], 1500, 2, 2)
            { Consumption = EventConsumption.OnSuccess, MinimumMoneyExclusive = 4000, MinimumTrustExclusive = 80, MaximumTrustExclusive = 1000 },
        "sacrifice" => new CommemorationSearchEventDefinition([new(1, 50, CommunityAbility.War, [10, 11], new("sacrifice.1"))], 1000, 1, 2)
            { Consumption = EventConsumption.OnSuccess, MinimumMoneyExclusive = 3500, MaximumTrustExclusive = 1000 },
        "training" => new TrainingSiteSearchEventDefinition([new(1, 2)], 500, 10000, 600, 4) { MinimumMoneyExclusive = 2500 },
        _ => throw new ArgumentException()
    }) with { Id = kind, ChanceAbility = CommunityAbility.Politics, ChanceDivisor = kind == "sacrifice" ? null : 1 };
    private static (WorldMutation World, PersistentTask Task, SimulationRules Rules, CommunitySearchEncounter Encounter) Create(string kind)
    {
        var world = WorldState.Create(new(190, 1, 1), 1,
            [new(1, 100) { LeaderId = 1, BaseTrust = 100 }],
            [new(1, 1, 11000, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 7 }],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(60, 30, 80, 70), false)],
            treasures: [new(50, 2, TreasureDiscovery.Discovered, null)],
            corps: [new(7, 1, 2, new(5000, 5000)) { ControlMode = CorpsControlMode.Direct }, new San9.Core.Domain.CorpsState(1, 1, 1, new(0, 0)) { IsRoot = true }],
            controls: [new("player", 1, ControllerKind.Player)]).BeginMutation(new OfficerAbilityRules(new([new(50, default, 0)]), new([])));
        ulong seed = TestSeeds.StateForRoll(0);
        world.SetSimulation(world.Simulation with { RandomStreams = [new("event-search-eligibility", seed)] });
        var task = new PersistentTask(Guid.NewGuid(), "personnel-search", "player", 1, 2, 1, world.Clock, world.Clock, 0, 0) { Journey = new(1, null, 0) };
        var d = Definition(kind); var rules = new SimulationRules { Personnel = new([new(1, new(0, 0))], searchEvents: [d]) };
        Assert.True(CommunitySearch.Prepare(world, task, rules.Personnel, d));
        return (world, task, rules, new(kind));
    }
    public static IEnumerable<object[]> Outcomes()
    {
        yield return ["relief", CommunitySearchChoice.DonateMoney, 102, -160L, 0L, 0u];
        yield return ["relief", CommunitySearchChoice.DonateFood, 102, 0L, -540L, 0u];
        yield return ["relief", CommunitySearchChoice.Decline, 98, 0L, 0L, 0u];
        yield return ["volunteers", CommunitySearchChoice.Accept, 100, 0L, 0L, 1400u];
        yield return ["bribe", CommunitySearchChoice.Accept, 98, 50L, 0L, 0u];
        yield return ["bribe", CommunitySearchChoice.DemandMoney, 98, 210L, 0L, 0u];
        yield return ["bribe", CommunitySearchChoice.Decline, 102, 0L, 0L, 0u];
        yield return ["permit", CommunitySearchChoice.Accept, 102, 0L, 0L, 0u];
        yield return ["permit", CommunitySearchChoice.DemandMoney, 98, 170L, 0L, 0u];
        yield return ["permit", CommunitySearchChoice.Decline, 98, 0L, 0L, 0u];
        yield return ["festival", CommunitySearchChoice.Accept, 102, 0L, 0L, 0u];
        yield return ["festival", CommunitySearchChoice.Execute, 96, 0L, 0L, 0u];
        yield return ["monument", CommunitySearchChoice.Accept, 102, -1500L, 0L, 0u];
        yield return ["sacrifice", CommunitySearchChoice.Accept, 101, -1000L, 0L, 0u];
        yield return ["training", CommunitySearchChoice.Accept, 100, -500L, 0L, 0u];
        foreach (string kind in new[] { "volunteers", "festival", "monument", "sacrifice", "training" })
            yield return [kind, CommunitySearchChoice.Decline, 100, 0L, 0L, 0u];
    }
    [Theory, MemberData(nameof(Outcomes))]
    public void EveryChoiceUsesActualCorpsAndProducesOnlyItsDeclaredEffects(string kind, CommunitySearchChoice choice, int trust, long money, long food, uint soldiers)
    {
        var (world, task, rules, encounter) = Create(kind);
        List<DomainEvent> events = []; CommunitySearch.Resolve(world, task, rules, encounter, choice, events);
        var result = Assert.Single(events.OfType<CommunitySearchResolvedEvent>());
        Assert.Equal(Math.Max(0, money), result.MoneyReward);
        Assert.Equal(trust, result.BaseTrust); Assert.Equal((uint)(5000 + money), world.Corps[7].Treasury!.Money);
        Assert.Equal((uint)(5000 + food), world.Corps[7].Treasury!.Food); Assert.Equal(0u, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Money);
        Assert.Equal(soldiers, result.Soldiers); Assert.Equal(11000u + soldiers, world.Settlements[1].Soldiers);
        Assert.Equal(2, world.Treasures[50].HolderId);
        bool memorial = kind is "monument" or "sacrifice" && choice == CommunitySearchChoice.Accept;
        Assert.Equal(memorial ? 10 : (int?)null, result.LearnedTacticId);
        Assert.Equal(memorial, !world.Simulation.ConsumedEventKeys.IsEmpty);
        Assert.Equal(kind == "monument" && memorial ? 32 : 30, world.Officers[2].BaseAbilities.Intelligence);
        Assert.Equal(kind == "sacrifice" && memorial ? 62 : 60, world.Officers[2].BaseAbilities.War);
        Assert.Equal(kind == "training" && choice == CommunitySearchChoice.Accept ? 17 : 0, world.Officers[2].TacticProficiencies[2]);
        Assert.Equal(!memorial, CommunitySearch.CanResolve(world, task, rules, encounter));
    }
    [Theory]
    [InlineData("relief", 3000)] [InlineData("monument", 4000)] [InlineData("sacrifice", 3500)] [InlineData("training", 2500)]
    public void StrictFundsGateNeverFallsBackToRichRoot(string kind, uint funds)
    {
        var (world, task, rules, encounter) = Create(kind);
        world.Replace(world.Corps[7] with { Treasury = new(funds, 5000) }); world.Replace(ResourceAccounts.Root(world, 1) with { Treasury = ResourceAccounts.Root(world, 1).Treasury with { Money = 100000 } });
        Assert.False(CommunitySearch.CanResolve(world, task, rules, encounter)); List<DomainEvent> events = [];
        Assert.Throws<InvalidOperationException>(() => CommunitySearch.Resolve(world, task, rules, encounter, CommunitySearchChoice.Decline, events));
        Assert.Empty(events); Assert.Equal(100, world.Forces[1].BaseTrust);
        world.Replace(world.Corps[7] with { Treasury = new(funds + 1, 5000) });
        Assert.True(CommunitySearch.CanResolve(world, task, rules, encounter));
    }
    [Fact]
    public void TrainingIsRawGrowthAndRespectsStrictSoldierAndProficiencyGates()
    {
        var (world, task, rules, encounter) = Create("training");
        world.Replace(world.Settlements[1] with { Soldiers = 10000 }); Assert.False(CommunitySearch.CanResolve(world, task, rules, encounter));
        world.Replace(world.Settlements[1] with { Soldiers = 10001 });
        world.Replace(world.Officers[2] with { TacticProficiencies = world.Officers[2].TacticProficiencies.SetItem(2, 600) });
        Assert.False(CommunitySearch.CanResolve(world, task, rules, encounter));
        world.Replace(world.Officers[2] with { TacticProficiencies = world.Officers[2].TacticProficiencies.SetItem(2, 599), MasteredCategories = [2] });
        List<DomainEvent> events = []; CommunitySearch.Resolve(world, task, rules, encounter, CommunitySearchChoice.Accept, events);
        Assert.Equal(616, world.Officers[2].TacticProficiencies[2]); Assert.Empty(world.Officers[2].LearnedTactics);
    }
    [Fact]
    public void SacrificeRequiresHeldItemAndDoesNotDrawEligibilityRandomness()
    {
        var (world, task, rules, encounter) = Create("sacrifice");
        var before = world.Simulation.RandomStreams;
        Assert.True(CommunitySearch.Prepare(world, task, rules.Personnel, Definition("sacrifice")));
        Assert.Equal(before, world.Simulation.RandomStreams);
        world.Replace(world.Treasures[50] with { HolderId = 1 });
        Assert.False(CommunitySearch.CanResolve(world, task, rules, encounter));
    }
    [Fact]
    public void KnownTacticsAndCappedAbilitiesStillAllowConsumption()
    {
        var (world, task, rules, encounter) = Create("monument");
        world.Replace(world.Officers[2] with { LearnedTactics = [10, 11], BaseAbilities = new(60, 99, 80, 70) });
        List<DomainEvent> events = []; CommunitySearch.Resolve(world, task, rules, encounter, CommunitySearchChoice.Accept, events);
        Assert.Equal(100, world.Officers[2].BaseAbilities.Intelligence); Assert.Null(Assert.Single(events.OfType<CommunitySearchResolvedEvent>()).LearnedTacticId);
        Assert.False(CommunitySearch.CanResolve(world, task, rules, encounter));
    }
    [Theory]
    [InlineData("relief", false)] [InlineData("permit", true)] [InlineData("festival", false)]
    public void ForeignDestinationPreservesSourceGateOrder(string kind, bool draws)
    {
        var (world, task, rules, encounter) = Create(kind); world.Replace(world.Settlements[1] with { OwnerId = null });
        var rng = new StableRandom(world.Simulation.RandomStreams.Single(s => s.Name == "event-search-eligibility").State);
        if (draws) rng.NextUInt64();
        Assert.False(CommunitySearch.Prepare(world, task, rules.Personnel, Definition(kind)));
        Assert.Equal(rng.State, world.Simulation.RandomStreams.Single(s => s.Name == "event-search-eligibility").State);
    }

    [Fact]
    public void ReliefUsesBaseAbilitiesDespiteInjuryAndRequiresBothResources()
    {
        var (world, task, rules, encounter) = Create("relief");
        world.Replace(world.Officers[2] with { Injury = 7 });
        var options = CommunitySearch.Options(world, task, Definition("relief"));
        Assert.Equal(160u, options[0].Cost); Assert.Equal(540u, options[1].Cost);
        world.Replace(world.Corps[7] with { Treasury = new(5000, 3000) }); Assert.False(CommunitySearch.CanResolve(world, task, rules, encounter));
        world.Replace(world.Corps[7] with { Treasury = new(5000, 3001) }); Assert.True(CommunitySearch.CanResolve(world, task, rules, encounter));
        world.Replace(world.Officers[2] with { BaseAbilities = new(60, 150, 150, 70) });
        Assert.All(CommunitySearch.Options(world, task, Definition("relief")), o => Assert.Equal(0u, o.Cost));
    }
    [Theory]
    [InlineData(80, false)] [InlineData(81, true)] [InlineData(999, true)] [InlineData(1000, false)]
    public void MonumentTrustLimitsAreStrict(int trust, bool eligible)
    {
        var (world, task, rules, encounter) = Create("monument"); world.Replace(world.Forces[1] with { BaseTrust = trust });
        Assert.Equal(eligible, CommunitySearch.CanResolve(world, task, rules, encounter));
    }
    [Theory]
    [InlineData("volunteers")] [InlineData("festival")]
    public void SeasonalEventsExcludeAprilAndAllowJuly(string kind)
    {
        var (world, task, rules, encounter) = Create(kind);
        world.SetClock(new(190, 4, 1)); Assert.False(CommunitySearch.CanResolve(world, task, rules, encounter));
        world.SetClock(new(190, 7, 1)); Assert.True(CommunitySearch.CanResolve(world, task, rules, encounter));
    }
    [Fact]
    public void VolunteerHomeCapacityCapsSoldierReward()
    {
        var (world, task, rules, encounter) = Create("volunteers");
        var home = world.Settlements[1] with { SoldierCapacity = 11010 };
        world.Replace(home);
        List<DomainEvent> events = []; CommunitySearch.Resolve(world, task, rules, encounter, CommunitySearchChoice.Accept, events);
        Assert.Equal(10u, Assert.Single(events.OfType<CommunitySearchResolvedEvent>()).Soldiers);
        Assert.Equal(home.SoldierCapacity, world.Settlements[1].Soldiers);
    }

    [Theory]
    [InlineData(19999, true)] [InlineData(20000, false)] [InlineData(20001, false)]
    public void VolunteersUseWholeForceGarrisonsAndStrictTotal(uint soldiers, bool eligible)
    {
        var (world, task, rules, encounter) = Create("volunteers");
        world.Replace(world.Settlements[1] with { Soldiers = soldiers - 10000, Wounded = 100, DraftPopulation = 50000 });
        world.AddSettlement(new(2, 1, 10000, 100, 100, 50, 0, 0, 0, 0, 0)
            { Kind = SettlementKind.Facility });
        Assert.Equal(eligible, CommunitySearch.CanResolve(world, task, rules, encounter));
        world.SetSimulation(world.Simulation with { RandomStreams = [new("event-search-eligibility",
            TestSeeds.StateForRoll(0))] });
        Assert.Equal(eligible, CommunitySearch.Prepare(world, task, rules.Personnel, Definition("volunteers")));
    }

    [Theory]
    [InlineData("volunteers", 17000, false)]
    [InlineData("training", 7000, false)]
    [InlineData("training", 7001, true)]
    public void GatesIncludeTroopsCurrentlyCarriedByTasks(string kind, uint garrison, bool eligible)
    {
        var (world, task, rules, encounter) = Create(kind);
        world.Replace(world.Settlements[1] with { Soldiers = garrison });
        var escort = new PersistentTask(Guid.NewGuid(), PersonnelReturns.Kind, "player", 1, 1, 1,
            world.Clock, world.Clock.NextDay(), 0, 0) { Reinforcements = new(3000, 50, 1) };
        world.SetSimulation(world.Simulation with { Tasks = [escort] });
        Assert.Equal(eligible, CommunitySearch.CanResolve(world, task, rules, encounter));
        if (eligible)
        {
            CommunitySearch.Resolve(world, task, rules, encounter, CommunitySearchChoice.Accept, []);
            Assert.Equal(17, world.Officers[2].TacticProficiencies[2]);
        }
    }

    [Theory]
    [InlineData(10000, false)] [InlineData(10001, true)]
    public void TrainingCountsGarrisonsAcrossCorpsWithoutChangingEligibilityWhenReassigned(uint soldiers, bool eligible)
    {
        var (world, task, rules, encounter) = Create("training");
        world.Replace(world.Settlements[1] with { Soldiers = 5000 });
        world.AddSettlement(new(2, 1, soldiers - 5000, 100, 100, 50, 0, 0, 0, 0, 0));
        Assert.Equal(eligible, CommunitySearch.CanResolve(world, task, rules, encounter));
        world.Replace(world.Settlements[2] with { CorpsId = 7 });
        Assert.Equal(eligible, CommunitySearch.CanResolve(world, task, rules, encounter));
    }

    [Fact]
    public void TrainingUsesWholeForceRegardlessOfCorpsOrReturnServiceChanges()
    {
        var (world, task, rules, encounter) = Create("training");
        world.Replace(world.Settlements[1] with { Soldiers = 8000 });
        world.AddSettlement(new(2, 1, 0, 100, 100, 50, 0, 0, 0, 0, 0));
        var escort = new PersistentTask(Guid.NewGuid(), PersonnelReturns.Kind, "player", 1, 1, 2,
            world.Clock, world.Clock.NextDay(), 0, 0) { Reinforcements = new(3000, 50, 1) };
        world.SetSimulation(world.Simulation with { Tasks = [escort] });
        Assert.Equal(11000, SoldierTotals.Force(world, 1));
        Assert.True(CommunitySearch.CanResolve(world, task, rules, encounter));

        world.Replace(world.Settlements[2] with { CorpsId = 7 });
        Assert.True(CommunitySearch.CanResolve(world, task, rules, encounter));
        world.Replace(world.Settlements[2] with { OwnerId = null });
        Assert.True(CommunitySearch.CanResolve(world, task, rules, encounter));
        Assert.Equal(11000, SoldierTotals.Force(world, 1));

        // Rerouting the return does not change force eligibility.
        world.SetSimulation(world.Simulation with { Tasks = [escort with { OriginSettlementId = 1 }] });
        Assert.True(CommunitySearch.CanResolve(world, task, rules, encounter));
        // Dissolving the corps also preserves eligibility.
        world.Replace(ResourceAccounts.Root(world, 1) with { Treasury = ResourceAccounts.Root(world, 1).Treasury with { Money = 5000 } });
        world.Replace(world.Settlements[1] with { CorpsId = null });
        Assert.True(CommunitySearch.CanResolve(world, task, rules, encounter));
        Assert.Equal(11000, SoldierTotals.Corps(world, 1, null));
    }
}
