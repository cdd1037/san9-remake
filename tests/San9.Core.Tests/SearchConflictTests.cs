using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SearchConflictTests
{
    internal static ConflictSearchEventDefinition Definition(string kind) => (kind switch
    {
        "demand" => (ConflictSearchEventDefinition)new MoneyDemandSearchEventDefinition([3, 4], 100, 2, 100) { MinimumMoneyExclusive = 3000, MaximumBaseTrustExclusive = 300 },
        "scholar" => new ScholarPunishmentSearchEventDefinition([new(1, 3, [10, 11], new("scholar.visited"))], 1) { MinimumBaseTrustExclusive = 70, TrustReward = 3 },
        "evil" => new EvildoerSearchEventDefinition(50) { RequiresOwnedPlace = true, MaximumBaseTrustExclusive = 200, Months = [4, 5, 6, 10, 11, 12], MaximumInjuryExclusive = 15 },
        "spirit" => new LandSpiritSearchEventDefinition(50, 200) { RequiresOwnedPlace = true, MaximumBaseTrustExclusive = 260, RequiresMoneyCapacity = true, Months = [4, 5, 6, 10, 11, 12] },
        "bandit" => new BanditSuppressionSearchEventDefinition(50, 6, 120, 1) { RequiresOwnedPlace = true, MaximumBaseTrustExclusive = 260, MinimumMoneyExclusive = 2000 },
        "rescue" => new TravelerRescueSearchEventDefinition(2, 50) { RequiresOwnedPlace = true, RequiresMoneyCapacity = true, TrustReward = 0 },
        "mediation" => new BanditMediationSearchEventDefinition([new(1, 4)], [4], 50, 60000, 15, 25, 1000) { RequiresOwnedPlace = true, MaximumBaseTrustExclusive = 260, RequiresMoneyCapacity = true },
        _ => throw new ArgumentException()
    }) with { Id = kind, ChanceAbility = ConflictChanceAbility.Politics, ChanceDivisor = 1 };
    private static ulong Seed(int roll) => Enumerable.Range(1, 10000).Select(i => (ulong)i).First(s => new StableRandom(s).NextUInt64() % 100 == (ulong)roll);
    private static (WorldMutation World, PersistentTask Task, SimulationRules Rules, ConflictSearchEncounter Encounter) Create(string kind, int roll = 0)
    {
        var world = WorldState.Create(new(190, 4, 1), 1,
            [new(1, 0, 0, 100) { LeaderId = 1, BaseTrust = 100 }],
            [new(1, 1, 1000, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 7 }],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(60, 30, 80, 70), false) { LifespanTier = 5 },
             new(3, null, null, default, false) { Status = OfficerStatus.Special },
             new(4, null, null, default, false) { Status = OfficerStatus.Special }],
            treasures: [new(50, 3, TreasureDiscovery.Discovered, null)],
            corps: [new(7, 1, 2, new(5000, 0)) { ControlMode = CorpsControlMode.Direct }],
            controls: [new("player", 1, ControllerKind.Player)]).BeginMutation();
        world.SetSimulation(world.Simulation with { RandomStreams = [new("event-search-eligibility", Seed(0)), new("event-search-conflict", Seed(roll))] });
        var task = new PersistentTask(Guid.NewGuid(), "personnel-search", "player", 1, 2, 1, world.Clock, world.Clock, 0, 0) { Journey = new(1, null, 0) };
        var d = Definition(kind); var rules = new SimulationRules { Personnel = new([new(1, new(0, 0))], searchEvents: [d]) };
        Assert.True(SearchConflicts.Prepare(world, task, rules.Personnel, d, out var encounter));
        return (world, task, rules, encounter!);
    }

    public static IEnumerable<object[]> Outcomes()
    {
        yield return ["demand", SearchConflictChoice.Pay, 0, 102, 0, 5, -200L];
        yield return ["demand", SearchConflictChoice.Execute, 0, 98, 7, 5, 0L];
        yield return ["scholar", SearchConflictChoice.Allow, 0, 103, 0, 5, 0L];
        yield return ["scholar", SearchConflictChoice.Execute, 0, 98, 7, 1, 0L];
        foreach (string kind in new[] { "evil", "spirit", "bandit", "rescue", "mediation" })
        {
            yield return [kind, SearchConflictChoice.Fight, 0, kind == "rescue" ? 100 : 102, 0, 5, kind == "spirit" ? 200L : kind == "rescue" ? 110L : 0L];
            yield return [kind, SearchConflictChoice.Fight, 99, 98, kind == "evil" ? 0 : 7, 5, 0L];
            yield return [kind, SearchConflictChoice.Decline, 99, 98, 0, 5, 0L];
        }
        yield return ["evil", SearchConflictChoice.Persuade, 79, 102, 0, 5, 0L];
        yield return ["evil", SearchConflictChoice.Persuade, 80, 98, 0, 5, 0L];
        yield return ["spirit", SearchConflictChoice.Persuade, 79, 102, 0, 5, 200L];
        yield return ["spirit", SearchConflictChoice.Persuade, 80, 98, 7, 5, 0L];
        yield return ["bandit", SearchConflictChoice.Appease, 99, 101, 0, 5, -240L];
        yield return ["mediation", SearchConflictChoice.MediatePeace, 59, 102, 0, 5, 0L];
        yield return ["mediation", SearchConflictChoice.MediatePeace, 60, 98, 7, 5, 0L];
    }
    [Theory]
    [MemberData(nameof(Outcomes))]
    public void AllBranchesUseTheCorrectAbilityAndAccount(string kind, SearchConflictChoice choice, int roll, int trust, int injury, int lifespan, long funds)
    {
        var (world, task, rules, encounter) = Create(kind, roll); List<DomainEvent> events = [];
        SearchConflicts.Resolve(world, task, rules, encounter, choice, events);
        var result = Assert.Single(events.OfType<SearchConflictResolvedEvent>());
        Assert.Equal((trust, injury, lifespan), (result.BaseTrust, result.Injury, result.LifespanTier));
        Assert.Equal((uint)(5000 + funds), world.Corps[7].Treasury!.Money); Assert.Equal(0u, world.Forces[1].Money);
        Assert.Equal(new ResourceAccount(ResourceOwnerKind.Corps, 7), result.Account);
        Assert.Equal(new OfficerAbilities(60, 30, 80, 70), world.Officers[2].BaseAbilities);
        if (kind == "scholar")
        {
            Assert.Contains(new PersistentEventKey("scholar.visited"), world.Forces[1].EventHistory);
            Assert.False(SearchConflicts.CanResolve(world, task, rules, encounter));
            Assert.Equal(choice == SearchConflictChoice.Execute, world.AllOfficers[3].IsDormant);
            Assert.Equal(choice == SearchConflictChoice.Allow ? 10 : (int?)null, result.LearnedTacticId);
            Assert.DoesNotContain(events, e => e is OfficerDiedEvent);
        }
        if (kind == "mediation") Assert.Equal(result.Result == EventResult.Succeeded ? 900u : 0u, result.Soldiers);
        if (choice is SearchConflictChoice.Pay or SearchConflictChoice.Execute or SearchConflictChoice.Allow or SearchConflictChoice.Decline or SearchConflictChoice.Appease)
            Assert.Null(result.Roll);
    }

    [Theory]
    [InlineData(50, 420)] [InlineData(80, 240)] [InlineData(100, 120)] [InlineData(120, 0)] [InlineData(180, 0)]
    public void AppeasementQuotesAndChargesBasePolitics(int politics, uint cost)
    {
        var (world, task, rules, encounter) = Create("bandit");
        world.Replace(world.Officers[2] with { BaseAbilities = new(60, 30, politics, 70), Injury = 1 });
        var d = (ConflictSearchEventDefinition)rules.Personnel.SearchEvents["bandit"];
        Assert.Equal(cost, SearchConflicts.Options(world, task, d).Single(o => o.Choice == SearchConflictChoice.Appease).Cost);
        List<DomainEvent> events = []; SearchConflicts.Resolve(world, task, rules, encounter, SearchConflictChoice.Appease, events);
        Assert.Equal(5000u - cost, world.Corps[7].Treasury!.Money);
    }

    [Theory]
    [InlineData(0, true, SearchConflictChoice.Fight)] [InlineData(99, false, SearchConflictChoice.Fight)]
    [InlineData(0, true, SearchConflictChoice.MediatePeace)] [InlineData(99, false, SearchConflictChoice.MediatePeace)]
    public void FavoredLeaderRewardsOnlySuccessfulMediation(int roll, bool won, SearchConflictChoice choice)
    {
        var (world, task, rules, encounter) = Create("mediation", roll);
        world.Replace(world.Forces[1] with { LeaderId = 4 });
        List<DomainEvent> events = []; SearchConflicts.Resolve(world, task, rules, encounter, choice, events);
        var result = Assert.Single(events.OfType<SearchConflictResolvedEvent>());
        Assert.Equal(won ? 102 : 98, result.BaseTrust); Assert.Equal(won ? 0 : 7, result.Injury);
        Assert.Equal(won ? 6000u : 5000u, world.Corps[7].Treasury!.Money);
        Assert.Equal(won ? 1000 : 0, result.MoneyReward);
        Assert.Equal(won ? 1500u : 0u, result.Soldiers); Assert.Equal(won ? EventResult.Succeeded : EventResult.Defeated, result.Result);
    }

    [Theory]
    [InlineData("evil", "season", true)] [InlineData("spirit", "full", true)] [InlineData("bandit", "money", true)]
    [InlineData("demand", "dead", true)] [InlineData("scholar", "history", true)]
    [InlineData("rescue", "foreign", false)] [InlineData("evil", "ai", false)]
    public void InvalidQualificationPreservesRandomGateOrder(string kind, string reason, bool draws)
    {
        var (world, task, rules, encounter) = Create(kind);
        var d = (ConflictSearchEventDefinition)rules.Personnel.SearchEvents[kind];
        switch (reason)
        {
            case "season": d = d with { Months = [1] }; break;
            case "full": world.Replace(world.Corps[7] with { Treasury = new(1000000, 0) }); break;
            case "money": world.Replace(world.Corps[7] with { Treasury = new(2000, 0) }); break;
            case "dead": world.Replace(world.Officers[3] with { Status = OfficerStatus.Dead }); world.Replace(world.Officers[4] with { Status = OfficerStatus.Dead }); break;
            case "history": world.Replace(world.Forces[1] with { EventHistory = [new("scholar.visited")] }); break;
            case "foreign": world.Replace(world.Settlements[1] with { OwnerId = null }); break;
            case "ai": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
        }
        var rng = new StableRandom(world.Simulation.RandomStreams.Single(s => s.Name == "event-search-eligibility").State);
        if (draws) rng.NextUInt64();
        Assert.False(SearchConflicts.Prepare(world, task, rules.Personnel, d, out _));
        Assert.Equal(rng.State, world.Simulation.RandomStreams.Single(s => s.Name == "event-search-eligibility").State);
    }

    [Theory]
    [InlineData(59999, true)] [InlineData(60000, false)] [InlineData(60001, false)]
    public void MediationRequiresForceSoldiersStrictlyBelowLimit(uint soldiers, bool eligible)
    {
        var (world, task, rules, encounter) = Create("mediation");
        world.Replace(world.Settlements[1] with { Soldiers = soldiers - 5000 });
        world.AddSettlement(new(2, 1, 5000, 100, 100, 50, 0, 0, 0, 0, 0));
        world.SetSimulation(world.Simulation with { RandomStreams = [new("event-search-eligibility", Seed(0)), new("event-search-conflict", Seed(0))] });
        Assert.Equal(eligible, SearchConflicts.Prepare(world, task, rules.Personnel,
            (ConflictSearchEventDefinition)rules.Personnel.SearchEvents["mediation"], out _));
        Assert.Equal(eligible, SearchConflicts.CanResolve(world, task, rules, encounter));
        if (eligible)
        {
            SearchConflicts.Resolve(world, task, rules, encounter, SearchConflictChoice.Fight, []);
            Assert.Equal(soldiers - 5000u + 900u, world.Settlements[1].Soldiers);
        }
        else
        {
            string before = CanonicalJson.Text(world.Simulation);
            List<DomainEvent> events = [];
            Assert.Throws<InvalidOperationException>(() => SearchConflicts.Resolve(world, task, rules, encounter, SearchConflictChoice.Fight, events));
            Assert.Empty(events); Assert.Equal(before, CanonicalJson.Text(world.Simulation));
            Assert.Equal(soldiers - 5000u, world.Settlements[1].Soldiers);
        }
    }

    [Fact]
    public void MediationIncludesInTransitTroopsAcrossCorps()
    {
        var (world, task, rules, encounter) = Create("mediation");
        world.Replace(world.Settlements[1] with { Soldiers = 57000 });
        var escort = new PersistentTask(Guid.NewGuid(), PersonnelReturns.Kind, "player", 1, 1, 1,
            world.Clock, world.Clock.NextDay(), 0, 0) { Reinforcements = new(3000, 50, 1) };
        world.SetSimulation(world.Simulation with { Tasks = [escort] });
        Assert.False(SearchConflicts.CanResolve(world, task, rules, encounter));
        world.Replace(world.Settlements[1] with { Soldiers = 56999 });
        Assert.True(SearchConflicts.CanResolve(world, task, rules, encounter));
        world.AddSettlement(new(2, 1, 0, 100, 100, 50, 0, 0, 0, 0, 0));
        world.SetSimulation(world.Simulation with { Tasks = [escort with { OriginSettlementId = 2 }] });
        world.Replace(world.Settlements[1] with { Soldiers = 59999 });
        Assert.False(SearchConflicts.CanResolve(world, task, rules, encounter));
        world.Replace(world.Settlements[2] with { CorpsId = 7 });
        Assert.False(SearchConflicts.CanResolve(world, task, rules, encounter));
    }

    [Fact]
    public void WithdrawalRepairsCorpsAndDropsTreasureWithoutDestroyingIdentity()
    {
        var (world, task, rules, encounter) = Create("scholar");
        world.Replace(world.Officers[3] with { OwnerId = 1, SettlementId = 1, Status = OfficerStatus.Governor });
        world.Replace(world.Corps[7] with { LeaderId = 3 });
        world.Replace(world.Forces[1] with { AdviserId = 3 });
        List<DomainEvent> events = [];
        SearchConflicts.Resolve(world, task, rules, encounter, SearchConflictChoice.Execute, events);
        Assert.True(world.AllOfficers[3].IsDormant); Assert.False(world.Officers.ContainsKey(3));
        Assert.Null(world.Forces[1].AdviserId); Assert.Null(world.Treasures[50].HolderId);
        Assert.DoesNotContain(world.Corps.Values, c => c.LeaderId == 3);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void AllKnownScholarRewardsStillConsumeHistoryAndPunishmentCannotExtendLife()
    {
        foreach (bool execute in new[] { false, true })
        {
            var (world, task, rules, encounter) = Create("scholar");
            world.Replace(world.Officers[2] with { LearnedTactics = [10, 11], LifespanTier = 0 });
            List<DomainEvent> events = [];
            SearchConflicts.Resolve(world, task, rules, encounter, execute ? SearchConflictChoice.Execute : SearchConflictChoice.Allow, events);
            Assert.Null(Assert.Single(events.OfType<SearchConflictResolvedEvent>()).LearnedTacticId);
            Assert.Equal(0, world.Officers[2].LifespanTier); Assert.Contains(new PersistentEventKey("scholar.visited"), world.Forces[1].EventHistory);
        }
    }

    [Fact]
    public void MissingFundsCannotPayFromRichRootOrPartiallyCommit()
    {
        var (world, task, rules, encounter) = Create("demand");
        world.Replace(world.Corps[7] with { Treasury = new(100, 0) }); world.Replace(world.Forces[1] with { Money = 10000 });
        List<DomainEvent> events = [];
        Assert.Throws<InvalidOperationException>(() => SearchConflicts.Resolve(world, task, rules, encounter, SearchConflictChoice.Pay, events));
        Assert.Empty(events); Assert.Equal(10000u, world.Forces[1].Money); Assert.Equal(100, world.Forces[1].BaseTrust);
    }
}
