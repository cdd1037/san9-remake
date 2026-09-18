using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class StrategyDiplomacyTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void BarbarianClassificationDoesNotReplaceAllianceProtection(int kind)
    {
        var world = World();
        var engine = new SimulationEngine(world, Rules());
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Forces[2] with { IsBarbarian = true });
        world.Commit(mutation);
        string before = engine.Snapshot.StateHash;
        Assert.Equal(kind < 2, engine.Validate(new("player"), Command(kind)).Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        mutation = world.BeginMutation();
        mutation.Replace(mutation.Forces[2] with { IsBarbarian = false });
        mutation.SetSimulation(mutation.Simulation with { Alliance = new(3, 1, [1, 2], 36) });
        world.Commit(mutation);
        before = engine.Snapshot.StateHash;
        Assert.False(engine.Execute(new("player"), Command(kind)).Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void GrowthRequiresSuccessUsesStrictThreePercentGateAndStopsAtOneHundred(int kind)
    {
        foreach (var sample in new[] { (Roll: 0, Seed: 52UL, Ability: 99, Grows: true),
            (Roll: 0, Seed: 56UL, Ability: 99, Grows: false), (Roll: 99, Seed: 52UL, Ability: 99, Grows: false),
            (Roll: 0, Seed: 52UL, Ability: 100, Grows: false) })
        {
            // Independent SplitMix64 vectors: state 52 yields percentile 2; state 56 yields 3.
            Assert.Equal(sample.Seed == 52 ? 2UL : 3UL, new StableRandom(sample.Seed).NextUInt64() % 100);
            var rules = Rules();
            var world = World();
            var mutation = world.BeginMutation();
            mutation.Replace(mutation.Officers[11] with { BaseAbilities = new(sample.Ability, sample.Ability, 90, sample.Ability) });
            mutation.Replace(mutation.Settlements[2] with { Durability = 300, MaxDurability = 300 });
            if (kind >= 6)
            {
                mutation.Replace(mutation.Units[5] with { OwnerId = 1 });
                mutation.Replace(mutation.Officers[22] with { OwnerId = 1 });
            }
            world.Commit(mutation);
            var engine = new SimulationEngine(world, rules);
            GameCommand command = kind < 6 ? Command(kind) : new UnitStrategyCommand(Guid.NewGuid(), 1, 0, 11,
                kind == 6 ? UnitStrategyKind.Inspire : UnitStrategyKind.Reinforce, 5);
            Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
            mutation = world.BeginMutation(rules.Abilities);
            var task = Assert.Single(mutation.Simulation.Tasks);
            string ability = kind is 2 or 3 ? "war" : kind == 7 ? "leadership" : "intelligence";
            string stream = "strategy-" + ability + "-growth";
            mutation.SetSimulation(mutation.Simulation with
            {
                Tasks = [task with { Journey = task.Journey! with { SuccessRoll = sample.Roll } }],
                RandomStreams = [new(stream, sample.Seed)]
            });
            List<DomainEvent> events = [];
            // All targets are within two daily command stages; no intervening automatic planner is involved.
            for (int day = 0; day < 2; day++) new ResolutionPipeline(rules).AdvanceDay(mutation, events);
            var growth = events.OfType<OfficerAbilityGrownEvent>().Where(e => e.OfficerId == 11).ToArray();
            if (sample.Grows)
            {
                var actual = Assert.Single(growth);
                Assert.Equal(ability, actual.Ability);
                Assert.Equal(99, actual.Previous);
                Assert.Equal(100, actual.Current);
            }
            else Assert.Empty(growth);
            Assert.Equal(2, mutation.Officers[11].TacticProficiencies[7]);
            Assert.Equal(2, mutation.Officers[11].TacticProficiencies[8]);
            if (sample.Roll == 99 || sample.Ability >= 100)
                Assert.Equal(sample.Seed, mutation.Simulation.RandomStreams.Single(s => s.Name == stream).State);
            else
            {
                var random = new StableRandom(sample.Seed);
                random.NextUInt64();
                Assert.Equal(random.State, mutation.Simulation.RandomStreams.Single(s => s.Name == stream).State);
            }
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void OmittedZeroRelationsAndExplicitZerosHaveIdenticalEligibilityAndEffects(int kind)
    {
        var initial = new SimulationEngine(World(), Rules()).Snapshot;
        var sparse = WorldState.Restore(initial with { Relations = [] });
        var explicitZeros = WorldState.Restore(initial with
        { Relations = initial.Relations.Select(r => r with { Value = 0 }).ToImmutableArray() });
        var rules = Rules();
        var command = Command(kind);
        IReadOnlyList<DomainEvent> Run(WorldState world)
        {
            var engine = new SimulationEngine(world, rules);
            Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
            var mutation = world.BeginMutation();
            var task = Assert.Single(mutation.Simulation.Tasks);
            mutation.SetSimulation(mutation.Simulation with { Tasks = [task with
                { Journey = task.Journey! with { SuccessRoll = 0 } }] });
            world.Commit(mutation);
            engine.EndControl(new("player"));
            return engine.AdvanceXun().Events;
        }
        var a = Run(sparse);
        var b = Run(explicitZeros);
        Assert.Equal(CanonicalJson.Text(a), CanonicalJson.Text(b));
        Assert.DoesNotContain(a, e => e is TaskCancelledEvent);
        Assert.Equal(CanonicalJson.Text(sparse.CreateSnapshot().Officers), CanonicalJson.Text(explicitZeros.CreateSnapshot().Officers));
    }

    private static SimulationRules Rules() => new()
    {
        Personnel = new([new(1, new(2, 2)), new(2, new(32, 2)), new(3, new(62, 2))],
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }, [new(1, 2), new(2, 1)]),
        Military = new(new(20, 3, Enumerable.Repeat(0, 60).ToImmutableArray(), [new(0, 1, null)],
            [new(9, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 2), new(1, 1)), new(2, new(32, 2), new(1, 1)), new(3, new(62, 2), new(1, 1))])
            { RegionIds = Enumerable.Repeat<int?>(2, 60).ToImmutableArray() })
    };

    private static WorldState World() => WorldState.Create(new(190, 1, 1), 42,
        [new ForceState(1, 10000, 10000, 100) { LeaderId = 11 },
            new ForceState(2, 10000, 10000, 100) { LeaderId = 21 },
            new ForceState(3, 10000, 10000, 100) { LeaderId = 31 }],
        Enumerable.Range(1, 3).Select(id => new SettlementState(id, id, 10000, 100, 100, 70, 500, 0, 100, 0, 100)),
        [new OfficerState(11, 1, 1, new(100, 100, 100, 100), false) { Status = OfficerStatus.Ruler },
            new OfficerState(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
            new OfficerState(22, 2, null, new(50, 50, 50, 50), false) { UnitId = 5 }.WithLoyalty(2, 80),
            new OfficerState(31, 3, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
        units: [new UnitState(5, 2, new(37, 2), 5000) { FormationId = 9, CommanderId = 22, Morale = 70 }],
        relations: [new(1, 2, 30), new(1, 3, 30), new(2, 3, 80)],
        controls: [new("player", 1, ControllerKind.Player)]);

    private static GameCommand Command(int kind) => kind switch
    {
        0 => new UnitStrategyCommand(Guid.NewGuid(), 1, 0, 11, UnitStrategyKind.FalseReport, 5),
        1 => new UnitStrategyCommand(Guid.NewGuid(), 1, 0, 11, UnitStrategyKind.Disorder, 5),
        2 => new BurnCommand(Guid.NewGuid(), 1, 0, 11, 2),
        3 => new SeizeCommand(Guid.NewGuid(), 1, 0, 11, 2),
        4 => new AlienateCommand(Guid.NewGuid(), 1, 0, 11, 22),
        5 => new GossipCommand(Guid.NewGuid(), 1, 0, 11, 2, 3),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void EitherDirectionOfRequestProtectsAllHostileStrategyTargets(int kind)
    {
        foreach (bool reverse in new[] { false, true })
        {
            var world = World();
            var engine = new SimulationEngine(world, Rules());
            var command = Command(kind);
            Assert.True(engine.Validate(new("player"), command).Accepted);
            var mutation = world.BeginMutation();
            AttackRequests.Establish(mutation, reverse ? 2 : 1, reverse ? 1 : 2, 3, []);
            world.Commit(mutation);
            string before = engine.Snapshot.StateHash;
            Assert.False(engine.Execute(new("player"), command).Submission.Accepted);
            Assert.Equal(before, engine.Snapshot.StateHash);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void RequestAgreedDuringTravelCancelsHostileEffectAndCompletesReturn(int kind)
    {
        var rules = Rules();
        var world = World();
        var engine = new SimulationEngine(world, rules);
        Assert.True(engine.Execute(new("player"), Command(kind)).Submission.Accepted);
        var mutation = world.BeginMutation();
        AttackRequests.Establish(mutation, 2, 1, 3, []);
        world.Commit(mutation);
        engine.EndControl(new("player"));
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<TaskCancelledEvent>());
        Assert.DoesNotContain(result.Events, e => e is UnitStrategyResolvedEvent or BurnResolvedEvent or SeizeResolvedEvent or AlienateResolvedEvent or GossipResolvedEvent);
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(1, engine.Snapshot.Officers.Single(p => p.Id == 11).SettlementId);
        Assert.Equal(UnitCondition.Normal, engine.Snapshot.Units.Single().Condition);
        Assert.Equal(30, engine.Snapshot.Relations.Single(r => r.FirstId == 1 && r.SecondId == 2).Value);
    }
}
