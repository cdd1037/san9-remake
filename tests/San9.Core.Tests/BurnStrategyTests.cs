using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class BurnStrategyTests
{
    [Theory]
    [InlineData(52UL, true)] // SplitMix64 percentile 2.
    [InlineData(56UL, false)] // SplitMix64 percentile 3.
    public void FoodGateHasStrictThreePercentBoundaryWithoutChangingMainDamage(ulong state, bool hits)
    {
        var rules = Rules(30);
        var world = World();
        var task = Submit(world, rules, 0);
        var mutation = world.BeginMutation(rules.Abilities);
        mutation.SetSimulation(mutation.Simulation with { RandomStreams = [new("strategy-burn-food-gate", state)] });
        List<DomainEvent> events = [];
        BurnStrategies.Resolve(mutation, task, rules, events);
        var result = Assert.Single(events.OfType<BurnResolvedEvent>());
        Assert.True(result.Success);
        Assert.True(result.DurabilityLoss > 0);
        Assert.Equal(hits, result.FoodLoss > 0);
        Assert.Equal(10000u - result.FoodLoss, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Food);
    }

    private static SimulationRules Rules(int distance = 31) => new()
    {
        Personnel = new([new(1, new(2, 2)), new(2, new(2 + distance, 2)), new(3, new(900, 2))],
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }, [new(1, 2), new(2, 3)]),
        Military = new(new(200, 3, Enumerable.Repeat(0, 600).ToImmutableArray(), [new(0, 1, null)],
            [new(0, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 2), new(1, 1)), new(2, new(2 + distance, 2), new(1, 1)), new(3, new(900, 2), new(1, 1))]))
    };

    private static WorldState World(int injury = 0, ulong seed = 42) => WorldState.Create(new(190, 1, 1), seed,
        [new(1, 100), new(2, 100)],
        [new(1, 1, 10000, 300, 300, 70, 500, 0, 100, 0, 100),
         new(2, 2, 30000, 300, 300, 70, 500, 0, 100, 0, 100) { Wounded = 4000 },
         new(3, 2, 10000, 300, 300, 70, 500, 0, 100, 0, 100)],
        [new(11, 1, 1, new(100, 70, 70, 70), false) { Injury = injury },
         new(12, 1, 1, new(70, 70, 70, 70), false), new(22, 2, 2, new(70, 70, 70, 50), false)],
        relations: [new(1, 2, 50)], controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(10000, 10000)) { IsRoot = true }]);

    private static PersistentTask Submit(WorldState world, SimulationRules rules, int roll)
    {
        var engine = new SimulationEngine(world, rules);
        var command = new BurnCommand(Guid.NewGuid(), 1, world.Simulation.NextSequence, 11, 2);
        string before = engine.Snapshot.StateHash;
        var balances = engine.Snapshot.Forces;
        Assert.True(engine.Validate(new("player"), command).Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(balances, engine.Snapshot.Forces);
        var mutation = world.BeginMutation();
        var task = Assert.Single(mutation.Simulation.Tasks);
        task = task with { Journey = task.Journey! with { SuccessRoll = roll } };
        mutation.SetSimulation(mutation.Simulation with { Tasks = [task] });
        world.Commit(mutation);
        return task;
    }

    private static List<DomainEvent> Day(WorldMutation world, SimulationRules rules)
    {
        List<DomainEvent> events = [];
        new ResolutionPipeline(rules).AdvanceDay(world, events);
        return events;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(30, 1)]
    [InlineData(31, 2)]
    public void SubmissionReservesOnlyActorAndResolutionUsesDailyArrival(int distance, int arrival)
    {
        var rules = Rules(distance); var world = World();
        Submit(world, rules, 0);
        var mutation = world.BeginMutation(rules.Abilities);
        Assert.Equal(300, mutation.Settlements[2].Durability);
        Assert.False(mutation.Officers[12].Acted);
        for (int day = 1; day < arrival; day++) Assert.Empty(Day(mutation, rules).OfType<BurnResolvedEvent>());
        var events = Day(mutation, rules);
        var result = Assert.Single(events.OfType<BurnResolvedEvent>());
        Assert.True(result.Success);
        Assert.InRange(result.DurabilityLoss, 17, 21);
        Assert.Equal(300 - result.DurabilityLoss, mutation.Settlements[2].Durability);
        Assert.All(mutation.Officers[12].TacticProficiencies, value => Assert.Equal(0, value));
        Assert.Equal(2, mutation.Officers[11].TacticProficiencies[7]);
        Assert.Equal(2, mutation.Officers[11].TacticProficiencies[8]);
        Assert.Contains(mutation.Simulation.Tasks, t => t.Kind == PersonnelReturns.Kind);
    }

    [Theory]
    [InlineData(46, true, 0)]
    [InlineData(47, false, 0)]
    [InlineData(57, false, 1)]
    [InlineData(99, false, 5)]
    public void ThresholdIsStrictAndFailureInjuresWithoutDamagingTarget(int roll, bool success, int injury)
    {
        var rules = Rules(30); var world = World(); Submit(world, rules, roll);
        var mutation = world.BeginMutation(rules.Abilities);
        uint before = mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Food;
        var events = Day(mutation, rules);
        var result = Assert.Single(events.OfType<BurnResolvedEvent>());
        Assert.Equal(47, result.Threshold);
        Assert.Equal(success, result.Success);
        Assert.Equal(injury, result.InjuryAfter);
        long change = events.OfType<ResourceChangedEvent>().Where(e => e.Account == new ResourceAccount(ResourceOwnerKind.Corps, 2) &&
            e.Resource == ResourceKind.Food).Sum(e => e.Actual);
        Assert.Equal((long)before + change, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Food);
        Assert.Equal(-(long)result.FoodLoss, events.OfType<ResourceChangedEvent>().Where(e => e.Reason == BurnStrategies.Kind).Sum(e => e.Actual));
        if (!success)
        {
            Assert.Equal(300, mutation.Settlements[2].Durability);
            Assert.InRange(mutation.Relations[(1, 2)].Value, 47, 49);
            Assert.Equal(100, mutation.Officers[11].BaseAbilities.War);
        }
    }

    [Fact]
    public void FailureDoesNotHealSevereExistingInjury()
    {
        var rules = Rules(30); var world = World(10); Submit(world, rules, 99);
        var result = Assert.Single(Day(world.BeginMutation(rules.Abilities), rules).OfType<BurnResolvedEvent>());
        Assert.Equal(9, result.InjuryBefore); // The opening xun already recovered one point.
        Assert.Equal(result.InjuryBefore, result.InjuryAfter);
    }

    [Fact]
    public void ExecutionRecomputesCommanderAndDurabilityThreshold()
    {
        var rules = Rules(30); var world = World(); Submit(world, rules, 46);
        var mutation = world.BeginMutation(rules.Abilities);
        mutation.Replace(mutation.Settlements[2] with { Durability = 100 });
        var result = Assert.Single(Day(mutation, rules).OfType<BurnResolvedEvent>());
        Assert.Equal(0, result.Threshold);
        Assert.False(result.Success);
        Assert.Equal(100, mutation.Settlements[2].Durability);
    }

    [Fact]
    public void GeographyUsesDirectedImmediateCityNeighborsAndAdviceIsReadOnly()
    {
        var world = World(); var rules = Rules(); var engine = new SimulationEngine(world, rules);
        string before = engine.Snapshot.StateHash;
        Assert.Equal(AdviserOpinion.NoAdviser, engine.GetBurnAdvice(1, 11, 2).Opinion);
        Assert.Equal(CommandRejection.InvalidTask, engine.GetBurnAdvice(1, 11, 3).Rejection);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.False(BurnStrategies.Eligible(world.BeginMutation(), 2, 2, 1, rules));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewFriendlyOwnershipOrBarbarianIdentityCancelsWithoutEffect(bool barbarian)
    {
        var rules = Rules(); var world = World(); Submit(world, rules, 0);
        var mutation = world.BeginMutation(rules.Abilities);
        if (barbarian) mutation.Replace(mutation.Forces[2] with { IsBarbarian = true });
        else
        {
            mutation.Replace(mutation.Settlements[2] with { OwnerId = 1 });
            mutation.Replace(mutation.Officers[22] with { OwnerId = 1 });
        }
        var events = Day(mutation, rules);
        Assert.Empty(events.OfType<BurnResolvedEvent>());
        Assert.Single(events.OfType<TaskCancelledEvent>());
        Assert.Equal(300, mutation.Settlements[2].Durability);
        Assert.DoesNotContain(mutation.Simulation.Tasks, t => t.Kind == BurnStrategies.Kind);
    }

    [Fact]
    public void WoundedParticipateInFoodLossAndArithmeticRemainsBounded()
    {
        Assert.Equal(1749u, StrategyMath.BurnFoodLoss(100, 30000, 4000, 10000, 49));
        Assert.Equal(100u, StrategyMath.BurnFoodLoss(100, 30000, 4000, 100, 49));
        Assert.Equal(uint.MaxValue, StrategyMath.BurnFoodLoss(int.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, 99));
    }

    [Fact]
    public void FoodGateIsIndependentAndUsesTargetAccountWithoutTransferringSoldiers()
    {
        var rules = Rules(30);
        int hits = 0, misses = 0;
        for (ulong seed = 1; seed <= 150; seed++)
        {
            var world = World(seed: seed); var task = Submit(world, rules, 0);
            var mutation = world.BeginMutation(rules.Abilities);
            List<DomainEvent> events = [];
            BurnStrategies.Resolve(mutation, task, rules, events);
            var result = Assert.Single(events.OfType<BurnResolvedEvent>());
            Assert.True(result.Success);
            Assert.Equal(30000u, mutation.Settlements[2].Soldiers);
            Assert.Equal(4000u, mutation.Settlements[2].Wounded);
            if (result.FoodLoss == 0) misses++;
            else
            {
                hits++;
                Assert.InRange(result.FoodLoss, 1700u, 1799u);
                Assert.Equal(10000u - result.FoodLoss, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Food);
                Assert.Equal(10000u, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Food);
            }
        }
        Assert.True(hits > 0);
        Assert.True(misses > 0);
    }

    [Fact]
    public void ConflictingPayloadAndMissingRequiredBurnFieldAreRejected()
    {
        var rules = Rules(); var world = World(); var task = Submit(world, rules, 0);
        Assert.False(new BurnRule(rules).HasValidPayload(task with
            { Journey = task.Journey! with { UnitStrategy = new(UnitStrategyKind.Inspire, 1) } }));
        string json = System.Text.Json.JsonSerializer.Serialize(task.Journey);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        Assert.True(node.Remove("Burn"));
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<PersonnelJourney>(node.ToJsonString()));
    }
}
