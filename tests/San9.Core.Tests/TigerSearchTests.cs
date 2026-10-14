using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class TigerSearchTests
{
    private static TigerSearchEventDefinition Definition => new([800], 20, 260, 5, 20, 1, 1, 2, 7)
        { Id = "tiger", Priority = 1, Consumption = EventConsumption.None };
    private static SimulationRules Rules(params SearchEventDefinition[] definitions) => new()
    {
        Personnel = new([new(1, new(0, 0)), new(2, new(0, 0)), new(3, new(0, 0))],
            new Dictionary<int, int> { [2] = 1 }, cityProvinces: new Dictionary<int, int> { [1] = 800, [3] = 900 },
            searchEvents: definitions.Length == 0 ? [Definition] : definitions)
    };
    private static ulong Seed(int roll) => TestSeeds.StateForRoll(roll);
    private static (WorldMutation World, PersistentTask Task) Create(int eligibilityRoll = 0, int battleRoll = 0)
    {
        var world = WorldState.Create(new(190, 2, 1), 123,
            [new(1, 100) { LeaderId = 1, BaseTrust = 100 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(99, 0, 0, 0), false) { LifespanTier = 3 }],
            controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }]).BeginMutation();
        world.SetSimulation(world.Simulation with { RandomStreams =
            [new("event-search-eligibility", Seed(eligibilityRoll)), new("event-tiger-battle", Seed(battleRoll))] });
        return (world, new(Guid.NewGuid(), "personnel-search", "player", 1, 2, 1, world.Clock, world.Clock, 0, 0)
            { Journey = new(1, null, 0) });
    }

    [Theory]
    [InlineData("city", true, true)]
    [InlineData("region", true, true)]
    [InlineData("foreign", true, true)]
    [InlineData("wrong-province", false, false)]
    [InlineData("unmapped", false, false)]
    [InlineData("automatic", false, false)]
    [InlineData("eliminated", false, false)]
    [InlineData("chance", false, true)]
    [InlineData("trust259", true, true)]
    [InlineData("trust260", false, true)]
    [InlineData("injury4", true, true)]
    [InlineData("injury5", false, true)]
    public void QualificationResolvesProvinceBeforeRandomAndTrustAndInjuryAfterIt(string variant, bool expected, bool draws)
    {
        var (world, task) = Create(variant == "chance" ? 4 : 0);
        switch (variant)
        {
            case "region": task = task with { Journey = new(2, null, 0) }; break;
            case "foreign": world.Replace(world.Settlements[1] with { OwnerId = null }); break;
            case "wrong-province": task = task with { Journey = new(3, null, 0) }; break;
            case "unmapped": task = task with { Journey = new(999, null, 0) }; break;
            case "automatic": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
            case "eliminated": world.Replace(world.Forces[1] with { IsEliminated = true }); break;
            case "trust259": world.Replace(world.Forces[1] with { BaseTrust = 259 }); break;
            case "trust260": world.Replace(world.Forces[1] with { BaseTrust = 260 }); break;
            case "injury4": world.Replace(world.Officers[2] with { Injury = 4 }); break;
            case "injury5": world.Replace(world.Officers[2] with { Injury = 5 }); break;
        }
        var random = new StableRandom(world.Simulation.RandomStreams.Single(s => s.Name == "event-search-eligibility").State);
        if (draws) random.NextUInt64();
        Assert.Equal(expected, TigerSearch.Prepare(world, task, Rules().Personnel, Definition));
        Assert.Equal(random.State, world.Simulation.RandomStreams.Single(s => s.Name == "event-search-eligibility").State);
    }

    [Theory]
    [InlineData(0, 19, true)]
    [InlineData(0, 20, false)]
    [InlineData(19, 19, true)]
    [InlineData(20, 20, false)]
    [InlineData(99, 98, true)]
    [InlineData(99, 99, false)]
    [InlineData(100, 99, true)]
    [InlineData(120, 99, true)]
    public void FightNeedsNoTacticUsesStrictThresholdAndNeverReducesLifespan(int war, int roll, bool wins)
    {
        var (world, task) = Create(battleRoll: roll);
        world.Replace(world.Officers[2] with { BaseAbilities = new(war, 0, 0, 0) });
        List<DomainEvent> events = [];
        TigerSearch.Resolve(world, task, Rules(), new("tiger"), true, events);
        var result = Assert.Single(events.OfType<TigerSearchResolvedEvent>());
        Assert.Equal(wins ? EventResult.Succeeded : EventResult.Defeated, result.Result);
        Assert.Equal(Math.Max(20, war), result.BattleChance); Assert.Equal(roll, result.Roll);
        Assert.Equal(wins ? (war >= 100 ? war : war + 1) : war, result.War);
        Assert.Equal(wins ? 101 : 98, result.BaseTrust); Assert.Equal(wins ? 0 : 7, result.Injury);
        Assert.Equal(3, result.LifespanTier); Assert.Empty(world.Simulation.ConsumedEventKeys);
        Assert.Empty(world.Officers[2].LearnedTactics);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(259)]
    public void RefusalHasSaturatingTrustPenaltyWithoutBattleInjuryOrConsumption(int trust)
    {
        var (world, task) = Create(); world.Replace(world.Forces[1] with { BaseTrust = trust });
        var random = world.Simulation.RandomStreams.ToArray(); List<DomainEvent> events = [];
        TigerSearch.Resolve(world, task, Rules(), new("tiger"), false, events);
        var e = Assert.Single(events.OfType<TigerSearchResolvedEvent>());
        Assert.Equal(EventResult.Declined, e.Result); Assert.False(e.Accepted); Assert.Null(e.Roll);
        Assert.Equal(Math.Max(0, trust - 2), e.BaseTrust); Assert.Equal(0, e.Injury); Assert.Equal(3, e.LifespanTier);
        Assert.Equal(random, world.Simulation.RandomStreams.ToArray()); Assert.Empty(world.Simulation.ConsumedEventKeys);
        Assert.True(TigerSearch.CanResolve(world, task, Rules(), new("tiger")));
    }

    [Fact]
    public void EligibilityUsesInjuredEffectiveWarInsteadOfBaseWarOrOtherAbilities()
    {
        var (world, task) = Create(3);
        world.Replace(world.Officers[2] with { Injury = 4, BaseAbilities = new(99, 500, 500, 500) });
        Assert.True(world.Abilities(world.Officers[2]).War / 20 < 3);
        Assert.False(TigerSearch.Prepare(world, task, Rules().Personnel, Definition));
    }

    [Fact]
    public void InvalidatedTrustGateRejectsWithoutPartialWritesOrRandomDraw()
    {
        var (world, task) = Create(); Assert.True(TigerSearch.Prepare(world, task, Rules().Personnel, Definition));
        world.Replace(world.Forces[1] with { BaseTrust = 260 });
        var person = world.Officers[2]; var random = world.Simulation.RandomStreams.ToArray(); List<DomainEvent> events = [];
        Assert.Throws<InvalidOperationException>(() => TigerSearch.Resolve(world, task, Rules(), new("tiger"), true, events));
        Assert.Empty(events); Assert.Equal(person, world.Officers[2]); Assert.Equal(random, world.Simulation.RandomStreams.ToArray());
    }

    [Fact]
    public void InjuryAndTreasureBonusesAffectTigerAndBeastBattleChancesButTreasureTacticsDoNotQualifyForBeast()
    {
        var book = new TreasureRulebook([new(50, new(20, 0, 0, 0), 0) { TacticId = 1 }]);
        var state = WorldState.Create(new(190, 2, 1), 1, [new(1, 100)], [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(2, 1, 1, new(60, 0, 0, 0), false) { Injury = 3 }],
            treasures: [new(50, 2, TreasureDiscovery.Discovered, null)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(100, 100)) { IsRoot = true }]);
        var world = state.BeginMutation(new(book, new([])));
        int effective = world.Abilities(world.Officers[2]).War;
        Assert.NotEqual(60, effective); Assert.Equal(Math.Max(20, effective), TigerSearch.BattleChance(world, 2, Definition));
        Assert.Equal(0, SearchEvents.BattleChance(world, 2, BeastSearchTests.Definition));
        world.Replace(world.Officers[2] with { LearnedTactics = [1] });
        Assert.Equal(Math.Max(10, effective / 5), SearchEvents.BattleChance(world, 2, BeastSearchTests.Definition));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TigerPrecedesBeastAndOnlyFailedEligibilityContinues(bool tigerQualifies)
    {
        var (world, task) = Create(); var beast = BeastSearchTests.Definition with { WarChanceDivisor = 1, Sites = [BeastSearchTests.Definition.Sites[0]] };
        if (!tigerQualifies) world.Replace(world.Forces[1] with { BaseTrust = 260 });
        var rules = Rules(beast, Definition); List<DomainEvent> events = [];
        Assert.True(SearchEvents.Prepare(world, task, rules, events, out var encounter));
        if (tigerQualifies)
        {
            var tiger = Assert.IsType<TigerSearchEncounter>(encounter);
            TigerSearch.Resolve(world, task, rules, tiger, false, events);
            Assert.DoesNotContain(events, e => e is BeastSearchResolvedEvent or SearchResolvedEvent);
            Assert.Empty(world.Simulation.ConsumedEventKeys);
        }
        else
        {
            var selected = Assert.IsType<BeastSearchEncounter>(encounter);
            SearchEvents.ResolveBeast(world, task, rules.Personnel, rules.Economy, selected, false, events);
            Assert.Equal(new PersistentEventKey("beast.home"), Assert.Single(world.Simulation.ConsumedEventKeys));
        }
        var random = new StableRandom(Seed(0)); random.NextUInt64(); if (!tigerQualifies) random.NextUInt64();
        Assert.Equal(random.State, world.Simulation.RandomStreams.Single(s => s.Name == "event-search-eligibility").State);
    }
}
