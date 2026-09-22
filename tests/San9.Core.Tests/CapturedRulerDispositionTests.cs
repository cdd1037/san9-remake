using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class CapturedRulerDispositionTests
{
    private static (WorldMutation World, SimulationRules Rules) Fixture(ControllerKind control = ControllerKind.Automatic)
    {
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 10000, 100000, 100) { LeaderId = 11 }, new(2, 10000, 100000, 1000) { LeaderId = 21, BaseTrust = 1000 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(11, 1, 2, new(100, 100, 100, 100), false) { Status = OfficerStatus.Captive, Righteousness = 15, BloodlineId = 7 },
             new(12, 1, 1, new(50, 50, 50, 50), false),
             new(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
            controls: [new("captor", 2, control)]);
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { ActiveControl = mutation.Simulation.Controls.Length });
        return (mutation, new SimulationRules { Personnel = new([new(1, new(2, 2)), new(2, new(12, 2))]) });
    }

    [Fact]
    public void AutomaticZeroChanceStillDrawsAndReleasesBeforeLateReturns()
    {
        var (world, rules) = Fixture();
        int loyalty = world.Officers[11].LoyaltyTo(2);
        List<DomainEvent> events = [];
        new ResolutionPipeline(rules).AdvanceDay(world, events);
        Assert.Equal(OfficerStatus.Ruler, world.Officers[11].Status);
        Assert.Equal(loyalty + 3, world.Officers[11].LoyaltyTo(2));
        Assert.Contains(world.Simulation.RandomStreams, s => s.Name == "captured-ruler-disposition");
        var task = Assert.Single(world.Simulation.Tasks);
        Assert.Equal(world.Clock, task.Started);
        Assert.False(task.Journey!.ReturnLeg!.AfterDailyCommands);
        Assert.Equal(task.Started.DayNumber + task.Journey.ReturnLeg.CountdownDays - 1, task.Due.DayNumber);
        Assert.Single(events.OfType<PrisonerReleasedEvent>());
        Assert.Contains(events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.UnitAndBuildingEvents);
    }

    [Theory]
    [InlineData(19, 99, 0, 20)]
    [InlineData(20, 100, 0, 18)]
    [InlineData(1000, 100, 100, 0)]
    public void ChanceUsesBaseTrustCurrentBestAbilityAndSeparateIntegerDivisions(int trust, int ability, int relation, int expected)
    {
        var (world, _) = Fixture();
        world.Replace(world.Forces[2] with { BaseTrust = trust });
        world.Replace(world.Officers[11] with { BaseAbilities = new(1, 1, ability, 1) });
        world.SetRelation(new(1, 2, relation));
        Assert.Equal(expected, CapturedRulerDisposition.ExecutionChance(world, 2, world.Officers[11]));
        world.Replace(world.Officers[11] with { Injury = 5 });
        Assert.True(CapturedRulerDisposition.ExecutionChance(world, 2, world.Officers[11]) >= expected);
    }

    [Theory]
    [InlineData(false, false, 45)]
    [InlineData(true, false, 40)]
    [InlineData(false, true, 40)]
    [InlineData(true, true, 35)]
    public void DailyExecutionCapsLoyaltyThenAppliesBothKinshipDirections(bool bloodline, bool parent, int expected)
    {
        var (world, rules) = Fixture();
        world.Replace(world.Officers[12].WithLoyalty(2, 90) with { BloodlineId = bloodline ? 7 : null });
        world.Replace(world.Officers[11] with { ParentId = parent ? 12 : null });
        world.SetClock(world.Clock.NextDay());
        List<DomainEvent> events = [];
        CapturedRulerDisposition.Resolve(world, rules, events, new(new(190, 1, 1), 11, 2), CapturedRulerAction.Execute);
        Assert.Equal(OfficerStatus.Dead, world.Officers[11].Status);
        Assert.Equal(12, world.Forces[1].LeaderId);
        Assert.Equal(expected, world.Officers[12].LoyaltyTo(2));
        Assert.DoesNotContain(events, e => e is ForceRelationChangedEvent);
        Assert.DoesNotContain(world.Simulation.RandomStreams, s => s.Name == "personnel-execution-relations");
    }

    [Fact]
    public void PlayerHeirWaitResumesSameDayWithoutRepeatingDisposition()
    {
        var (world, rules) = Fixture();
        world.SetSimulation(world.Simulation with { Controls = [new("victim", 1, ControllerKind.Player), new("captor", 2, ControllerKind.Automatic)], ActiveControl = 2 });
        world.SetClock(world.Clock.NextDay());
        List<DomainEvent> events = [];
        CapturedRulerDisposition.Resolve(world, rules, events, new(new(190, 1, 1), 11, 2), CapturedRulerAction.Execute);
        Assert.NotNull(world.Simulation.PendingExecutionSuccession);
        Assert.Equal(OfficerStatus.Captive, world.Officers[11].Status);
        PrisonerExecution.ValidatePending(world);
        Assert.Equal(CommandRejection.None, PrisonerExecution.Respond(world, new(Guid.NewGuid(), 1, 0, 11, 12), rules, events));
        new ResolutionPipeline(rules).FinishUnitAndBuildingEvents(world, events, new(190, 1, 1), 11);
        Assert.Null(world.Simulation.PendingExecutionSuccession);
        Assert.Single(events.OfType<PrisonerExecutedEvent>());
        Assert.Single(events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.UnitAndBuildingEvents);
    }
    [Theory]
    [InlineData(19, true)]
    [InlineData(20, false)]
    public void AutomaticRollUsesStrictPercentageBoundary(int roll, bool execute)
    {
        var (world, rules) = Fixture();
        world.Replace(world.Forces[2] with { BaseTrust = 0 });
        for (ulong seed = 1; ; seed++)
        {
            var random = new StableRandom(seed);
            if (random.NextUInt64() % 100 != (ulong)roll) continue;
            world.SetSimulation(world.Simulation with { RandomStreams = [new("captured-ruler-disposition", seed)] });
            break;
        }
        List<DomainEvent> events = [];
        new ResolutionPipeline(rules).AdvanceDay(world, events);
        Assert.Equal(execute ? OfficerStatus.Dead : OfficerStatus.Ruler, world.Officers[11].Status);
        Assert.Equal(execute ? 1 : 0, events.OfType<PrisonerExecutedEvent>().Count());
        Assert.False(world.Simulation.BattleDeathsEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryAndUnaffiliatedPrisonersDoNotEnterRulerDisposition(bool unaffiliated)
    {
        var (world, rules) = Fixture(ControllerKind.Player);
        world.Replace(world.Forces[1] with { LeaderId = 12 });
        world.Replace(world.Officers[11] with { OwnerId = unaffiliated ? null : 1 });
        new ResolutionPipeline(rules).AdvanceDay(world, []);
        Assert.Null(world.Simulation.PendingCapturedRuler);
        Assert.Equal(OfficerStatus.Captive, world.Officers[11].Status);
        Assert.DoesNotContain(world.Simulation.RandomStreams, s => s.Name == "captured-ruler-disposition");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void ZeroDistanceReleaseCompletesInSameLatePhaseIncludingXunBoundary(int day)
    {
        var (world, _) = Fixture();
        var rules = new SimulationRules { Personnel = new([new(1, new(2, 2)), new(2, new(2, 2))]) };
        world.SetClock(new(190, 1, day));
        List<DomainEvent> events = [];
        new ResolutionPipeline(rules).AdvanceDay(world, events, new(190, 1, 1));
        Assert.Empty(world.Simulation.Tasks);
        var returned = Assert.Single(events.OfType<PersonnelReturnedEvent>());
        Assert.Equal(world.Clock, returned.Clock);
        Assert.True(events.FindIndex(e => e is PrisonerReleasedEvent) < events.FindIndex(e => e is PersonnelReturnedEvent));
        Assert.Single(events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.UnitAndBuildingEvents);
    }

    [Fact]
    public void FieldCaptiveReturnsFromActualUnitPositionAndRetainsValidTask()
    {
        var state = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 10000, 100000, 100) { LeaderId = 11 }, new(2, 10000, 100000, 1000) { LeaderId = 21, BaseTrust = 1000 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(11, 1, null, new(100, 100, 100, 100), false) { Status = OfficerStatus.Captive, UnitId = 10 },
             new(12, 1, 1, new(50, 50, 50, 50), false), new(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
            units: [new(10, 2, new(37, 7), 100)]);
        var world = state.BeginMutation();
        world.SetClock(world.Clock.NextDay());
        var places = new PersonnelRulebook([new(1, new(2, 7)), new(2, new(12, 7))]);
        var military = new MilitaryRulebook(new(8, 3,
            System.Collections.Immutable.ImmutableArray.CreateRange(Enumerable.Repeat(0, 24)), [new(0, 1, null)],
            [new(7, 1, 50, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 7), new(1, 1)), new(2, new(12, 7), new(1, 1))]));
        var rules = new SimulationRules { Personnel = places, Military = military };
        CapturedRulerDisposition.Apply(world, rules, [], new(190, 1, 1));
        var task = Assert.Single(world.Simulation.Tasks);
        Assert.Equal(new WorldPosition(37, 7), task.Journey!.Route.Origin);
        Assert.Equal(2, task.Journey.ReturnLeg!.CountdownDays);
        Assert.True(task.Journey.ReturnLeg.FieldTravel);
        Assert.True(rules.FindTask(task.Kind)!.Validate(world, task));
        Assert.Null(world.Officers[11].UnitId);
        Assert.Equal(11, world.Forces[1].LeaderId);
        Assert.True(world.Units.ContainsKey(10));
    }
}
