using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class GossipStrategyTests
{
    [Theory]
    [InlineData(156UL, true)] // SplitMix64 percentile 49.
    [InlineData(72UL, false)] // SplitMix64 percentile 50.
    public void LoyaltyGateIsStrictAndCannotUndoTheSuccessfulRelationEffect(ulong state, bool hits)
    {
        var rules = Rules();
        var world = World();
        var task = Submit(world, rules, 0);
        var mutation = world.BeginMutation(rules.Abilities);
        mutation.Replace(mutation.Officers[23] with { BaseAbilities = new(70, 50, 70, 70) });
        mutation.SetSimulation(mutation.Simulation with { RandomStreams = [new("strategy-gossip-loyalty-gate", state)] });
        List<DomainEvent> events = [];
        GossipStrategies.Resolve(mutation, task, rules.Personnel, events);
        var result = Assert.Single(events.OfType<GossipResolvedEvent>());
        Assert.True(result.Success);
        Assert.Equal(10, result.RelationLoss);
        Assert.Equal(80, mutation.Relations[(2, 3)].Value);
        Assert.Equal(hits, result.LoyaltyGatePassed);
        Assert.Equal(hits, result.LoyaltyLoss > 0);
    }

    private static SimulationRules Rules() => new()
    {
        Personnel = new([new(1, new(2, 2)), new(2, new(32, 2)), new(3, new(62, 2))],
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 },
            [new(1, 2), new(2, 3), new(1, 3)])
    };

    private static WorldState World(ulong seed = 42, bool bothTargetsPlayer = false,
        bool firstOnlyRuler = false, bool firstBarbarian = false)
    {
        var forces = new[]
        {
            new ForceState(1, 100) { LeaderId = 11 },
            new ForceState(2, 100) { LeaderId = 21,
                AdviserId = firstOnlyRuler ? null : 22, IsBarbarian = firstBarbarian },
            new ForceState(3, 100) { LeaderId = 31 }
        };
        List<OfficerState> officers =
        [
            new(11, 1, 1, new(70, 100, 70, 70), false) { Status = OfficerStatus.Ruler },
            new(21, 2, 2, new(70, 40, 70, 70), false) { Status = OfficerStatus.Ruler },
            new(31, 3, 3, new(70, 60, 70, 70), false) { Status = OfficerStatus.Ruler }
        ];
        if (!firstOnlyRuler)
        {
            officers.Add(new OfficerState(22, 2, 2, new(70, 20, 70, 70), false)
                { Status = OfficerStatus.Strategist }.WithLoyalty(2, 80));
            officers.Add(new OfficerState(23, 2, 2, new(70, 0, 70, 70), false).WithLoyalty(2, 20));
        }
        var controls = new[]
        {
            new ControlSeat("player", 1, ControllerKind.Player),
            new ControlSeat("target-1", 2, bothTargetsPlayer ? ControllerKind.Player : ControllerKind.Automatic),
            new ControlSeat("target-2", 3, bothTargetsPlayer ? ControllerKind.Player : ControllerKind.Automatic)
        };
        return WorldState.Create(new(190, 1, 1), seed, forces,
            [new(1, 1, 10000, 300, 300, 70, 500, 0, 100, 0, 100),
             new(2, 2, 10000, 300, 300, 70, 500, 0, 100, 0, 100),
             new(3, 3, 10000, 300, 300, 70, 500, 0, 100, 0, 100)],
            officers, relations: [new(1, 2, 50), new(1, 3, 50), new(2, 3, 90)], controls: controls, corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 31, new(10000, 10000)) { IsRoot = true }]);
    }

    private static PersistentTask Submit(WorldState world, SimulationRules rules, int? roll)
    {
        var engine = new SimulationEngine(world, rules);
        var command = new GossipCommand(Guid.NewGuid(), 1, world.Simulation.NextSequence, 11, 2, 3);
        string before = engine.Snapshot.StateHash;
        Assert.True(engine.Validate(new("player"), command).Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        var task = Assert.Single(world.Simulation.Tasks);
        if (roll is int fixedRoll)
        {
            var mutation = world.BeginMutation();
            task = task with { Journey = task.Journey! with { SuccessRoll = fixedRoll } };
            mutation.SetSimulation(mutation.Simulation with { Tasks = [task] });
            world.Commit(mutation);
        }
        return task;
    }

    private static (WorldMutation State, GossipResolvedEvent Result, List<DomainEvent> Events) Resolve(
        int roll, ulong seed = 42)
    {
        var rules = Rules();
        var world = World(seed);
        var task = Submit(world, rules, roll);
        var state = world.BeginMutation(rules.Abilities);
        List<DomainEvent> events = [];
        GossipStrategies.Resolve(state, task, rules.Personnel, events);
        return (state, Assert.Single(events.OfType<GossipResolvedEvent>()), events);
    }

    [Fact]
    public void SuccessDamagesTargetRelationsAndLowestLoyaltyNonRuler()
    {
        var (state, result, events) = Resolve(0);

        Assert.True(result.Success);
        Assert.Equal(40, result.Threshold);
        Assert.Equal(10, result.RelationLoss);
        Assert.Equal(80, state.Relations[(2, 3)].Value);
        Assert.Equal(50, state.Relations[(1, 2)].Value);
        Assert.Equal(23, result.TargetOfficerId);
        Assert.True(result.LoyaltyGatePassed);
        Assert.InRange(result.LoyaltyLoss, 3, 4);
        Assert.Equal(20 - result.LoyaltyLoss, state.Officers[23].Loyalty);
        Assert.Equal(80, state.Officers[22].Loyalty);
        Assert.Single(events.OfType<OfficerLoyaltyChangedEvent>());
    }

    [Fact]
    public void FailureOnlyDamagesActorAndFirstTargetRelations()
    {
        var (state, result, events) = Resolve(99);

        Assert.False(result.Success);
        Assert.InRange(result.RelationLoss, 1, 3);
        Assert.Equal(50 - result.RelationLoss, state.Relations[(1, 2)].Value);
        Assert.Equal(90, state.Relations[(2, 3)].Value);
        Assert.Equal(20, state.Officers[23].Loyalty);
        Assert.Empty(events.OfType<OfficerLoyaltyChangedEvent>());
    }

    [Fact]
    public void ThresholdUsesFirstTargetLeadershipAndBothPlayerGate()
    {
        var rules = Rules();
        var world = World();
        _ = new SimulationEngine(world, rules);
        Assert.Equal(40, GossipStrategies.Threshold(world.BeginMutation(rules.Abilities), 11, 2, 3));

        world = World(bothTargetsPlayer: true);
        _ = new SimulationEngine(world, rules);
        Assert.Equal(0, GossipStrategies.Threshold(world.BeginMutation(rules.Abilities), 11, 2, 3));
    }

    [Fact]
    public void AdviceIsReadOnlyAndSubmissionStoresBothTargetsWithoutCost()
    {
        var rules = Rules();
        var world = World();
        var engine = new SimulationEngine(world, rules);
        string before = engine.Snapshot.StateHash;
        uint money = engine.Snapshot.Corps.Single(force => force.IsRoot && force.OwnerId == 1).Treasury.Money;

        Assert.Equal(AdviserOpinion.NoAdviser, engine.GetGossipAdvice(1, 11, 2, 3).Opinion);
        Assert.Equal(before, engine.Snapshot.StateHash);
        var task = Submit(world, rules, null);
        Assert.Equal(new GossipMission(2, 3), task.Journey!.Gossip);
        Assert.Equal(ConditionalRandom.Percentile(42, ConditionalRandomPurpose.Gossip,
            new(190, 1, 1), 11, 2, 3), task.Journey.SuccessRoll);
        Assert.Equal(money, engine.Snapshot.Corps.Single(force => force.IsRoot && force.OwnerId == 1).Treasury.Money);
    }

    [Fact]
    public void InvalidPairsAndFirstTargetWithoutAnotherPersonAreRejected()
    {
        var rules = Rules();
        var engine = new SimulationEngine(World(firstOnlyRuler: true), rules);
        Assert.Equal(CommandRejection.InvalidTask, engine.GetGossipAdvice(1, 11, 2, 3).Rejection);

        engine = new SimulationEngine(World(firstBarbarian: true), rules);
        Assert.Equal(CommandRejection.InvalidTask, engine.GetGossipAdvice(1, 11, 2, 3).Rejection);

        engine = new SimulationEngine(World(), rules);
        Assert.Equal(CommandRejection.InvalidTask, engine.GetGossipAdvice(1, 11, 2, 2).Rejection);
        Assert.Equal(CommandRejection.InvalidTask, engine.GetGossipAdvice(1, 11, 1, 3).Rejection);
    }

    [Fact]
    public void LiveTargetInvalidationCancelsMissionAndStartsReturn()
    {
        var rules = Rules();
        var world = World();
        Submit(world, rules, 0);
        var mutation = world.BeginMutation(rules.Abilities);
        mutation.Replace(mutation.Forces[2] with { IsBarbarian = true });
        List<DomainEvent> events = [];

        new ResolutionPipeline(rules).AdvanceDay(mutation, events);

        Assert.Single(events.OfType<TaskCancelledEvent>());
        Assert.Empty(events.OfType<GossipResolvedEvent>());
        Assert.Contains(mutation.Simulation.Tasks, task => task.Kind == PersonnelReturns.Kind);
    }

    [Fact]
    public void PayloadRequiresGossipAndRejectsConflictingStrategyData()
    {
        var rules = Rules();
        var world = World();
        var task = Submit(world, rules, 0);
        var rule = new GossipRule(rules);
        Assert.False(rule.HasValidPayload(task with { Journey = task.Journey! with { Seize = new(2) } }));
        string json = System.Text.Json.JsonSerializer.Serialize(task.Journey);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        Assert.True(node.Remove("Gossip"));
        Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<PersonnelJourney>(node.ToJsonString()));
    }

    [Fact]
    public void ArithmeticMatchesVerifiedThresholdAndRelationBoundary()
    {
        Assert.Equal(40, StrategyMath.GossipThreshold(100, 40, 20));
        Assert.Equal(20, StrategyMath.GossipThreshold(70, 40, null));
        Assert.Equal(0, StrategyMath.GossipThreshold(100, 40, 20, true));
        Assert.Equal(20, StrategyMath.GossipRelationLoss(70));
        Assert.Equal(10, StrategyMath.GossipRelationLoss(90));
        Assert.Equal(0, StrategyMath.GossipRelationLoss(100));
    }
}
