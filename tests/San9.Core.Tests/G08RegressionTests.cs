using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class G08RegressionTests
{
    private static WorldState World() => WorldState.Create(new(190, 1, 1), 42,
        [new ForceState(1, 30000, 30000, 100) { LeaderId = 11, AdviserId = 12 },
         new ForceState(2, 30000, 30000, 100) { LeaderId = 21 },
         new ForceState(3, 30000, 30000, 100) { LeaderId = 31 }],
        [new(1, 1, 10000, 300, 300, 70, 0, 0, 0, 0, 0),
         new(2, 2, 10000, 300, 300, 70, 0, 0, 0, 0, 0),
         new(3, 3, 10000, 300, 300, 70, 0, 0, 0, 0, 0)],
        [new OfficerState(11, 1, 1, new(100, 100, 100, 100), false) { Status = OfficerStatus.Ruler },
         new OfficerState(12, 1, 1, new(50, 100, 50, 50), false) { Status = OfficerStatus.Strategist },
         new OfficerState(21, 2, 2, new(50, 40, 50, 50), false) { Status = OfficerStatus.Ruler },
         new OfficerState(22, 2, 2, new(50, 0, 50, 50), false).WithLoyalty(2, 80),
         new OfficerState(23, 2, 3, new(50, 0, 50, 50), false) { Status = OfficerStatus.Captive }.WithLoyalty(2, 1),
         new OfficerState(31, 3, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
        controls: [new("first", 1, ControllerKind.Player), new("second", 2, ControllerKind.Automatic), new("third", 3, ControllerKind.Automatic)]);

    [Fact]
    public void GossipSelectsServingOfficerAndLeavesCaptiveLoyaltyUntouched()
    {
        var rules = new SimulationRules { Personnel = new([new(1, new(0, 0)), new(2, new(30, 0)), new(3, new(60, 0))]) };
        var world = World().BeginMutation();
        List<DomainEvent> events = [];
        Assert.Equal(CommandRejection.None, GossipStrategies.Start(world, new("first"), new(Guid.NewGuid(), 1, 0, 11, 2, 3), rules, events));
        var task = world.Simulation.Tasks.Single();
        task = task with { Journey = task.Journey! with { SuccessRoll = 0 } };
        GossipStrategies.Resolve(world, task, rules.Personnel, events);
        var result = Assert.Single(events.OfType<GossipResolvedEvent>());
        Assert.True(result.Success);
        Assert.Equal(22, result.TargetOfficerId);
        Assert.Equal(1, world.Officers[23].Loyalty);
        Assert.InRange(world.Officers[22].Loyalty, 76, 77);
    }

    [Fact]
    public void CaptiveDoesNotSatisfyGossipServingMemberRequirement()
    {
        var rules = new SimulationRules { Personnel = new([new(1, new(0, 0)), new(2, new(30, 0)), new(3, new(60, 0))]) };
        var world = World().BeginMutation();
        world.Replace(world.Officers[22] with { Status = OfficerStatus.Captive, SettlementId = 3 });
        Assert.Equal(CommandRejection.InvalidTask, GossipStrategies.Start(world, new("first"),
            new(Guid.NewGuid(), 1, 0, 11, 2, 3), rules, []));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(35)]
    [InlineData(40)]
    [InlineData(50)]
    public void PerfectAdviserPredictsCurrentFixedSuccessGateAtLowProbabilities(int chance)
    {
        bool favorable = false, unfavorable = false;
        for (ulong seed = 0; seed < 300; seed++)
        {
            var world = World().BeginMutation();
            world.SetSimulation(world.Simulation with { InitialSeed = seed });
            int roll = ConditionalRandom.Percentile(seed, ConditionalRandomPurpose.Burn, world.Clock, 11, 2);
            var advice = AdviserJudgment.Evaluate(world, 1, ConditionalRandomPurpose.Burn, 11, 2, chance);
            Assert.Equal(roll < chance ? AdviserOpinion.Favorable : AdviserOpinion.Unfavorable, advice.Opinion);
            favorable |= advice.Opinion == AdviserOpinion.Favorable;
            unfavorable |= advice.Opinion == AdviserOpinion.Unfavorable;
        }
        Assert.True(favorable);
        Assert.True(unfavorable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70)]
    [InlineData(90)]
    [InlineData(100)]
    [InlineData(200)]
    public void AdviceUsesSameDrawForAccuracyAndSuccessWithoutChangingRandomState(int intelligence)
    {
        var world = World().BeginMutation();
        world.Replace(world.Officers[12] with { BaseAbilities = new(50, intelligence, 50, 50) });
        int accuracy = Math.Min(100, 60 + Math.Max(0, intelligence - 70) + Math.Max(0, intelligence - 90));
        for (ulong seed = 0; seed < 100; seed++)
        {
            world.SetSimulation(world.Simulation with { InitialSeed = seed });
            var before = world.Simulation;
            int roll = ConditionalRandom.Percentile(seed, ConditionalRandomPurpose.Gossip, world.Clock, 11, 2, 3);
            var advice = AdviserJudgment.Evaluate(world, 1, ConditionalRandomPurpose.Gossip, 11, 2, 3, 40);
            Assert.Equal((roll < 40) == (roll < accuracy) ? AdviserOpinion.Favorable : AdviserOpinion.Unfavorable, advice.Opinion);
            Assert.Equal(before, world.Simulation);
        }
    }

    [Fact]
    public void InterruptedDiplomacyUsesActualRoundedOutboundCountdown()
    {
        var rules = new SimulationRules { Personnel = new([new(1, new(0, 0)), new(2, new(330, 0)), new(3, new(690, 0))]) };
        var world = World().BeginMutation();
        List<DomainEvent> events = [];
        Assert.Equal(CommandRejection.None, GiftDiplomacy.Start(world, new("first"),
            new(Guid.NewGuid(), 1, 0, 11, 2, new(GiftKind.Money, 100, null)), rules, events));
        var task = world.Simulation.Tasks.Single() with { ProgressDays = 1 };
        Assert.Equal(20, task.Due.DayNumber - task.Started.DayNumber);
        world.SetClock(world.Clock.NextDay());
        world.Replace(world.Settlements[1] with { OwnerId = 2 });
        world.Replace(world.Settlements[3] with { OwnerId = 1 });
        world.SetSimulation(world.Simulation with { Tasks = [] });
        PersonnelReturns.Start(world, task, rules.Personnel, events, false);
        // D=12, Q=20, R=19 => 1 day; this is an interrupted outbound journey.
        Assert.Equal(1, world.Simulation.Tasks.Single().Journey!.ReturnLeg!.CountdownDays);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildingStrategyJourneyFreezesLiveFacilityCoordinates(bool seize)
    {
        var definition = new MilitaryDefinition(200, 3, Enumerable.Repeat(0, 600).ToImmutableArray(),
            [new(0, 1, null)], [], [new(1, new(0, 0), new(1, 1)) { FacilityTypeId = 1 },
                new(2, new(30, 0), new(1, 1)) { FacilityTypeId = 1 }])
        {
            Facilities = [new(1, 100, 300, new(1, 1), 0, true, true, true, 10000, null, null, null, [])]
        };
        var rules = new SimulationRules
        {
            Personnel = new([new(1, new(0, 0)), new(2, new(30, 0)), new(3, new(60, 0))],
                new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }, [new(1, 2)]),
            Military = new(definition)
        };
        var world = World().BeginMutation();
        world.Replace(world.Settlements[2] with { Kind = SettlementKind.Facility, Facility = new(1, new(330, 0)) });
        List<DomainEvent> events = [];
        Assert.Equal(new WorldPosition(330, 0), rules.Military.Place(world.Settlements[2]).Position);
        Assert.Equal(CommandRejection.None, seize
            ? SeizeStrategies.Start(world, new("first"), new(Guid.NewGuid(), 1, 0, 11, 2), rules, events)
            : BurnStrategies.Start(world, new("first"), new(Guid.NewGuid(), 1, 0, 11, 2), rules, events));
        var task = world.Simulation.Tasks.Single();
        Assert.Equal(new WorldPosition(330, 0), task.Journey!.Route.Destination);
        Assert.Equal(10, task.Due.DayNumber - task.Started.DayNumber);
        // Rebuilding either endpoint must not rewrite or invalidate the saved outbound journey.
        world.Replace(world.Settlements[2] with { Facility = new(1, new(600, 0)) });
        world.Replace(world.Settlements[1] with { Kind = SettlementKind.Facility, Facility = new(1, new(60, 0)) });
        TaskRule rule = seize ? new SeizeRule(rules) : new BurnRule(rules);
        Assert.True(rule.HasValidPayload(task));
        Assert.Equal(10, rule.DurationDays(task));
        task = task with { ProgressDays = 11 };
        world.SetSimulation(world.Simulation with { Tasks = [] });
        PersonnelReturns.Start(world, task, rules.Personnel, events, false);
        var returning = Assert.Single(world.Simulation.Tasks);
        Assert.Equal(new WorldPosition(330, 0), returning.Journey!.Route.Origin);
        Assert.Equal(new WorldPosition(60, 0), returning.Journey.Route.Destination);
        Assert.Equal(9, returning.Journey.ReturnLeg!.CountdownDays);
        world.Replace(world.Settlements[1] with { Facility = new(1, new(300, 0)) });
        Assert.True(new PersonnelReturnRule(rules.Personnel, rules.Military).Validate(world, returning));
    }
}
