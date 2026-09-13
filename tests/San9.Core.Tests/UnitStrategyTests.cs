using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class UnitStrategyTests
{
    [Fact]
    public void AdviceIsStableDoesNotAdvanceRandomnessAndChangesWithRelevantAbility()
    {
        var rules = Rules();
        var world = World(friendly: true);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Forces[1] with { AdviserId = 12 });
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

    private static SimulationRules Rules(bool defensive = false, bool immune = false) => new()
    {
        Personnel = new([new(1, new(2, 2)), new(2, new(997, 12))]),
        Military = new(new(200, 3, Enumerable.Repeat(0, 600).ToImmutableArray(), [new(0, 1, null)],
            [new(9, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)
                { HalvesHostileStrategyChance = defensive, ImmuneToDisorder = immune }],
            [new(1, new(2, 2), new(1, 1)), new(2, new(997, 12), new(1, 1))])
            { RegionIds = Enumerable.Repeat<int?>(2, 600).ToImmutableArray() })
    };

    private static WorldState World(bool friendly = false, int distance = 31, int intelligence = 99) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 10000, 10000, 100), new(2, 10000, 10000, 100)],
        [new(1, 1, 10000, 100, 100, 70, 500, 0, 100, 0, 100), new(2, 2, 10000, 100, 100, 70, 500, 0, 100, 0, 100)],
        [new(11, 1, 1, new(70, intelligence, 70, 70), false), new(12, 1, 1, new(70, 70, 70, 70), false),
         new(22, friendly ? 1 : 2, null, new(70, 70, 70, 80), false) { UnitId = 5, TacticCooldown = 20 }],
        units: [new(5, friendly ? 1 : 2, new(2 + distance, 2), 5000) { FormationId = 9, Morale = 70,
            CommanderId = 22, MemberOrder = [22], OrderClass = UnitOrderClass.Military }],
        relations: [new(1, 2, 50)], controls: [new("player", 1, ControllerKind.Player)]);

    private static PersistentTask Submit(WorldState world, SimulationRules rules, UnitStrategyKind strategy, int? forcedRoll = null)
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

    private static List<DomainEvent> Day(WorldMutation mutation, SimulationRules rules)
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
        Assert.Equal(new WorldPosition(2 + distance, 2), task.Journey!.DestinationPosition);
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
        Assert.True(Assert.Single(events.OfType<UnitStrategyResolvedEvent>()).Success);
        var unit = Assert.Single(state.Units.Values);
        Assert.Equal(UnitCondition.HighSpirits, unit.Condition);
        Assert.Equal(11, unit.ConditionDaysRemaining);
        Assert.Equal(89, unit.Morale); // The first daily stage already applied the ordinary field morale loss.
        Assert.True(unit.HighSpiritsMoralePenaltyPending);
        Assert.Equal(0, state.Officers.Values.Single(p => p.Id == 22).TacticCooldown);
        var returning = Assert.Single(state.Simulation.Tasks);
        Assert.Equal("personnel-return", returning.Kind);
        Assert.Equal(new WorldPosition(33, 2), returning.Journey!.ReturnLeg!.OriginPosition);
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
}
