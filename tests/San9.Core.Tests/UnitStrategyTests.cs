using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class UnitStrategyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuarterlyPopulationCountsOutboundAndReturningReinforcements(bool returning)
    {
        var rules = Rules();
        var world = World(friendly: true, distance: 300);
        var task = Submit(world, rules, UnitStrategyKind.Reinforce, 0);
        var state = world.BeginMutation(rules.Abilities);
        foreach (var city in state.Settlements.Values.ToArray())
            state.Replace(city with { Population = 112000, DraftPopulation = 0 });
        if (returning)
        {
            state.SetSimulation(state.Simulation with { Tasks = [] });
            PersonnelReturns.Start(state, task, rules.Personnel, [], afterDailyCommands: false, arrived: true);
            Assert.Equal(PersonnelReturns.Kind, Assert.Single(state.Simulation.Tasks).Kind);
        }
        Assert.Equal(25000, SoldierTotals.Living(state));
        Assert.Equal(15000, SoldierTotals.Living(state, 1));
        state.SetClock(new(190, 4, 1));
        EconomicCycle.Apply(ResolutionStage.QuarterUpdate, state, EconomyRulebook.Empty, []);
        Assert.All(state.Settlements.Values, city => Assert.Equal(224u, city.DraftPopulation));
    }

    [Fact]
    public void AnnualBarbarianSupplyCountsTaskTroopsAndWoundedOnce()
    {
        var rules = Rules(); var world = World(friendly: true, distance: 300);
        Submit(world, rules, UnitStrategyKind.Reinforce, 0);
        var state = world.BeginMutation(rules.Abilities);
        state.Replace(state.Forces[1] with { IsBarbarian = true });
        state.Replace(state.Settlements[1] with { Soldiers = 95000, Wounded = 1000 });
        state.Replace(state.Units[5] with { Wounded = 2000 });
        Assert.Equal(103000, SoldierTotals.Force(state, 1));
        Assert.Equal(106000, SoldierTotals.Living(state, 1));
        state.SetClock(new(190, 7, 1));
        EconomicCycle.Apply(ResolutionStage.QuarterUpdate, state, EconomyRulebook.Empty, []);
        Assert.Equal(106000u, state.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Food);
    }

    [Fact]
    public void AdviceIsStableDoesNotAdvanceRandomnessAndChangesWithRelevantAbility()
    {
        var rules = Rules();
        var world = World(friendly: true);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Forces[1] with { AdviserId = 12 });
        mutation.Replace(mutation.Officers[11] with { BaseAbilities = new(70, 0, 70, 70) });
        mutation.Replace(mutation.Officers[12] with { Status = OfficerStatus.Strategist, BaseAbilities = new(70, 100, 70, 70) });
        world.Commit(mutation);
        var engine = new SimulationEngine(world, rules);
        string before = engine.Snapshot.StateHash;
        var expected = engine.GetUnitStrategyAdvice(1, 11, UnitStrategyKind.Inspire, 5);
        Assert.Equal(AdviserOpinion.Unfavorable, expected.Opinion);
        for (int query = 0; query < 20; query++) Assert.Equal(expected, engine.GetUnitStrategyAdvice(1, 11, UnitStrategyKind.Inspire, 5));
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.True(engine.Execute(new("player"), new WaitCommand(Guid.NewGuid(), 1, 0)).Submission.Accepted);
        Assert.Equal(expected, engine.GetUnitStrategyAdvice(1, 11, UnitStrategyKind.Inspire, 5));
        mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[11] with { BaseAbilities = new(70, 200, 70, 70) });
        world.Commit(mutation);
        Assert.Equal(AdviserOpinion.Favorable, engine.GetUnitStrategyAdvice(1, 11, UnitStrategyKind.Inspire, 5).Opinion);
    }

    [Fact]
    public void UnrelatedCommandAndRunSeedDoNotChangeSavedSubmissionDraw()
    {
        var rules = Rules();
        var first = World();
        int? roll = Submit(first, rules, UnitStrategyKind.FalseReport).Journey!.SuccessRoll;
        var second = World();
        var engine = new SimulationEngine(second, rules);
        Assert.True(engine.Execute(new("player"), new WaitCommand(Guid.NewGuid(), 1, 0)).Submission.Accepted);
        var snapshot = engine.Snapshot;
        second = WorldState.Restore(snapshot with { Simulation = snapshot.Simulation with { RunSeed = 987654321 } });
        Assert.Equal(roll, Submit(second, rules, UnitStrategyKind.FalseReport).Journey!.SuccessRoll);
    }

    [Fact]
    public void FalseReportClearsAttackAndKeepsLongerExistingDurationUntilExpiry()
    {
        var rules = Rules();
        var world = World(distance: 30);
        Submit(world, rules, UnitStrategyKind.FalseReport, 0);
        var state = world.BeginMutation(rules.Abilities);
        state.Replace(state.Units[5] with { Condition = UnitCondition.NoFormation, ConditionDaysRemaining = 25,
            Order = new(MilitaryOrderKind.Move, new(70, 2), null) });
        Day(state, rules);
        Assert.Null(state.Units[5].Order);
        Assert.Equal(24, state.Units[5].ConditionDaysRemaining);
        Assert.Equal(UnitCondition.Disguised, state.Units[5].Condition);
        for (int day = 0; day < 24; day++) MilitaryConditions.Tick(state, []);
        Assert.Equal(UnitCondition.Normal, state.Units[5].Condition);
        Assert.Equal(0, state.Units[5].ConditionDaysRemaining);
    }

    internal static SimulationRules Rules(bool defensive = false, bool immune = false, bool reinforcementConnection = true) => new()
    {
        Personnel = new([new(1, new(2, 2)), new(2, new(997, 12))],
            new Dictionary<int, int> { [1] = 1, [2] = 2 }, reinforcementConnection ? [new(1, 2)] : []),
        Military = new(new(200, 3, Enumerable.Repeat(0, 600).ToImmutableArray(), [new(0, 1, null)],
            [new(9, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)
                { HalvesHostileStrategyChance = defensive, ImmuneToDisorder = immune }],
            [new(1, new(2, 2), new(1, 1)), new(2, new(997, 12), new(1, 1))])
            { RegionIds = Enumerable.Repeat<int?>(2, 600).ToImmutableArray() })
    };

    internal static WorldState World(bool friendly = false, int distance = 31, int intelligence = 99) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 100), new(2, 100)],
        [new(1, 1, 10000, 100, 100, 70, 500, 0, 100, 0, 100), new(2, 2, 10000, 100, 100, 70, 500, 0, 100, 0, 100)],
        [new(11, 1, 1, new(70, intelligence, 70, 70), false), new(12, 1, 1, new(70, 70, 70, 70), false),
         new(22, friendly ? 1 : 2, null, new(70, 70, 70, 80), false) { UnitId = 5, TacticCooldown = 20 }],
        units: [new(5, friendly ? 1 : 2, new(2 + distance, 2), 5000) { FormationId = 9, Morale = 70,
            CommanderId = 22, OrderClass = UnitOrderClass.Military }],
        relations: [new(1, 2, 50)], controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(10000, 10000)) { IsRoot = true }]);

    internal static PersistentTask Submit(WorldState world, SimulationRules rules, UnitStrategyKind strategy, int? forcedRoll = null)
    {
        var engine = new SimulationEngine(world, rules);
        var command = new UnitStrategyCommand(Guid.NewGuid(), 1, world.Simulation.NextSequence, 11, strategy, 5);
        string hash = engine.Snapshot.StateHash;
        Assert.True(engine.Validate(new("player"), command).Accepted);
        Assert.Equal(hash, engine.Snapshot.StateHash);
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        if (forcedRoll is int roll)
        {
            var mutation = world.BeginMutation();
            var task = Assert.Single(mutation.Simulation.Tasks);
            mutation.SetSimulation(mutation.Simulation with { Tasks = [task with { Journey = task.Journey! with { SuccessRoll = roll } }] });
            world.Commit(mutation);
        }
        return Assert.Single(world.Simulation.Tasks);
    }

    internal static List<DomainEvent> Day(WorldMutation mutation, SimulationRules rules)
    {
        List<DomainEvent> events = [];
        new ResolutionPipeline(rules).AdvanceDay(mutation, events);
        return events;
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(30, 1)]
    [InlineData(31, 2)]
    public void SubmissionOnlyReservesAndUsesFinePositionCountdown(int distance, int processingDay)
    {
        var rules = Rules();
        var world = World(distance: distance);
        var task = Submit(world, rules, UnitStrategyKind.FalseReport, 0);
        var state = world.BeginMutation(rules.Abilities);
        Assert.Equal(new WorldPosition(2 + distance, 2), task.Journey!.Route.Destination);
        Assert.Equal(UnitCondition.Normal, Assert.Single(state.Units.Values).Condition);
        Assert.Equal(10000u, state.Settlements.Values.Single(s => s.Id == 1).Soldiers);
        for (int day = 1; day < processingDay; day++) Assert.Empty(Day(state, rules).OfType<UnitStrategyResolvedEvent>());
        Assert.True(Assert.Single(Day(state, rules).OfType<UnitStrategyResolvedEvent>()).Success);
        Assert.Equal(UnitCondition.Disguised, Assert.Single(state.Units.Values).Condition);
    }

    [Theory]
    [InlineData(UnitStrategyKind.FalseReport, 28, true)]
    [InlineData(UnitStrategyKind.FalseReport, 29, false)]
    [InlineData(UnitStrategyKind.Disorder, 20, true)]
    [InlineData(UnitStrategyKind.Disorder, 21, false)]
    public void LiveThresholdUsesStrictComparisonAndOnlyExecutorGainsProficiency(UnitStrategyKind strategy, int roll, bool success)
    {
        var rules = Rules();
        var world = World(distance: 30);
        Submit(world, rules, strategy, roll);
        var state = world.BeginMutation(rules.Abilities);
        var result = Assert.Single(Day(state, rules).OfType<UnitStrategyResolvedEvent>());
        Assert.Equal(success, result.Success);
        Assert.Equal(strategy == UnitStrategyKind.FalseReport ? 29 : 21, result.Threshold);
        Assert.Equal(2, state.Officers.Values.Single(p => p.Id == 11).TacticProficiencies[7]);
        Assert.Equal(0, state.Officers.Values.Single(p => p.Id == 12).TacticProficiencies[7]);
        Assert.InRange(Assert.Single(state.Relations.Values).Value, success ? 50 : 47, success ? 50 : 49);
    }

    [Fact]
    public void InspireClearsCooldownRearmsPenaltyAndReturnsWithoutGrowingGarrison()
    {
        var rules = Rules();
        var world = World(friendly: true, distance: 31);
        Submit(world, rules, UnitStrategyKind.Inspire, 0);
        var state = world.BeginMutation(rules.Abilities);
        Day(state, rules);
        var events = Day(state, rules);
        var result = Assert.Single(events.OfType<UnitStrategyResolvedEvent>());
        Assert.True(result.Success);
        Assert.Equal(UnitCondition.Normal,result.Target.Condition);
        Assert.Equal(new WorldPosition(33,2),result.Target.Position);
        Assert.Equal(22,result.Target.CommanderId);
        var unit = Assert.Single(state.Units.Values);
        Assert.Equal(UnitCondition.HighSpirits, unit.Condition);
        Assert.Equal(11, unit.ConditionDaysRemaining);
        Assert.Equal(89, unit.Morale); // The first daily stage already applied the ordinary field morale loss.
        Assert.True(unit.HighSpiritsMoralePenaltyPending);
        Assert.Equal(0, state.Officers.Values.Single(p => p.Id == 22).TacticCooldown);
        var returning = Assert.Single(state.Simulation.Tasks);
        Assert.Equal("personnel-return", returning.Kind);
        Assert.Equal(new WorldPosition(33, 2), returning.Journey!.Route.Origin);
        Assert.Null(returning.Journey.UnitStrategy);
        Day(state, rules);
        Assert.Single(Day(state, rules).OfType<PersonnelReturnedEvent>());
        Assert.Empty(state.Simulation.Tasks);
    }

    [Fact]
    public void FailureLowersInspireMoraleWithoutChangingConditionOrGrowingAbility()
    {
        var rules = Rules();
        var world = World(friendly: true, distance: 30);
        Submit(world, rules, UnitStrategyKind.Inspire, 99);
        var state = world.BeginMutation(rules.Abilities);
        var events = Day(state, rules);
        Assert.False(Assert.Single(events.OfType<UnitStrategyResolvedEvent>()).Success);
        Assert.Empty(events.OfType<OfficerAbilityGrownEvent>());
        Assert.Equal(UnitCondition.Normal, Assert.Single(state.Units.Values).Condition);
        Assert.InRange(Assert.Single(state.Units.Values).Morale, 64, 68);
    }

    [Fact]
    public void TargetChangesAreRevalidatedAndExpiredTargetReturnsInsteadOfApplyingEffects()
    {
        var rules = Rules();
        var world = World(friendly: true);
        Submit(world, rules, UnitStrategyKind.Inspire, 0);
        var state = world.BeginMutation(rules.Abilities);
        state.Replace(state.Units[5] with { Morale = 100, Condition = UnitCondition.HighSpirits, ConditionDaysRemaining = 100 });
        var events = Day(state, rules);
        Assert.Single(events.OfType<TaskCancelledEvent>());
        Assert.Empty(events.OfType<UnitStrategyResolvedEvent>());
        Assert.Equal("personnel-return", Assert.Single(state.Simulation.Tasks).Kind);
        Assert.Equal(99, Assert.Single(state.Units.Values).Morale);
    }

    [Fact]
    public void OutboundAndReturnSnapshotsReplayIdenticallyAndRejectMismatchedPayloads()
    {
        var rules = Rules();
        var world = World(friendly: true, distance: 330);
        Submit(world, rules, UnitStrategyKind.Inspire, 0);
        var engine = new SimulationEngine(world, rules);
        var restored = new SimulationEngine(WorldState.Restore(world.CreateSnapshot()), rules);
        for (int xun = 0; xun < 3; xun++)
        {
            engine.EndControl(new("player"));
            restored.EndControl(new("player"));
            Assert.Equal(CanonicalJson.Hash(engine.AdvanceXun().Events), CanonicalJson.Hash(restored.AdvanceXun().Events));
            Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
            if (xun == 0) Assert.Equal("unit-strategy", Assert.Single(engine.Snapshot.Simulation.Tasks).Kind);
            if (xun == 1) Assert.Equal("personnel-return", Assert.Single(engine.Snapshot.Simulation.Tasks).Kind);
            restored = new SimulationEngine(WorldState.Restore(restored.Snapshot), rules);
        }
        var invalid = World(friendly: true);
        var task = Submit(invalid, rules, UnitStrategyKind.Inspire);
        var snapshot = invalid.CreateSnapshot();
        snapshot = snapshot with { Simulation = snapshot.Simulation with { Tasks = [task with { Kind = "personnel-search" }] } };
        Assert.Throws<ArgumentException>(() => new SimulationEngine(WorldState.Restore(snapshot), rules));
    }

    [Fact]
    public void FormationSemanticsAndLiveAbilityChangesAffectThreshold()
    {
        var rules = Rules(defensive: true);
        var world = World();
        Submit(world, rules, UnitStrategyKind.FalseReport, 10);
        var state = world.BeginMutation(rules.Abilities);
        state.Replace(state.Officers[22] with { BaseAbilities = new(70, 99, 70, 80) });
        Day(state, rules);
        var result = Assert.Single(Day(state, rules).OfType<UnitStrategyResolvedEvent>());
        Assert.Equal(3, result.Threshold);
        Assert.False(result.Success);
        var immune = new SimulationEngine(World(), Rules(immune: true));
        Assert.False(immune.Execute(new("player"), new UnitStrategyCommand(Guid.NewGuid(), 1, 0, 11, UnitStrategyKind.Disorder, 5)).Submission.Accepted);
    }

    [Fact]
    public void ReinforcementCarriesOverflowHomeAndUsesDeliveredMorale()
    {
        var rules = Rules();
        var world = World(friendly: true, distance: 30);
        var setup = world.BeginMutation();
        setup.Replace(setup.Units[5] with { Soldiers = 9000, Morale = 40, Wounded = 250,
            Condition = UnitCondition.Confused, ConditionDaysRemaining = 7 });
        world.Commit(setup);

        var task = Submit(world, rules, UnitStrategyKind.Reinforce, 0);
        Assert.Equal(7000u, world.BeginMutation().Settlements[1].Soldiers);
        Assert.Equal(70, task.Reinforcements!.Morale);
        string json = CanonicalJson.Text(world.CreateSnapshot());
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<WorldSnapshot>(json, CanonicalJson.Options)!;
        var restored = new SimulationEngine(WorldState.Restore(snapshot), rules);
        Assert.Equal(world.CreateSnapshot().StateHash, restored.Snapshot.StateHash);
        Assert.Equal(70, Assert.Single(restored.Snapshot.Simulation.Tasks).Reinforcements!.Morale);

        var state = world.BeginMutation(rules.Abilities);
        state.SetSimulation(state.Simulation with { Tasks = state.Simulation.Tasks.Remove(task) });
        List<DomainEvent> events = [];
        UnitStrategies.Resolve(state, task, rules, events);

        var result = Assert.Single(events.OfType<UnitStrategyResolvedEvent>());
        Assert.True(result.Success);
        Assert.Equal(50, result.Threshold);
        Assert.Equal(3000u, result.RequestedSoldiers);
        Assert.Equal(1000u, result.DeliveredSoldiers);
        Assert.Equal(10000u, state.Units[5].Soldiers);
        Assert.Equal(43, state.Units[5].Morale);
        Assert.Equal(250u, state.Units[5].Wounded);
        Assert.Equal(UnitCondition.Normal, state.Units[5].Condition);
        Assert.Equal(0, state.Units[5].ConditionDaysRemaining);
        Assert.Equal(7000u, state.Settlements[1].Soldiers);
        Assert.Equal(2, state.Officers[11].TacticProficiencies[7]);
        Assert.Equal(2, state.Officers[11].TacticProficiencies[8]);
        Assert.Single(state.Simulation.Tasks, t => t.Kind == PersonnelReturns.Kind);
        var returning = Assert.Single(state.Simulation.Tasks);
        Assert.Equal(2000u, returning.Reinforcements!.Soldiers);
        state.Replace(state.Settlements[1] with { Morale = 10, SoldierCapacity = 8000 });
        Day(state, rules);
        Day(state, rules);
        Assert.Equal(8000u, state.Settlements[1].Soldiers);
        Assert.Equal(17, state.Settlements[1].Morale);
        Assert.Equal(1000u, state.Settlements[1].DraftPopulation);
        Assert.Empty(state.Simulation.Tasks);
    }

    [Fact]
    public void FailedReinforcementReturnsUndeliveredTroopsAndKeepsCondition()
    {
        var rules = Rules();
        var world = World(friendly: true, distance: 30);
        var setup = world.BeginMutation();
        setup.Replace(setup.Units[5] with { Soldiers = 9500, Morale = 40, Wounded = 250,
            Condition = UnitCondition.Confused, ConditionDaysRemaining = 7 });
        world.Commit(setup);
        var task = Submit(world, rules, UnitStrategyKind.Reinforce, 99);

        var state = world.BeginMutation(rules.Abilities);
        state.SetSimulation(state.Simulation with { Tasks = state.Simulation.Tasks.Remove(task) });
        List<DomainEvent> events = [];
        UnitStrategies.Resolve(state, task, rules, events);

        var result = Assert.Single(events.OfType<UnitStrategyResolvedEvent>());
        Assert.False(result.Success);
        Assert.Equal(750u, result.RequestedSoldiers);
        Assert.Equal(500u, result.DeliveredSoldiers);
        Assert.Equal(41, state.Units[5].Morale);
        Assert.Equal(2500u, Assert.Single(state.Simulation.Tasks).Reinforcements!.Soldiers);
        Assert.Equal(UnitCondition.Confused, state.Units[5].Condition);
        Assert.Equal(7, state.Units[5].ConditionDaysRemaining);
        Assert.Equal(250u, state.Units[5].Wounded);
        Assert.Equal(7000u, state.Settlements[1].Soldiers);
        Assert.Equal(2, state.Officers[11].TacticProficiencies[7]);
        Assert.Equal(2, state.Officers[11].TacticProficiencies[8]);
    }

    [Fact]
    public void SuccessfulReinforcementPreservesOnlyNormalAndHighSpiritsConditions()
    {
        var rules = Rules();
        foreach (var condition in Enum.GetValues<UnitCondition>())
        {
            var world = World(friendly: true, distance: 30);
            var task = Submit(world, rules, UnitStrategyKind.Reinforce, 0);
            var state = world.BeginMutation(rules.Abilities);
            state.SetSimulation(state.Simulation with { Tasks = state.Simulation.Tasks.Remove(task) });
            state.Replace(state.Units[5] with { Condition = condition, ConditionDaysRemaining = 7,
                HighSpiritsMoralePenaltyPending = condition == UnitCondition.HighSpirits });
            UnitStrategies.Resolve(state, task, rules, []);

            bool preserved = condition is UnitCondition.Normal or UnitCondition.HighSpirits;
            Assert.Equal(preserved ? condition : UnitCondition.Normal, state.Units[5].Condition);
            Assert.Equal(preserved ? 7 : 0, state.Units[5].ConditionDaysRemaining);
            Assert.Equal(condition == UnitCondition.HighSpirits, state.Units[5].HighSpiritsMoralePenaltyPending);
        }
    }

    [Fact]
    public void ReinforcementRequiresCostRangeAndCapacityAtSubmission()
    {
        var insufficient = World(friendly: true);
        var mutation = insufficient.BeginMutation();
        mutation.Replace(mutation.Settlements[1] with { Soldiers = 2999 });
        insufficient.Commit(mutation);
        Assert.False(new SimulationEngine(insufficient, Rules()).Execute(new("player"),
            new UnitStrategyCommand(Guid.NewGuid(), 1, 0, 11, UnitStrategyKind.Reinforce, 5)).Submission.Accepted);

        var outOfRange = new SimulationEngine(World(friendly: true), Rules(reinforcementConnection: false));
        Assert.False(outOfRange.Execute(new("player"),
            new UnitStrategyCommand(Guid.NewGuid(), 1, 0, 11, UnitStrategyKind.Reinforce, 5)).Submission.Accepted);

        var full = World(friendly: true);
        mutation = full.BeginMutation();
        mutation.Replace(mutation.Units[5] with { Soldiers = 10000 });
        full.Commit(mutation);
        Assert.False(new SimulationEngine(full, Rules()).Execute(new("player"),
            new UnitStrategyCommand(Guid.NewGuid(), 1, 0, 11, UnitStrategyKind.Reinforce, 5)).Submission.Accepted);
    }

    [Fact]
    public void TargetFullAtArrivalReturnsAllTroops()
    {
        var rules = Rules();
        var world = World(friendly: true, distance: 30);
        Submit(world, rules, UnitStrategyKind.Reinforce, 0);
        var state = world.BeginMutation(rules.Abilities);
        state.Replace(state.Units[5] with { Soldiers = 10000 });

        var events = Day(state, rules);

        Assert.Single(events.OfType<TaskCancelledEvent>());
        Assert.Empty(events.OfType<UnitStrategyResolvedEvent>());
        Assert.Equal(7000u, state.Settlements[1].Soldiers);
        Assert.Equal(10000u, state.Units[5].Soldiers);
        Assert.Single(state.Simulation.Tasks, t => t.Kind == PersonnelReturns.Kind);
        Assert.Equal(3000u, Assert.Single(state.Simulation.Tasks).Reinforcements!.Soldiers);
        Day(state, rules);
        Assert.Equal(10000u, state.Settlements[1].Soldiers);
        Assert.Empty(state.Simulation.Tasks);
    }

    [Fact]
    public void FalseReportForcesMovementHomeUntilExpiryAndPreservesNewOrders()
    {
        var rules = Rules();
        var world = World(distance: 30);
        var setup = world.BeginMutation();
        setup.Replace(setup.Units[5] with { ServicePlaceId = 2,
            Order = new(MilitaryOrderKind.Move, new(20, 2), null) });
        world.Commit(setup);
        var task = Submit(world, rules, UnitStrategyKind.FalseReport, 0);
        var state = world.BeginMutation(rules.Abilities);
        state.SetSimulation(state.Simulation with { Tasks = state.Simulation.Tasks.Remove(task) });
        UnitStrategies.Resolve(state, task, rules, []);
        Assert.Null(state.Units[5].Order);
        Assert.Equal(2, state.Units[5].EffectiveOrder!.SettlementId);
        Assert.Equal(CommandRejection.None, MilitaryMovement.Order(state,
            new(Guid.NewGuid(), 2, 0, 5, new(MilitaryOrderKind.Move, new(20, 2), null)), rules.Military, []));
        var before = state.Units[5].Position;
        MilitaryMovement.AdvanceDay(state, rules.Military, rules.Governance, rules.Personnel, []);
        Assert.True(state.Units[5].Position.X > before.X);
        Assert.Equal(MilitaryOrderKind.Return, state.Units[5].EffectiveOrder!.Kind);
        state.Replace(state.Units[5] with { ConditionDaysRemaining = 1 });
        MilitaryConditions.Tick(state, []);
        Assert.Equal(MilitaryOrderKind.Move, state.Units[5].EffectiveOrder!.Kind);
        before = state.Units[5].Position;
        MilitaryMovement.AdvanceDay(state, rules.Military, rules.Governance, rules.Personnel, []);
        Assert.True(state.Units[5].Position.X < before.X);
    }

    [Fact]
    public void FalseReportEffectiveReturnSurvivesSaveAndDailyContinuation()
    {
        var rules = Rules();
        var world = World();
        var state = world.BeginMutation(rules.Abilities);
        state.Replace(state.Units[5] with { ServicePlaceId = 2,
            Condition = UnitCondition.Disguised, ConditionDaysRemaining = 5,
            Order = new(MilitaryOrderKind.Move, new(20, 2), null) });
        world.Commit(state);
        var first = new SimulationEngine(world, rules);
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<WorldSnapshot>(
            CanonicalJson.Text(world.CreateSnapshot()), CanonicalJson.Options)!;
        var restored = WorldState.Restore(snapshot);
        var second = new SimulationEngine(restored, rules);
        Assert.Equal(MilitaryOrderKind.Return, second.Snapshot.Units.Single().EffectiveOrder!.Kind);
        first.EndControl(new("player"));
        second.EndControl(new("player"));
        Assert.Equal(CanonicalJson.Text(first.AdvanceXun().Events), CanonicalJson.Text(second.AdvanceXun().Events));
        Assert.Equal(first.Snapshot.StateHash, second.Snapshot.StateHash);
    }

    [Fact]
    public void FalseReportWithoutServicePlaceCannotExecuteStoredAttack()
    {
        var rules = Rules();
        var world = World();
        var state = world.BeginMutation(rules.Abilities);
        state.Replace(state.Units[5] with { Condition = UnitCondition.Disguised, ConditionDaysRemaining = 5,
            ServicePlaceId = null, Order = new(MilitaryOrderKind.Move, new(50, 2), null) });
        var before = state.Units[5].Position;
        Assert.Null(state.Units[5].EffectiveOrder);
        MilitaryMovement.AdvanceDay(state, rules.Military, rules.Governance, rules.Personnel, []);
        Assert.Equal(before, state.Units[5].Position);
    }

    [Fact]
    public void FalseReportReturnsIntoServicePlaceWithTroopsAndOfficers()
    {
        var rules = Rules();
        var world = World();
        var state = world.BeginMutation(rules.Abilities);
        state.Replace(state.Units[5] with { Position = new(995, 12), ServicePlaceId = 2,
            Condition = UnitCondition.Disguised, ConditionDaysRemaining = 7 });
        for (int day = 0; day < 5 && state.Units.ContainsKey(5); day++)
            MilitaryMovement.AdvanceDay(state, rules.Military, rules.Governance, rules.Personnel, []);
        Assert.False(state.Units.ContainsKey(5));
        Assert.Equal(15000u, state.Settlements[2].Soldiers);
        Assert.Equal(2, state.Officers[22].SettlementId);
        Assert.Null(state.Officers[22].UnitId);
    }

    [Fact]
    public void TemporaryFullTargetBeforeArrivalDoesNotCancelReinforcement()
    {
        var rules = Rules();
        var world = World(friendly: true, distance: 90);
        var task = Submit(world, rules, UnitStrategyKind.Reinforce, 0);
        var state = world.BeginMutation(rules.Abilities);
        state.Replace(state.Units[5] with { Soldiers = 10000 });
        Assert.Empty(Day(state, rules).OfType<TaskCancelledEvent>());
        Assert.Equal(task.Id, Assert.Single(state.Simulation.Tasks).Id);
        state.Replace(state.Units[5] with { Soldiers = 9000 });
        long total = SoldierTotals.Force(state, 1);
        Assert.Equal(total, SoldierTotals.Corps(state, 1, null));
        Day(state, rules);
        var result = Assert.Single(Day(state, rules).OfType<UnitStrategyResolvedEvent>());
        Assert.Equal(1000u, result.DeliveredSoldiers);
        Assert.Equal(2000u, Assert.Single(state.Simulation.Tasks).Reinforcements!.Soldiers);
        Assert.Equal(total, SoldierTotals.Force(state, 1));
        Assert.Equal(total, SoldierTotals.Corps(state, 1, null));
        for (int i = 0; i < 40 && state.Simulation.Tasks.Length > 0; i++) Day(state, rules);
        Assert.Empty(state.Simulation.Tasks);
        Assert.Equal(total, SoldierTotals.Force(state, 1));
        Assert.Equal(total, SoldierTotals.Corps(state, 1, null));
    }

    [Fact]
    public void LostSourceReroutesTroopsAndNoHomeDemobilizesThem()
    {
        foreach (bool alternative in new[] { false, true })
        {
            var rules = Rules();
            var world = World(friendly: true, distance: 90);
            Submit(world, rules, UnitStrategyKind.Reinforce, 0);
            var state = world.BeginMutation(rules.Abilities);
            state.Replace(state.Settlements[1] with { OwnerId = 2 });
            if (alternative) state.Replace(state.Settlements[2] with { OwnerId = 1 });
            Day(state, rules);
            if (alternative)
            {
                var returning = Assert.Single(state.Simulation.Tasks);
                Assert.Equal(2, returning.OriginSettlementId);
                Assert.Equal(3000u, returning.Reinforcements!.Soldiers);
                for (int i = 0; i < 40 && state.Simulation.Tasks.Length > 0; i++) Day(state, rules);
                Assert.Empty(state.Simulation.Tasks);
                Assert.Equal(13000u, state.Settlements[2].Soldiers);
            }
            else
            {
                Assert.Empty(state.Simulation.Tasks);
                Assert.Equal(3000u, state.Settlements[1].DraftPopulation);
            }
            Assert.Equal(7000u, state.Settlements[1].Soldiers);
        }
    }

    [Fact]
    public void CapturedExecutorDemobilizesEscortOnlyOnce()
    {
        var rules = Rules();
        var world = World(friendly: true);
        Submit(world, rules, UnitStrategyKind.Reinforce, 0);
        var state = world.BeginMutation(rules.Abilities);
        PersonnelTransitions.Capture(state, 11, 2, null, rules.Personnel, []);
        Assert.Empty(state.Simulation.Tasks);
        Assert.Equal(3000u, state.Settlements[1].DraftPopulation);
        Assert.Equal(10000u, state.Settlements[2].Soldiers);
        PersonnelTransitions.CancelMissions(state, 11, "repeat", [], rules.Personnel, true);
        Assert.Equal(3000u, state.Settlements[1].DraftPopulation);
    }

    [Fact]
    public void TerminalCleanupDemobilizesOutstandingTroopsOnlyOnce()
    {
        var rules = Rules();
        var world = World(friendly: true);
        Submit(world, rules, UnitStrategyKind.Reinforce, 0);
        var state = world.BeginMutation(rules.Abilities);
        state.SetSimulation(state.Simulation with { TerminalReason = "test-end" });
        List<DomainEvent> events = [];
        Assert.False(ResolutionPipeline.CheckTerminal(state, events));
        Assert.Empty(state.Simulation.Tasks);
        Assert.Equal(3000u, state.Settlements[1].DraftPopulation);
        Assert.False(ResolutionPipeline.CheckTerminal(state, events));
        Assert.Equal(3000u, state.Settlements[1].DraftPopulation);
    }

    [Fact]
    public void SaveRejectsMissingOrInvalidReinforcementOwnership()
    {
        var rules = Rules();
        var world = World(friendly: true);
        var task = Submit(world, rules, UnitStrategyKind.Reinforce, 0);
        var snapshot = world.CreateSnapshot();
        foreach (var invalid in new ReinforcementTroops?[]
            { null, new(3001, 70, 1), new(3000, 101, 1), new(3000, 70, 999) })
        {
            var changed = snapshot with { Simulation = snapshot.Simulation with
                { Tasks = [task with { Reinforcements = invalid }] } };
            Assert.Throws<ArgumentException>(() => WorldState.Restore(changed));
        }
        var node = System.Text.Json.Nodes.JsonNode.Parse(CanonicalJson.Text(task))!.AsObject();
        Assert.True(node.Remove("reinforcements"));
        Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<PersistentTask>(node.ToJsonString(), CanonicalJson.Options));
    }
}
