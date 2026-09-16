using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class StrategyDiplomacyTests
{
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
        units: [new UnitState(5, 2, new(37, 2), 5000) { FormationId = 9, CommanderId = 22, MemberOrder = [22], Morale = 70 }],
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
