using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class FormationTeachingTests
{
    private static MilitaryRulebook Rules() => new(new(1, 1, [0], [new(0, 1, null)],
        [new(7, 10, 0, 10000, 5, new(1, 1), TravelMode.Land)
        { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true), Positions = San9.Tests.FormationTestData.Layout([BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle]) }], []));
    private static WorldState State(bool learning = true)
    {
        var skills = ImmutableSortedDictionary<int, int>.Empty;
        var world = WorldState.Create(new(190, 1, 1), 1, [new ForceState(1, 0) { LeaderId = 1 }],
            [new SettlementState(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, FormationProficiencies = skills.Add(7, 500) },
             new OfficerState(2, 1, 1, new(50, 50, 50, 50), false) { FormationProficiencies = skills },
             new OfficerState(3, 1, 1, new(50, 50, 50, 50), false) { FormationProficiencies = skills }],
            corps: [new CorpsState(1, 1, 1, new(0, 0)) { IsRoot = true }], newGame: new() { FormationLearningEnabled = learning });
        return world;
    }
    private static WorldMutation World(bool learning = true) => State(learning).BeginMutation();
    [Fact]
    public void DisabledFormationLearningRejectsTeaching()
    {
        var world = World(learning: false);
        var command = new StartFormationTeachingCommand(Guid.NewGuid(), 1, 0, 1, 1, 2, 7);
        Assert.Equal(CommandRejection.InvalidTask, FormationTeaching.Start(world, command, Rules(), []));
        Assert.Null(world.Officers[2].FormationTeaching);
    }
    [Fact]
    public void TeachingCompletesAfterThirtyDaysAndPreventsDuplicateAssignments()
    {
        var world = World(); var events = new List<DomainEvent>();
        var command = new StartFormationTeachingCommand(Guid.NewGuid(), 1, 0, 1, 1, 2, 7);
        Assert.Equal(CommandRejection.None, FormationTeaching.Start(world, command, Rules(), events));
        Assert.Equal(CommandRejection.InvalidTask, FormationTeaching.Start(world, command with { StudentId = 3 }, Rules(), events));
        for (int i = 0; i < 29; i++) FormationTeaching.AdvanceDay(world, events);
        Assert.False(FormationKnowledge.Knows(world.Officers[2], 7));
        Assert.Equal(1, world.Officers[2].FormationTeaching!.DaysRemaining);
        FormationTeaching.AdvanceDay(world, events);
        Assert.Equal(0, world.Officers[2].FormationProficiencies![7]);
        Assert.Null(world.Officers[2].FormationTeaching);
        Assert.Single(events.OfType<FormationTeachingCompletedEvent>());
    }
    [Fact]
    public void TeacherDepartureImmediatelyCancelsWithoutLearning()
    {
        var world = World(); var events = new List<DomainEvent>();
        var command = new StartFormationTeachingCommand(Guid.NewGuid(), 1, 0, 1, 1, 2, 7);
        Assert.Equal(CommandRejection.None, FormationTeaching.Start(world, command, Rules(), events));
        world.Replace(world.Officers[1] with { OwnerId = null, Status = OfficerStatus.Free });
        FormationTeaching.Reconcile(world, events);
        Assert.Null(world.Officers[2].FormationTeaching);
        Assert.False(FormationKnowledge.Knows(world.Officers[2], 7));
        Assert.Single(events.OfType<FormationTeachingCancelledEvent>());
    }

    [Theory]
    [InlineData("student-leaves")]
    [InlineData("settlement-changes-owner")]
    [InlineData("settlement-destroyed")]
    [InlineData("teacher-dies")]
    public void InvalidatedLessonCancelsWithoutCompleting(string cause)
    {
        var world = World(); var events = new List<DomainEvent>();
        var command = new StartFormationTeachingCommand(Guid.NewGuid(), 1, 0, 1, 1, 2, 7);
        Assert.Equal(CommandRejection.None, FormationTeaching.Start(world, command, Rules(), events));
        for (int i = 0; i < 29; i++) FormationTeaching.AdvanceDay(world, events);
        switch (cause)
        {
            case "student-leaves": world.Replace(world.Officers[2] with { SettlementId = null }); break;
            case "settlement-changes-owner": world.Replace(world.Settlements[1] with { OwnerId = null }); break;
            case "settlement-destroyed": world.Replace(world.Settlements[1] with { IsDestroyed = true }); break;
            case "teacher-dies": world.Replace(world.Officers[1] with { Status = OfficerStatus.Dead, OwnerId = null }); break;
        }
        FormationTeaching.AdvanceDay(world, events);
        Assert.Null(world.Officers[2].FormationTeaching);
        Assert.False(FormationKnowledge.Knows(world.Officers[2], 7));
        Assert.Single(events.OfType<FormationTeachingCancelledEvent>());
        Assert.Empty(events.OfType<FormationTeachingCompletedEvent>());
    }

    [Fact]
    public void StudentCannotStartAnotherLessonWithADifferentTeacher()
    {
        var world = World();
        world.Replace(world.Officers[3] with { FormationProficiencies = ImmutableSortedDictionary<int, int>.Empty.Add(7, 300) });
        var command = new StartFormationTeachingCommand(Guid.NewGuid(), 1, 0, 1, 1, 2, 7);
        Assert.Equal(CommandRejection.None, FormationTeaching.Start(world, command, Rules(), []));
        Assert.Equal(CommandRejection.InvalidTask, FormationTeaching.Start(world, command with { TeacherId = 3 }, Rules(), []));
        Assert.Equal(1, world.Officers[2].FormationTeaching!.TeacherId);
    }
    [Fact]
    public void LessonSurvivesWorldSnapshotAndContinuesFromRemainingDays()
    {
        var engine = new SimulationEngine(State(), new SimulationRules { Military = Rules() });
        var world = WorldState.Restore(engine.CaptureStableState());
        var first = world.BeginMutation();
        var command = new StartFormationTeachingCommand(Guid.NewGuid(), 1, 0, 1, 1, 2, 7);
        Assert.Equal(CommandRejection.None, FormationTeaching.Start(first, command, Rules(), []));
        for (int i = 0; i < 12; i++) FormationTeaching.AdvanceDay(first, []);
        world.Commit(first);
        var restored = WorldState.Restore(world.CreateSnapshot()).BeginMutation();
        Assert.Equal(18, restored.Officers[2].FormationTeaching!.DaysRemaining);
        for (int i = 0; i < 18; i++) FormationTeaching.AdvanceDay(restored, []);
        Assert.Equal(0, restored.Officers[2].FormationProficiencies![7]);
        Assert.Null(restored.Officers[2].FormationTeaching);
    }
}
