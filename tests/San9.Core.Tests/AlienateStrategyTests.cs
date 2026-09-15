using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class AlienateStrategyTests
{
    private static SimulationRules Rules() => new()
    {
        Personnel = new([new(1, new(2, 2)), new(2, new(32, 2)), new(3, new(62, 2))]),
        Military = new(new(200, 3, Enumerable.Repeat(0, 600).ToImmutableArray(), [new(0, 1, null)],
            [new(9, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 2), new(1, 1)), new(2, new(32, 2), new(1, 1)), new(3, new(62, 2), new(1, 1))])
            { RegionIds = Enumerable.Repeat<int?>(2, 600).ToImmutableArray() })
    };

    private static WorldState World(ulong seed = 42, bool fieldTarget = false) => WorldState.Create(new(190, 1, 1), seed,
        [new(1, 10000, 10000, 100), new(2, 10000, 10000, 100), new(3, 10000, 10000, 100)],
        [new(1, 1, 10000, 100, 100, 70, 500, 0, 100, 0, 100),
         new(2, 2, 10000, 100, 100, 70, 500, 0, 100, 0, 100),
         new(3, 2, 10000, 100, 100, 70, 500, 0, 100, 0, 100)],
        [new OfficerState(11, 1, 1, new(70, 100, 70, 70), false)
            { ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 100) },
         new OfficerState(21, 2, fieldTarget ? null : 2, new(70, 50, 70, 70), false)
            { UnitId = fieldTarget ? 5 : null, Righteousness = 5,
              ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(2, 80) }],
        units: fieldTarget ? [new UnitState(5, 2, new(32, 2), 5000)
            { FormationId = 9, Morale = 70, CommanderId = 21, MemberOrder = [21], OrderClass = UnitOrderClass.Military }] : [],
        relations: [new(1, 2, 50), new(1, 3, 30), new(2, 3, 80)],
        controls: [new("player", 1, ControllerKind.Player)]);

    private static PersistentTask Submit(WorldState world, SimulationRules rules, int roll)
    {
        var engine = new SimulationEngine(world, rules);
        var command = new AlienateCommand(Guid.NewGuid(), 1, world.Simulation.NextSequence, 11, 21);
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
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
    [InlineData(39, true)]
    [InlineData(40, false)]
    public void StrictThresholdChangesLoyaltyOnlyOnSuccessAndAlwaysPaysRelationCost(int roll, bool success)
    {
        var rules = Rules();
        var world = World();
        Submit(world, rules, roll);
        var state = world.BeginMutation(rules.Abilities);

        var events = Day(state, rules);

        var result = Assert.Single(events.OfType<AlienateResolvedEvent>());
        Assert.Equal(40, result.Threshold);
        Assert.Equal(success, result.Success);
        Assert.Equal(success ? 80 - result.LoyaltyLoss : 80, state.Officers[21].Loyalty);
        Assert.InRange(result.LoyaltyLoss, success ? 3 : 0, success ? 5 : 0);
        if (success)
        {
            Assert.Equal(50, state.Relations[(1, 2)].Value);
            Assert.InRange(state.Relations[(2, 3)].Value, 77, 79);
        }
        else
        {
            Assert.InRange(state.Relations[(1, 2)].Value, 47, 49);
            Assert.Equal(80, state.Relations[(2, 3)].Value);
        }
        Assert.Equal(success ? 3 : 1, result.RelationForceId);
        Assert.Equal(2, state.Officers[11].TacticProficiencies[7]);
        Assert.Equal(2, state.Officers[11].TacticProficiencies[8]);
        Assert.Single(state.Simulation.Tasks, task => task.Kind == PersonnelReturns.Kind);
    }

    [Fact]
    public void SubmissionAndAdviceAreReadOnlyUntilDailyArrivalAndPayloadRoundTrips()
    {
        var rules = Rules();
        var world = World();
        var engine = new SimulationEngine(world, rules);
        string before = engine.Snapshot.StateHash;
        Assert.Equal(AdviserOpinion.NoAdviser, engine.GetAlienateAdvice(1, 11, 21).Opinion);
        Assert.Equal(before, engine.Snapshot.StateHash);

        var task = Submit(world, rules, 0);
        Assert.Equal(80, world.BeginMutation().Officers[21].Loyalty);
        Assert.Equal(2, task.Journey!.DestinationPlaceId);
        string json = CanonicalJson.Text(world.CreateSnapshot());
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<WorldSnapshot>(json, CanonicalJson.Options)!;
        var restored = new SimulationEngine(WorldState.Restore(snapshot), rules);
        Assert.Equal(world.CreateSnapshot().StateHash, restored.Snapshot.StateHash);

        Assert.Single(Day(world.BeginMutation(rules.Abilities), rules).OfType<AlienateResolvedEvent>());
    }

    [Fact]
    public void LiveAbilityIsRevalidatedButMovingToAnotherValidPlaceDoesNotCancel()
    {
        var rules = Rules();
        var world = World();
        Submit(world, rules, 20);
        var state = world.BeginMutation(rules.Abilities);
        state.Replace(state.Officers[21] with { BaseAbilities = new(70, 100, 70, 70) });
        var resolved = Assert.Single(Day(state, rules).OfType<AlienateResolvedEvent>());
        Assert.Equal(10, resolved.Threshold);
        Assert.False(resolved.Success);

        world = World();
        Submit(world, rules, 0);
        state = world.BeginMutation(rules.Abilities);
        state.Replace(state.Officers[21] with { SettlementId = 3 });
        var moved = Assert.Single(Day(state, rules).OfType<AlienateResolvedEvent>());
        Assert.True(moved.Success);
        Assert.True(state.Officers[21].Loyalty < 80);
        Assert.Equal(3, moved.RelationForceId);
    }

    [Fact]
    public void SuccessFallsBackToActorForceWhenNoOtherRegularForceCanReceiveTheRelationEffect()
    {
        var rules = Rules();
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Forces[3] with { IsBarbarian = true });
        world.Commit(mutation);
        Submit(world, rules, 0);
        var state = world.BeginMutation(rules.Abilities);

        var result = Assert.Single(Day(state, rules).OfType<AlienateResolvedEvent>());

        Assert.True(result.Success);
        Assert.Equal(1, result.RelationForceId);
        Assert.InRange(state.Relations[(1, 2)].Value, 47, 49);
        Assert.Equal(80, state.Relations[(2, 3)].Value);
    }

    [Fact]
    public void FieldUnitOfficerUsesTheSubmissionPositionWithoutBecomingLocationLocked()
    {
        var rules = Rules();
        var world = World(fieldTarget: true);

        var task = Submit(world, rules, 0);

        Assert.Equal(2, task.Journey!.DestinationPlaceId);
        Assert.Equal(new WorldPosition(32, 2), task.Journey.DestinationPosition);
        var state = world.BeginMutation(rules.Abilities);
        state.Replace(state.Units[5] with { Position = new(62, 2) });
        Assert.Single(Day(state, rules).OfType<AlienateResolvedEvent>());
    }

    [Theory]
    [InlineData(OfficerStatus.Ruler, false, 80)]
    [InlineData(OfficerStatus.Serving, true, 80)]
    [InlineData(OfficerStatus.Serving, false, 0)]
    public void InvalidTargetIdentityIsRejected(OfficerStatus status, bool barbarian, int loyalty)
    {
        var rules = Rules();
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[21].WithLoyalty(2, loyalty) with { Status = status });
        mutation.Replace(mutation.Forces[2] with { IsBarbarian = barbarian });
        world.Commit(mutation);
        var engine = new SimulationEngine(world, rules);

        Assert.Equal(CommandRejection.InvalidTask, engine.GetAlienateAdvice(1, 11, 21).Rejection);
        Assert.False(engine.Execute(new("player"),
            new AlienateCommand(Guid.NewGuid(), 1, 0, 11, 21)).Submission.Accepted);
    }
}
