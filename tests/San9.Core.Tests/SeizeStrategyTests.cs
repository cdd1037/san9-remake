using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SeizeStrategyTests
{
    [Theory]
    [InlineData(156UL, SeizeOutcomeKind.Item)] // SplitMix64 percentile 49.
    [InlineData(72UL, SeizeOutcomeKind.Person)] // SplitMix64 percentile 50.
    public void HalfChanceItemGateSelectsExactlyOneBranchWithSameMainDraw(ulong state, SeizeOutcomeKind outcome)
    {
        var rules = Rules();
        var world = World();
        var task = Submit(world, rules, 0);
        var mutation = world.BeginMutation(rules.Abilities);
        mutation.SetSimulation(mutation.Simulation with { RandomStreams = [new("strategy-seize-item-gate", state)] });
        List<DomainEvent> events = [];
        SeizeStrategies.Resolve(mutation, task, rules, events);
        var result = Assert.Single(events.OfType<SeizeResolvedEvent>());
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(0u, result.Money);
        Assert.Equal(outcome == SeizeOutcomeKind.Item, result.TreasureId is not null);
        Assert.Equal(outcome == SeizeOutcomeKind.Person, result.RecoveredOfficerId is not null);
        Assert.Equal(outcome == SeizeOutcomeKind.Item ? 23 : (int?)null, result.PreviousHolderId);
        if (outcome == SeizeOutcomeKind.Item) Assert.Equal(11, mutation.Treasures[result.TreasureId!.Value].HolderId);
    }

    private static SimulationRules Rules(int distance = 30) => new()
    {
        Personnel = new([new(1, new(2, 2)), new(2, new(2 + distance, 2)), new(3, new(900, 2))],
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }, [new(1, 2), new(2, 3)]),
        Military = new(new(200, 3, Enumerable.Repeat(0, 600).ToImmutableArray(), [new(0, 1, null)],
            [new(0, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 2), new(1, 1)), new(2, new(2 + distance, 2), new(1, 1)),
             new(3, new(900, 2), new(1, 1))])),
        Treasures = new([new(10, default, 5), new(11, default, 1)])
    };

    private static WorldState World(ulong seed = 42, int actorWar = 100, bool captive = true,
        bool treasures = true, uint actorMoney = 10000) => WorldState.Create(new(190, 1, 1), seed,
        [new ForceState(1, 100) { LeaderId = 11 },
         new ForceState(2, 100) { LeaderId = 22 }],
        [new(1, 1, 10000, 300, 300, 70, 500, 0, 100, 0, 100),
         new(2, 2, 30000, 300, 300, 70, 500, 0, 100, 0, 100) { Wounded = 4000 },
         new(3, 2, 10000, 300, 300, 70, 500, 0, 100, 0, 100)],
        [new OfficerState(11, 1, 1, new(actorWar, 70, 70, 70), false),
         new OfficerState(22, 2, 2, new(20, 20, 20, 20), false) { Status = OfficerStatus.Ruler, IsCommander = true },
         new OfficerState(23, 2, 2, new(10, 10, 10, 10), false),
         .. (captive ? new OfficerState[] { new(31, 1, 2, new(70, 70, 70, 70), false)
            { Status = OfficerStatus.Captive } } : [])],
        treasures: treasures ? [new(10, 22, TreasureDiscovery.Discovered, null),
            new(11, 23, TreasureDiscovery.Discovered, null)] : [],
        relations: [new(1, 2, 50)], controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(actorMoney, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 22, new(10000, 10000)) { IsRoot = true }]);

    private static PersistentTask Submit(WorldState world, SimulationRules rules, int roll)
    {
        var engine = new SimulationEngine(world, rules);
        var command = new SeizeCommand(Guid.NewGuid(), 1, world.Simulation.NextSequence, 11, 2);
        var before = engine.Snapshot;
        uint targetMoney = engine.Snapshot.Corps.Single(force => force.IsRoot && force.OwnerId == 2).Treasury.Money;
        Assert.True(engine.Validate(new("player"), command).Accepted);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(targetMoney, engine.Snapshot.Corps.Single(force => force.IsRoot && force.OwnerId == 2).Treasury.Money);
        var mutation = world.BeginMutation();
        var task = Assert.Single(mutation.Simulation.Tasks);
        task = task with { Journey = task.Journey! with { SuccessRoll = roll } };
        mutation.SetSimulation(mutation.Simulation with { Tasks = [task] });
        world.Commit(mutation);
        return task;
    }

    private static (WorldMutation State, SeizeResolvedEvent Result, List<DomainEvent> Events) Resolve(
        ulong seed, int roll, bool captive = true, bool treasures = true, int actorWar = 100,
        uint actorMoney = 10000)
    {
        var rules = Rules();
        var world = World(seed, actorWar, captive, treasures, actorMoney);
        var task = Submit(world, rules, roll);
        var state = world.BeginMutation(rules.Abilities);
        List<DomainEvent> events = [];
        SeizeStrategies.Resolve(state, task, rules, events);
        return (state, Assert.Single(events.OfType<SeizeResolvedEvent>()), events);
    }

    private static (WorldMutation State, SeizeResolvedEvent Result, List<DomainEvent> Events) Find(
        Func<SeizeResolvedEvent, bool> predicate, int roll, bool captive = true, bool treasures = true,
        int actorWar = 100, uint actorMoney = 10000)
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var result = Resolve(seed, roll, captive, treasures, actorWar, actorMoney);
            if (predicate(result.Result)) return result;
        }
        throw new InvalidOperationException("No deterministic test seed matched the requested seize branch.");
    }

    [Fact]
    public void ItemBranchUsesIndependentGateLowestLoyaltyItemAndFirstPriority()
    {
        var (state, result, events) = Find(r => r.ItemGatePassed, 0);

        Assert.Equal(SeizeOutcomeKind.Item, result.Outcome);
        Assert.Equal(11, result.TreasureId);
        Assert.Equal(88, result.ItemThreshold);
        Assert.Equal(6, result.PersonThreshold);
        Assert.Equal(25, result.MoneyThreshold);
        Assert.Equal(11, state.Treasures[11].HolderId);
        Assert.Equal(22, state.Treasures[10].HolderId);
        Assert.Equal(9999u, state.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Money);
        Assert.Single(events.OfType<TreasureTransferredEvent>());
    }

    [Fact]
    public void PersonBranchRecoversOneOwnPrisonerWhenItemGateMisses()
    {
        var (state, result, events) = Find(r => !r.ItemGatePassed, 0);

        Assert.Equal(SeizeOutcomeKind.Person, result.Outcome);
        Assert.Equal(31, result.RecoveredOfficerId);
        Assert.Equal(OfficerStatus.Serving, state.Officers[31].Status);
        Assert.Equal(1, state.Officers[31].SettlementId);
        Assert.Single(events.OfType<PrisonerRecoveredEvent>());
        Assert.Contains(state.Simulation.Tasks, task => task.OfficerId == 31 && task.Kind == PersonnelReturns.Kind);
        Assert.Equal(9999u, state.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Money);
    }

    [Fact]
    public void MoneyBranchTransfersBoundedAmountWithoutChangingTroopsOrWounded()
    {
        var (state, result, events) = Find(r => !r.ItemGatePassed, 10, captive: false);

        Assert.Equal(SeizeOutcomeKind.Money, result.Outcome);
        Assert.InRange(result.Money, 435u, 484u);
        Assert.Equal(9999u + result.Money, state.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Money);
        Assert.Equal(9999u - result.Money, state.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Money);
        Assert.Equal(30000u, state.Settlements[2].Soldiers);
        Assert.Equal(4000u, state.Settlements[2].Wounded);
        Assert.Equal(0, events.OfType<ResourceChangedEvent>().Sum(e => e.Actual));

        var capped = Find(r => !r.ItemGatePassed, 10, captive: false, actorMoney: TreasuryRules.Capacity);
        Assert.Equal(1u, capped.Result.Money);
        Assert.Equal(TreasuryRules.Capacity, capped.State.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Money);
        Assert.Equal(9998u, capped.State.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Money);
    }

    [Fact]
    public void TotalFailureUsesMoneyThresholdForInjuryAndOnlyThenChangesRelations()
    {
        var (state, result, _) = Resolve(42, 99, actorWar: 50);

        Assert.Equal(SeizeOutcomeKind.Failure, result.Outcome);
        Assert.Equal(-3, result.MoneyThreshold);
        Assert.Equal(7, result.InjuryAfter);
        Assert.InRange(state.Relations[(1, 2)].Value, 47, 49);
        Assert.Equal(50, state.Officers[11].BaseAbilities.War);
        Assert.Equal(9999u, state.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Money);
    }

    [Fact]
    public void AdviceIsStableAndReadOnlyWhileSubmissionOnlyReservesActor()
    {
        var rules = Rules();
        var world = World();
        var engine = new SimulationEngine(world, rules);
        var before = engine.Snapshot;

        var advice = engine.GetSeizeAdvice(1, 11, 2);

        Assert.Equal(AdviserOpinion.NoAdviser, advice.Opinion);
        Assert.Equal(advice, engine.GetSeizeAdvice(1, 11, 2));
        StateAssert.Equal(before, engine.Snapshot);
        var task = Submit(world, rules, 0);
        var snapshot = engine.Snapshot;
        Assert.NotNull(task.Journey!.Seize);
        Assert.Equal(9999u, snapshot.Corps.Single(force => force.IsRoot && force.OwnerId == 2).Treasury.Money);
        Assert.Equal(22, snapshot.Treasures.Single(treasure => treasure.Id == 10).HolderId);
    }

    [Fact]
    public void AdviceUsesTotalProbabilityAndIgnoresExecutionStreamAndRunSeed()
    {
        var rules = Rules();
        var world = World();
        var state = world.BeginMutation();
        state.Replace(state.Officers[31] with
        {
            SettlementId = 1,
            Status = OfficerStatus.Strategist,
            BaseAbilities = new(70, 100, 70, 70)
        });
        state.Replace(state.Forces[1] with { AdviserId = 31 });
        world.Commit(state);
        var engine = new SimulationEngine(world, rules);
        // Item threshold 88 with probability 1/2; money threshold 25 always: total 56.5%.
        var expected = engine.GetSeizeAdvice(1, 11, 2);
        Assert.Equal(AdviserOpinion.Favorable, expected.Opinion);
        for (int i = 0; i < 20; i++)
        {
            state = world.BeginMutation();
            state.NextRandom("strategy-seize-item-gate");
            state.SetSimulation(state.Simulation with { RunSeed = (ulong)i });
            world.Commit(state);
            var before = engine.Snapshot;
            Assert.Equal(expected, engine.GetSeizeAdvice(1, 11, 2));
            StateAssert.Equal(before, engine.Snapshot);
        }
        state = world.BeginMutation();
        state.Replace(state.Officers[11] with { BaseAbilities = new(87, 70, 70, 70) });
        world.Commit(state);
        // (75 + 25) / 2 = 50%; the fixed draw still predicts a success, without a special midpoint category.
        Assert.Equal(AdviserOpinion.Favorable, engine.GetSeizeAdvice(1, 11, 2).Opinion);
        state = world.BeginMutation();
        state.Replace(state.Officers[11] with { BaseAbilities = new(88, 70, 70, 70) });
        world.Commit(state);
        Assert.Equal(AdviserOpinion.Favorable, engine.GetSeizeAdvice(1, 11, 2).Opinion);
    }

    [Fact]
    public void ZeroTargetFundsOrNonAdjacentTargetIsRejectedAndLiveFundsAreRevalidated()
    {
        var rules = Rules();
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(ResourceAccounts.Root(mutation, 2) with { Treasury = ResourceAccounts.Root(mutation, 2).Treasury with { Money = 0 } });
        world.Commit(mutation);
        var engine = new SimulationEngine(world, rules);
        Assert.Equal(CommandRejection.InvalidTask, engine.GetSeizeAdvice(1, 11, 2).Rejection);
        Assert.Equal(CommandRejection.InvalidTask, engine.GetSeizeAdvice(1, 11, 3).Rejection);

        world = World();
        Submit(world, rules, 0);
        mutation = world.BeginMutation(rules.Abilities);
        mutation.Replace(ResourceAccounts.Root(mutation, 2) with { Treasury = ResourceAccounts.Root(mutation, 2).Treasury with { Money = 0 } });
        List<DomainEvent> events = [];
        new ResolutionPipeline(rules).AdvanceDay(mutation, events);
        Assert.Single(events.OfType<TaskCancelledEvent>());
        Assert.Empty(events.OfType<SeizeResolvedEvent>());
    }

    [Fact]
    public void PayloadRejectsConflictsAndRequiresSeizeDiscriminator()
    {
        var rules = Rules();
        var world = World();
        var task = Submit(world, rules, 0);
        Assert.False(new SeizeRule(rules).HasValidPayload(task with
            { Journey = task.Journey! with { Burn = new(2) } }));
        string json = System.Text.Json.JsonSerializer.Serialize(task.Journey);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        Assert.True(node.Remove("Seize"));
        Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<PersonnelJourney>(node.ToJsonString()));
    }

    [Fact]
    public void ArithmeticMatchesVerifiedSignedThresholdsAndBoundedMoneyFormula()
    {
        Assert.Equal(78, StrategyMath.SeizeItemThreshold(100, 1, 20));
        Assert.Equal(0, StrategyMath.SeizePersonThreshold(100, 0));
        Assert.Equal(11, StrategyMath.SeizePersonThreshold(100, 6));
        Assert.Equal(25, StrategyMath.SeizeMoneyThreshold(100, 20, 30000));
        Assert.Equal(484u, StrategyMath.SeizeMoney(30000, 4000, 10000, 49));
        Assert.Equal(
            (uint)Math.Min(uint.MaxValue, ((ulong)uint.MaxValue * 2 / 80) + 59),
            StrategyMath.SeizeMoney(uint.MaxValue, uint.MaxValue, uint.MaxValue, 49));
    }
}
