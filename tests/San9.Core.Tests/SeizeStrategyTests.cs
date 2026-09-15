using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SeizeStrategyTests
{
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
        [new ForceState(1, actorMoney, 10000, 100) { LeaderId = 11 },
         new ForceState(2, 10000, 10000, 100) { LeaderId = 22 }],
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
        relations: [new(1, 2, 50)], controls: [new("player", 1, ControllerKind.Player)]);

    private static PersistentTask Submit(WorldState world, SimulationRules rules, int roll)
    {
        var engine = new SimulationEngine(world, rules);
        var command = new SeizeCommand(Guid.NewGuid(), 1, world.Simulation.NextSequence, 11, 2);
        string before = engine.Snapshot.StateHash;
        uint targetMoney = engine.Snapshot.Forces.Single(force => force.Id == 2).Money;
        Assert.True(engine.Validate(new("player"), command).Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(targetMoney, engine.Snapshot.Forces.Single(force => force.Id == 2).Money);
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
        Assert.Equal(9999u, state.Forces[2].Money);
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
        Assert.Equal(9999u, state.Forces[2].Money);
    }

    [Fact]
    public void MoneyBranchTransfersBoundedAmountWithoutChangingTroopsOrWounded()
    {
        var (state, result, events) = Find(r => !r.ItemGatePassed, 10, captive: false);

        Assert.Equal(SeizeOutcomeKind.Money, result.Outcome);
        Assert.InRange(result.Money, 435u, 484u);
        Assert.Equal(9999u + result.Money, state.Forces[1].Money);
        Assert.Equal(9999u - result.Money, state.Forces[2].Money);
        Assert.Equal(30000u, state.Settlements[2].Soldiers);
        Assert.Equal(4000u, state.Settlements[2].Wounded);
        Assert.Equal(0, events.OfType<ResourceChangedEvent>().Sum(e => e.Actual));

        var capped = Find(r => !r.ItemGatePassed, 10, captive: false, actorMoney: 1000000);
        Assert.Equal(1u, capped.Result.Money);
        Assert.Equal(1000000u, capped.State.Forces[1].Money);
        Assert.Equal(9998u, capped.State.Forces[2].Money);
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
        Assert.Equal(9999u, state.Forces[2].Money);
    }

    [Fact]
    public void AdviceIsStableAndReadOnlyWhileSubmissionOnlyReservesActor()
    {
        var rules = Rules();
        var world = World();
        var engine = new SimulationEngine(world, rules);
        string before = engine.Snapshot.StateHash;

        var advice = engine.GetSeizeAdvice(1, 11, 2);

        Assert.Equal(AdviserOpinion.NoAdviser, advice.Opinion);
        Assert.Equal(advice, engine.GetSeizeAdvice(1, 11, 2));
        Assert.Equal(before, engine.Snapshot.StateHash);
        var task = Submit(world, rules, 0);
        var snapshot = engine.Snapshot;
        Assert.NotNull(task.Journey!.Seize);
        Assert.Equal(9999u, snapshot.Forces.Single(force => force.Id == 2).Money);
        Assert.Equal(22, snapshot.Treasures.Single(treasure => treasure.Id == 10).HolderId);
    }

    [Fact]
    public void ZeroTargetFundsOrNonAdjacentTargetIsRejectedAndLiveFundsAreRevalidated()
    {
        var rules = Rules();
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Forces[2] with { Money = 0 });
        world.Commit(mutation);
        var engine = new SimulationEngine(world, rules);
        Assert.Equal(CommandRejection.InvalidTask, engine.GetSeizeAdvice(1, 11, 2).Rejection);
        Assert.Equal(CommandRejection.InvalidTask, engine.GetSeizeAdvice(1, 11, 3).Rejection);

        world = World();
        Submit(world, rules, 0);
        mutation = world.BeginMutation(rules.Abilities);
        mutation.Replace(mutation.Forces[2] with { Money = 0 });
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
