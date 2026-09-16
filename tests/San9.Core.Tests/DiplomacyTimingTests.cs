using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class DiplomacyTimingTests
{
    private static SimulationRules Rules(int distance) => new()
    {
        Personnel = new([new(1, new(0, 0)), new(2, new(distance, 0)), new(3, new(900, 0))])
    };

    private static WorldState World(GameClock clock) => WorldState.Create(clock, 42,
        [new ForceState(1, 10000, 10000, 100) { LeaderId = 11 },
         new ForceState(2, 10000, 10000, 100) { LeaderId = 21 },
         new ForceState(3, 10000, 10000, 100) { LeaderId = 31 }],
        [new(1, 1, 10000, 100, 100, 0, 0, 0, 0, 0, 0),
         new(2, 2, 10000, 100, 100, 0, 0, 0, 0, 0, 0),
         new(3, 3, 10000, 100, 100, 0, 0, 0, 0, 0, 0)],
        [new OfficerState(11, 1, 1, new(50, 50, 100, 50), false) { Status = OfficerStatus.Ruler },
         new(12, 1, 1, new(50, 50, 100, 50), false),
         new OfficerState(13, 1, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive },
         new OfficerState(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new OfficerState(31, 3, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
        controls: [new("first", 1, ControllerKind.Player), new("second", 2, ControllerKind.Player),
            new("third", 3, ControllerKind.Automatic)]);

    private static GameCommand Command(int kind) => kind switch
    {
        0 => new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2, new(GiftKind.Money, 1000, null)),
        1 => new RequestAttackCommand(Guid.NewGuid(), 1, 0, 12, 2, 3),
        2 => new ReturnPrisonerCommand(Guid.NewGuid(), 1, 0, 12, 2, 13),
        _ => new DemandSurrenderCommand(Guid.NewGuid(), 1, 0, 12, 2)
    };

    private static AdvanceXunResult Advance(SimulationEngine engine)
    {
        foreach (var seat in engine.Snapshot.Simulation.Controls) engine.EndControl(new(seat.ControllerId));
        return engine.AdvanceXun();
    }

    private sealed class DialogRules : SimulationRules
    {
        internal override TaskRule? FindTask(string kind) => kind == "dialog-fixture" ? new DialogRule() : base.FindTask(kind);

        private sealed class DialogRule : TaskRule
        {
            internal override int DurationDays(PersistentTask task) => 2;
            internal override bool Validate(WorldMutation world, PersistentTask task) => true;
            internal override int? DecisionRecipient(WorldMutation world, PersistentTask task) => 1;
            internal override void CompleteDecision(WorldMutation world, PersistentTask task, bool accept, List<DomainEvent> events) { }
        }
    }

    [Fact]
    public void DailyDecisionResumesIntoBoundaryDiplomacyWithoutRepeatingTheDay()
    {
        var rules = new DialogRules { Personnel = Rules(90).Personnel };
        var engine = new SimulationEngine(World(new(190, 1, 1)), rules);
        var dialog = new StartTaskCommand(Guid.NewGuid(), 1, 0, "dialog-fixture", 11, 1);
        Assert.True(engine.Execute(new("first"), dialog).Submission.Accepted);
        var gift = ((GiftCommand)Command(0)) with { Sequence = 1 };
        Assert.True(engine.Execute(new("first"), gift).Submission.Accepted);
        var all = Advance(engine).Events.ToList();
        Assert.Equal(new GameClock(190, 1, 3), engine.Snapshot.Clock);
        Assert.Equal(TaskResolutionPhase.DailyCommands, engine.Snapshot.Simulation.PendingDailyTask!.Phase);
        engine = new SimulationEngine(WorldState.Restore(engine.Snapshot), rules);
        var firstAnswer = engine.Execute(new("first"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, 2, dialog.CommandId, 0, true));
        Assert.True(firstAnswer.Submission.Accepted);
        all.AddRange(firstAnswer.Events);
        Assert.Equal(new GameClock(190, 1, 11), engine.Snapshot.Clock);
        Assert.Equal(TaskResolutionPhase.XunBoundary, engine.Snapshot.Simulation.PendingDailyTask!.Phase);
        Assert.DoesNotContain(all, e => e is XunAdvancedEvent);
        engine = new SimulationEngine(WorldState.Restore(engine.Snapshot), rules);
        var secondAnswer = engine.Execute(new("second"), new RespondDailyTaskCommand(Guid.NewGuid(), 2, 3, gift.CommandId, 0, false));
        Assert.True(secondAnswer.Submission.Accepted);
        all.AddRange(secondAnswer.Events);
        Assert.Equal(new GameClock(190, 1, 11), engine.Snapshot.Clock);
        Assert.Single(all.OfType<XunAdvancedEvent>());
        foreach (var stage in new[] { ResolutionStage.BeforeDailyCommands, ResolutionStage.DailyCommands,
            ResolutionStage.DailyMapUpdate, ResolutionStage.UnitAndBuildingEvents })
            Assert.Equal(10, all.OfType<StageCompletedEvent>().Count(e => e.Stage == stage));
        Assert.Single(all.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.XunUpdate);
    }

    public static IEnumerable<object[]> Journeys()
    {
        for (int kind = 0; kind < 4; kind++)
            foreach (var (distance, days) in new[] { (0, 10), (1, 10), (300, 10), (301, 20), (600, 20), (601, 30) })
                yield return [kind, distance, days];
    }

    [Theory]
    [MemberData(nameof(Journeys))]
    public void AllDiplomacyArrivesOnRoundedXunBoundaryBeforeWorldUpdate(int kind, int distance, int days)
    {
        // Crosses both month and year boundaries for multi-xun journeys.
        var started = new GameClock(190, 12, 21);
        var rules = Rules(distance);
        var engine = new SimulationEngine(World(started), rules);
        Assert.True(engine.Execute(new("first"), Command(kind)).Submission.Accepted);
        var task = Assert.Single(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(days, task.Due.DayNumber - started.DayNumber);
        for (int elapsed = 10; elapsed < days; elapsed += 10)
        {
            var earlier = Advance(engine);
            Assert.Null(earlier.Snapshot.Simulation.PendingDailyTask);
            Assert.Contains(earlier.Snapshot.Simulation.Tasks, t => t.Id == task.Id);
        }
        var arrived = Advance(engine);
        Assert.Equal(task.Due, arrived.Snapshot.Clock);
        Assert.Equal(TaskResolutionPhase.XunBoundary, arrived.Snapshot.Simulation.PendingDailyTask!.Phase);
        Assert.DoesNotContain(arrived.Events, e => e is XunAdvancedEvent ||
            e is StageCompletedEvent { Stage: ResolutionStage.XunUpdate });
        Assert.Equal(10, arrived.Events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.BeforeDailyCommands));
        Assert.Equal(10, arrived.Events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.DailyMapUpdate));
        var saved = JsonSerializer.Deserialize<WorldSnapshot>(CanonicalJson.Text(engine.Snapshot), CanonicalJson.Options)!;
        var restored = new SimulationEngine(WorldState.Restore(saved), rules);
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 2, 1, task.Id, 0, false);
        var answered = engine.Execute(new("second"), response);
        Assert.True(answered.Submission.Accepted);
        Assert.Equal(CanonicalJson.Text(answered.Events), CanonicalJson.Text(restored.Execute(new("second"), response).Events));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        Assert.Equal(task.Due, engine.Snapshot.Clock);
        Assert.Single(answered.Events.OfType<XunAdvancedEvent>());
        Assert.Single(answered.Events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.XunUpdate);
        Assert.DoesNotContain(answered.Events.OfType<StageCompletedEvent>(), e => e.Stage is
            ResolutionStage.BeforeDailyCommands or ResolutionStage.DailyCommands or ResolutionStage.DailyMapUpdate);
        Assert.DoesNotContain(engine.Snapshot.Simulation.Tasks, t => t.Id == task.Id);
        if (distance == 0) Assert.Empty(engine.Snapshot.Simulation.Tasks);
    }

    [Fact]
    public void MultipleBoundaryDecisionsCompleteTheXunOnlyAfterLastAnswer()
    {
        var engine = new SimulationEngine(World(new(190, 1, 1)), Rules(90));
        var first = (GiftCommand)Command(0);
        Assert.True(engine.Execute(new("first"), first).Submission.Accepted);
        var second = first with { CommandId = Guid.NewGuid(), Sequence = 1, OfficerId = 11 };
        Assert.True(engine.Execute(new("first"), second).Submission.Accepted);
        var all = Advance(engine).Events.ToList();
        foreach (var id in new[] { first.CommandId, second.CommandId })
        {
            Assert.Equal(id, engine.Snapshot.Simulation.PendingDailyTask!.RemainingTaskIds[0]);
            var result = engine.Execute(new("second"), new RespondDailyTaskCommand(Guid.NewGuid(), 2,
                engine.Snapshot.Simulation.NextSequence, id, 0, false));
            Assert.True(result.Submission.Accepted);
            all.AddRange(result.Events);
            if (id == first.CommandId) Assert.DoesNotContain(result.Events, e => e is XunAdvancedEvent or StageCompletedEvent);
        }
        Assert.Equal(new GameClock(190, 1, 11), engine.Snapshot.Clock);
        Assert.Single(all.OfType<XunAdvancedEvent>());
        Assert.Equal(2, all.OfType<GiftResolvedEvent>().Count());
        foreach (var stage in new[] { ResolutionStage.BeforeDailyCommands, ResolutionStage.DailyCommands,
            ResolutionStage.DailyMapUpdate, ResolutionStage.UnitAndBuildingEvents })
            Assert.Equal(10, all.OfType<StageCompletedEvent>().Count(e => e.Stage == stage));
        Assert.Single(all.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.XunUpdate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FieldRulerPositionDeterminesTravelInsteadOfRegionCenter(int kind)
    {
        var world = World(new(190, 1, 1)).BeginMutation();
        var position = new WorldPosition(302, 2);
        world.AddUnit(new UnitState(50, 2, position, 1000));
        world.Replace(world.Officers[21] with { SettlementId = null, UnitId = 50 });
        var rules = new SimulationRules
        {
            Personnel = Rules(90).Personnel,
            Military = new(new(200, 3, Enumerable.Repeat(0, 600).ToImmutableArray(), [new(0, 1, null)], [], [])
                { RegionIds = Enumerable.Repeat<int?>(2, 600).ToImmutableArray() })
        };
        List<DomainEvent> events = [];
        var rejection = Command(kind) switch
        {
            GiftCommand c => GiftDiplomacy.Start(world, new("first"), c, rules, events),
            RequestAttackCommand c => AttackRequestDiplomacy.Start(world, new("first"), c, rules, events),
            ReturnPrisonerCommand c => PrisonerReturnDiplomacy.Start(world, new("first"), c, rules, events),
            DemandSurrenderCommand c => SurrenderDiplomacy.Start(world, new("first"), c, rules, events),
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(CommandRejection.None, rejection);
        var task = Assert.Single(world.Simulation.Tasks);
        Assert.Equal(position, task.Journey!.DestinationPosition);
        Assert.Equal(new GameClock(190, 1, 21), task.Due);
        Assert.True(rules.FindTask(task.Kind)!.HasValidPayload(task));
        Assert.Equal(20, rules.FindTask(task.Kind)!.DurationDays(task));
    }
}
