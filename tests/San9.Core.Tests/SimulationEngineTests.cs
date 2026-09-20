using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SimulationEngineTests
{
    internal static WorldState CreateWorld(GameClock? clock = null) => WorldState.Create(clock ?? new(190, 1, 1), 190,
        [new(1, 1000, 2000, 50) { LeaderId = 1 }, new(2, 1000, 2000, 50) { LeaderId = 4 }],
        [new(1, 1, 100, 100, 100, 50, 500, 300, 500, 200, 500) { CorpsId = 1 },
         new(2, 2, 100, 100, 100, 50, 500, 300, 500, 200, 500) { CorpsId = 2 }],
        [new(1, 1, 1, new(60, 70, 80, 90), false), new(2, 1, 1, new(50, 60, 100, 70), false),
         new(3, 1, 1, new(40, 80, 70, 60), false), new(4, 2, 2, new(60, 70, 80, 90), false),
         new(5, 2, 2, new(50, 60, 100, 70), false), new(6, 2, 2, new(40, 80, 70, 60), false)],
        corps: [new(1, 1, 1, null), new(2, 2, 4, null)],
        controls: [new("player", 1, ControllerKind.Player), new("ai", 2, ControllerKind.Automatic)]);

    internal static Guid Id(int value) => new(value, 0, 0, new byte[8]);
    internal static AdvanceXunResult Advance(SimulationEngine engine)
    {
        while (engine.Snapshot.Simulation.ActiveControl < engine.Snapshot.Simulation.Controls.Length &&
            engine.Snapshot.Simulation.Boundary == SimulationBoundary.Stable)
            engine.EndControl(new(engine.Snapshot.Simulation.Controls[engine.Snapshot.Simulation.ActiveControl].ControllerId));
        return engine.AdvanceXun();
    }

    internal sealed class FixtureRules : SimulationRules
    {
        internal Action<ResolutionStage, WorldMutation, List<DomainEvent>>? OnStage { get; set; }
        internal bool FailCompletion { get; set; }
        internal int Duration { get; set; } = 15;
        internal override TaskRule? FindTask(string kind) => kind == "fixture-investment" ? new Investment(this) : null;
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events) => OnStage?.Invoke(stage, world, events);
        private sealed class Investment(FixtureRules owner) : TaskRule
        {
            internal override int DurationDays(PersistentTask task) => owner.Duration;
            internal override bool Validate(WorldMutation world, PersistentTask task) => world.Forces[task.ForceId].Money >= 50;
            internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events)
            {
                var force = world.Forces[task.ForceId];
                world.Replace(force with { Money = force.Money - 50 });
                var city = world.Settlements[task.OriginSettlementId];
                int increment = 1 + (int)(world.NextRandom("fixture-investment") % 10);
                world.Replace(city with { Commerce = owner.FailCompletion ? 9999 : city.Commerce + increment });
            }
        }
    }

    [Fact]
    public void EndingControlsPreservesPublishedRecordsAndRestoresMidRound()
    {
        var engine = new SimulationEngine(CreateWorld());
        var before = engine.Snapshot;
        Assert.Throws<InvalidOperationException>(() => engine.EndControl(new("ai")));
        Assert.Same(before, engine.Snapshot);
        var ended = engine.EndControl(new("player"));
        var after = ended.Snapshot;
        Assert.Equal(new ControlEndedEvent("player", 1), Assert.Single(ended.Events));
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(0, before.Simulation.ActiveControl);
        Assert.Equal(1, after.Simulation.ActiveControl);
        Assert.Same(before.Forces, after.Forces);
        Assert.Same(before.Settlements, after.Settlements);
        Assert.Same(before.Officers, after.Officers);
        Assert.Same(before.Units, after.Units);
        Assert.Same(before.Corps, after.Corps);
        Assert.Same(before.Treasures, after.Treasures);
        Assert.Same(before.Relations, after.Relations);
        var restored = new SimulationEngine(WorldState.Restore(after));
        Assert.Throws<InvalidOperationException>(() => restored.EndControl(new("player")));
        engine.EndControl(new("ai"));
        restored.EndControl(new("ai"));
        Assert.Throws<InvalidOperationException>(() => engine.EndControl(new("ai")));
        Assert.Equal(engine.AdvanceXun().Snapshot.StateHash, restored.AdvanceXun().Snapshot.StateHash);
    }

    [Fact]
    public void NewGamePreparesOnceAndRestoreDoesNotRepeat()
    {
        var rules = new FixtureRules();
        int calls = 0;
        rules.OnStage = (stage, world, _) =>
        {
            if (stage != ResolutionStage.XunUpdate) return;
            calls++;
            world.Replace(world.Forces[1] with { Money = world.Forces[1].Money - 7 });
        };
        var engine = new SimulationEngine(CreateWorld(), rules);
        Assert.Equal(1, calls);
        Assert.Equal(990u, engine.Snapshot.Forces[0].Money);
        Assert.Equal(new GameClock(190, 1, 1), engine.Snapshot.Clock);
        var saved = engine.CaptureStableState();
        var restored = new SimulationEngine(WorldState.Restore(saved), rules);
        Assert.Equal(1, calls);
        Assert.Empty(restored.InitialEvents);
        Assert.Equal(saved.StateHash, restored.Snapshot.StateHash);
        Assert.Equal(saved.Revision, restored.Snapshot.Revision);
        var sameWorld = WorldState.Restore(saved);
        _ = new SimulationEngine(sameWorld, rules);
        _ = new SimulationEngine(sameWorld, rules);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(1, 1, 190, 1, 11, false, false, false)]
    [InlineData(1, 11, 190, 1, 21, false, false, false)]
    [InlineData(2, 21, 190, 3, 1, true, false, false)]
    [InlineData(3, 21, 190, 4, 1, true, true, false)]
    [InlineData(12, 21, 191, 1, 1, true, true, true)]
    public void DailyAndPeriodStagesHaveExplicitOrder(int month, int day, int nextYear, int nextMonth, int nextDay,
        bool monthly, bool quarterly, bool annual)
    {
        var engine = new SimulationEngine(CreateWorld(new(190, month, day)));
        var result = Advance(engine);
        Assert.Equal(new GameClock(nextYear, nextMonth, nextDay), result.Snapshot.Clock);
        var stages = result.Events.OfType<StageCompletedEvent>().ToArray();
        var cursor = new GameClock(190, month, day);
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(new StageCompletedEvent(cursor, ResolutionStage.BeforeDailyCommands), stages[4 * i]);
            Assert.Equal(new StageCompletedEvent(cursor, ResolutionStage.DailyCommands), stages[4 * i + 1]);
            cursor = cursor.NextDay();
            Assert.Equal(new StageCompletedEvent(cursor, ResolutionStage.DailyMapUpdate), stages[4 * i + 2]);
            Assert.Equal(new StageCompletedEvent(cursor, ResolutionStage.UnitAndBuildingEvents), stages[4 * i + 3]);
        }
        List<ResolutionStage> expected = [ResolutionStage.TerminalCheck, ResolutionStage.XunEvents, ResolutionStage.XunUpdate];
        if (monthly) expected.Add(ResolutionStage.MonthEvents);
        expected.Add(ResolutionStage.AppearanceAndIndependence);
        if (quarterly) expected.AddRange([ResolutionStage.QuarterPrelude, ResolutionStage.QuarterEvents, ResolutionStage.QuarterStudyOffers, ResolutionStage.QuarterUpdate]);
        if (annual) expected.AddRange([ResolutionStage.AnnualAppearance, ResolutionStage.AnnualCleanup]);
        expected.Add(ResolutionStage.XunCleanup);
        if (quarterly) expected.Add(ResolutionStage.QuarterPolitics);
        Assert.Equal(expected, stages.Skip(40).Select(s => s.Stage));
    }

    [Fact]
    public void CommandsShareControlAndBusinessValidationWithoutReservingOnRejection()
    {
        var world = CreateWorld();
        var engine = new SimulationEngine(world);
        var command = new DevelopCommand(Id(1), 1, 0, new(1, 1, [2], DomesticActivity.Commerce));
        var before = engine.Snapshot;
        Assert.True(engine.Validate(new("player"), command).Accepted);
        Assert.True(engine.Validate(new("player"), command).Accepted);
        Assert.Same(before, engine.Snapshot);
        foreach (var bad in new GameCommand[]
        {
            command with { IssuerId = -1 }, command with { Sequence = -1 }, command with { Sequence = 9 },
            command with { CommandId = Guid.Empty }, command with { IssuerId = 2 },
            command with { Development = new(2, 2, [5], DomesticActivity.Commerce) },
            command with { Development = new(1, 2, [2], DomesticActivity.Commerce) },
            command with { Development = new(1, 1, [99], DomesticActivity.Commerce) },
            new StartTaskCommand(Id(1), 1, 0, "unknown", 2, 1),
        })
        {
            var result = engine.Execute(new("player"), bad);
            Assert.False(result.Submission.Accepted);
            Assert.Empty(result.Events);
            Assert.Same(before, result.Snapshot);
        }
        Assert.Equal(CommandRejection.NotActiveController, engine.Execute(new("ai"), command).Submission.Rejection);
        var accepted = engine.Execute(new("player"), command);
        Assert.True(accepted.Submission.Accepted);
        Assert.Equal(new DevelopmentOutcome(DevelopmentRejection.None, 7, 50), accepted.Development);
        Assert.Equal(307, accepted.Snapshot.Settlements[0].Commerce);
        Assert.Equal(947u, accepted.Snapshot.Forces[0].Money);
        Assert.Equal(before.Clock, accepted.Snapshot.Clock);
        Assert.Equal(before.Simulation.RandomStreams, accepted.Snapshot.Simulation.RandomStreams);
        Assert.Equal(CommandRejection.WrongSequence, engine.Execute(new("player"), command).Submission.Rejection);
        Assert.False(engine.Execute(new("player"), command with { CommandId = Id(2), Sequence = 1,
            Development = new(1, 1, [2], DomesticActivity.Cultivation) }).Submission.Accepted);
        Assert.Same(accepted.Snapshot, engine.Snapshot);
        engine.EndControl(new("player"));
        Assert.False(engine.Execute(new("player"), new WaitCommand(Id(2), 1, 1)).Submission.Accepted);
        Assert.True(engine.Execute(new("ai"), new DevelopCommand(Id(2), 2, 1, new(2, 2, [5], DomesticActivity.Commerce))).Submission.Accepted);
        Assert.Throws<InvalidOperationException>(() => engine.EndControl(new("player")));
        engine.EndControl(new("ai"));
        engine.AdvanceXun();
        Assert.Equal(CommandRejection.WrongSequence, engine.Validate(new("player"), command).Rejection);
    }

    [Fact]
    public void ExecutionRechecksOwnershipAfterPlanning()
    {
        var world = CreateWorld();
        var engine = new SimulationEngine(world);
        var command = new DevelopCommand(Id(1), 1, 0, new(1, 1, [2], DomesticActivity.Commerce));
        Assert.True(engine.Validate(new("player"), command).Accepted);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[2] with { OwnerId = 2, SettlementId = 2 });
        world.Commit(mutation);
        var before = engine.Snapshot;
        Assert.Equal(DevelopmentRejection.WrongOwner, engine.Execute(new("player"), command).Development!.Value.Rejection);
        Assert.Same(before, engine.Snapshot);
    }

    [Fact]
    public void TaskSurvivesXunAndSnapshotUntilDueThenCommitsOnce()
    {
        var rules = new FixtureRules();
        var engine = new SimulationEngine(CreateWorld(), rules);
        var start = engine.Execute(new("player"), new StartTaskCommand(Id(1), 1, 0, "fixture-investment", 2, 1));
        Assert.True(start.Submission.Accepted);
        Assert.Single(start.Events.OfType<TaskStartedEvent>());
        Assert.Equal(997u, engine.Snapshot.Forces[0].Money);
        Assert.Equal(300, engine.Snapshot.Settlements[0].Commerce);
        Assert.Empty(Advance(engine).Events.OfType<TaskCompletedEvent>());
        Assert.Equal(10, Assert.Single(engine.Snapshot.Simulation.Tasks).ProgressDays);
        Assert.False(engine.Execute(new("player"), new DevelopCommand(Id(2), 1, 1,
            new(1, 1, [2], DomesticActivity.Commerce))).Submission.Accepted);
        var json = JsonSerializer.Serialize(engine.CaptureStableState());
        var restored = new SimulationEngine(WorldState.Restore(JsonSerializer.Deserialize<WorldSnapshot>(json)!), rules);
        Assert.Empty(restored.InitialEvents);
        var expected = Advance(engine);
        var actual = Advance(restored);
        Assert.Equal(expected.Events, actual.Events);
        Assert.Equal(expected.Snapshot.StateHash, actual.Snapshot.StateHash);
        Assert.Equal(new GameClock(190, 1, 16), Assert.Single(actual.Events.OfType<TaskCompletedEvent>()).Clock);
        Assert.Empty(actual.Snapshot.Simulation.Tasks);
        Assert.Equal(941u, actual.Snapshot.Forces[0].Money);
        Assert.InRange(actual.Snapshot.Settlements[0].Commerce, 301, 310);
        Assert.Single(actual.Snapshot.Simulation.RandomStreams.Where(s => s.Name == "fixture-investment"));
        Assert.Empty(Advance(restored).Events.OfType<TaskCompletedEvent>());
        Assert.Equal(938u, restored.Snapshot.Forces[0].Money);
    }

    [Fact]
    public void DueDateAtStableBoundaryDoesNotRunNextDaysCommandEarly()
    {
        var engine = new SimulationEngine(CreateWorld(), new FixtureRules { Duration = 10 });
        engine.Execute(new("player"), new StartTaskCommand(Id(1), 1, 0, "fixture-investment", 2, 1));
        Assert.Empty(Advance(engine).Events.OfType<TaskCompletedEvent>());
        Assert.Single(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(new GameClock(190, 1, 11), Assert.Single(Advance(engine).Events.OfType<TaskCompletedEvent>()).Clock);
    }

    [Fact]
    public void InvalidTargetCancelsTaskOnceWithoutFinalEffectAndReleasesOccupation()
    {
        var world = CreateWorld();
        var engine = new SimulationEngine(world, new FixtureRules());
        engine.Execute(new("player"), new StartTaskCommand(Id(1), 1, 0, "fixture-investment", 2, 1));
        Advance(engine);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Settlements[1] with { OwnerId = 2 });
        foreach (var officer in mutation.Officers.Values.Where(o => o.OwnerId == 1)) mutation.Replace(officer with { OwnerId = 2 });
        mutation.Replace(mutation.Forces[1] with { LeaderId = null });
        mutation.Replace(mutation.Corps[1] with { OwnerId = 2 });
        world.Commit(mutation);
        var result = Advance(engine);
        Assert.Single(result.Events.OfType<TaskCancelledEvent>());
        Assert.Empty(result.Events.OfType<TaskCompletedEvent>());
        Assert.Empty(result.Snapshot.Simulation.Tasks);
        Assert.Equal(994u, result.Snapshot.Forces[0].Money);
        Assert.DoesNotContain(result.Snapshot.Simulation.RandomStreams, s => s.Name == "fixture-investment");
        Assert.Empty(Advance(engine).Events.OfType<TaskCancelledEvent>());
        engine.EndControl(new("player"));
        Assert.True(engine.Execute(new("ai"), new DevelopCommand(Id(2), 2, 1, new(2, 1, [2], DomesticActivity.Commerce))).Submission.Accepted);
    }

    [Fact]
    public void FailedAdvanceDiscardsEffectsRandomAndEventsAndCanRetry()
    {
        var rules = new FixtureRules { FailCompletion = true, Duration = 2 };
        var engine = new SimulationEngine(CreateWorld(), rules);
        engine.Execute(new("player"), new StartTaskCommand(Id(1), 1, 0, "fixture-investment", 2, 1));
        engine.EndControl(new("player")); engine.EndControl(new("ai"));
        var saved = engine.CaptureStableState();
        Assert.Throws<ArgumentException>(() => engine.AdvanceXun());
        Assert.Same(saved, engine.CaptureStableState());
        rules.FailCompletion = false;
        var fresh = new SimulationEngine(WorldState.Restore(saved), rules);
        var expected = fresh.AdvanceXun();
        var actual = engine.AdvanceXun();
        Assert.Equal(expected.Events, actual.Events);
        Assert.Equal(expected.Snapshot.StateHash, actual.Snapshot.StateHash);
    }

    [Theory]
    [InlineData(ResolutionStage.TerminalCheck)]
    [InlineData(ResolutionStage.QuarterUpdate)]
    public void InitialTerminalShortCircuitsAllLaterStages(ResolutionStage stop)
    {
        var rules = new FixtureRules { OnStage = (stage, world, _) =>
        {
            if (stage == stop) world.SetSimulation(world.Simulation with { TerminalReason = "fixture-terminal" });
        } };
        var engine = new SimulationEngine(CreateWorld(), rules);
        Assert.Equal(stop, engine.InitialEvents.OfType<StageCompletedEvent>().Last().Stage);
        Assert.Single(engine.InitialEvents.OfType<SimulationEndedEvent>());
        Assert.Equal(SimulationBoundary.Terminal, engine.Snapshot.Simulation.Boundary);
        var before = engine.Snapshot;
        Assert.Empty(engine.AdvanceXun().Events);
        Assert.Same(before, engine.Snapshot);
        Assert.False(engine.Execute(new("player"), new WaitCommand(Id(1), 1, 0)).Submission.Accepted);
        Assert.Throws<InvalidOperationException>(() => engine.EndControl(new("player")));
        Assert.Equal(before.StateHash, new SimulationEngine(WorldState.Restore(before), rules).Snapshot.StateHash);
    }

    [Fact]
    public void DailyTerminalCancelsTasksAndStableCaptureRejectsReentrancy()
    {
        var rules = new FixtureRules();
        var engine = new SimulationEngine(CreateWorld(), rules);
        engine.Execute(new("player"), new StartTaskCommand(Id(1), 1, 0, "fixture-investment", 2, 1));
        rules.OnStage = (stage, world, _) =>
        {
            Assert.Throws<InvalidOperationException>(() => engine.CaptureStableState());
            Assert.Throws<InvalidOperationException>(() => engine.AdvanceXun());
            Assert.False(engine.Execute(new("player"), new WaitCommand(Id(2), 1, 1)).Submission.Accepted);
            Assert.Equal(new GameClock(190, 1, 1), engine.Snapshot.Clock);
            if (stage == ResolutionStage.DailyMapUpdate)
                world.SetSimulation(world.Simulation with { TerminalReason = "fixture-daily-terminal" });
        };
        var result = Advance(engine);
        Assert.Equal(new GameClock(190, 1, 2), result.Snapshot.Clock);
        Assert.Single(result.Events.OfType<TaskCancelledEvent>());
        Assert.Single(result.Events.OfType<SimulationEndedEvent>());
        Assert.DoesNotContain(result.Events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.UnitAndBuildingEvents);
        Assert.Equal(result.Snapshot.StateHash, new SimulationEngine(WorldState.Restore(result.Snapshot), new FixtureRules()).Snapshot.StateHash);
    }

    [Fact]
    public void NamedRandomStreamsAreOrderIndependentAndRestorable()
    {
        var a = CreateWorld(); var b = CreateWorld();
        _ = new SimulationEngine(a); _ = new SimulationEngine(b);
        var first = a.BeginMutation(); var second = b.BeginMutation();
        ulong combat = first.NextRandom("combat");
        ulong economy = first.NextRandom("economy");
        Assert.Equal(economy, second.NextRandom("economy"));
        Assert.Equal(combat, second.NextRandom("combat"));
        a.Commit(first); b.Commit(second);
        Assert.Equal(a.CreateSnapshot().StateHash, b.CreateSnapshot().StateHash);
        var restored = WorldState.Restore(a.CreateSnapshot());
        Assert.Equal(a.BeginMutation().NextRandom("combat"), restored.BeginMutation().NextRandom("combat"));
        Assert.NotEqual(combat, economy);
    }

    [Fact]
    public void RestoringActiveControlKeepsActualOrderAndRejectsUnsupportedTasks()
    {
        var engine = new SimulationEngine(CreateWorld(), new FixtureRules());
        engine.Execute(new("player"), new StartTaskCommand(Id(1), 1, 0, "fixture-investment", 2, 1));
        engine.EndControl(new("player"));
        var saved = engine.CaptureStableState();
        Assert.Throws<ArgumentException>(() => new SimulationEngine(WorldState.Restore(saved)));
        var restored = new SimulationEngine(WorldState.Restore(saved), new FixtureRules());
        Assert.Equal(1, restored.Snapshot.Simulation.ActiveControl);
        Assert.False(restored.Execute(new("player"), new WaitCommand(Id(2), 1, 1)).Submission.Accepted);
        Assert.True(restored.Execute(new("ai"), new WaitCommand(Id(2), 2, 1)).Submission.Accepted);
        Assert.Equal(2, restored.Snapshot.Simulation.NextSequence);
        Assert.Equal(2, Assert.Single(restored.Execute(new("ai"), new WaitCommand(Id(3), 2, 2)).Events.OfType<CommandResolvedEvent>()).Sequence);
        Assert.Throws<ArgumentException>(() => WorldState.Restore(CreateWorld().CreateSnapshot()));
    }
    [Fact]
    public void MultipleTasksResolveInActualStartOrderAndCompetingOfficerIsRejected()
    {
        var engine = new SimulationEngine(CreateWorld(), new FixtureRules { Duration = 2 });
        Assert.True(engine.Execute(new("player"), new StartTaskCommand(Id(9), 1, 0, "fixture-investment", 2, 1)).Submission.Accepted);
        var before = engine.Snapshot;
        Assert.False(engine.Execute(new("player"), new StartTaskCommand(Id(3), 1, 1, "fixture-investment", 2, 1)).Submission.Accepted);
        Assert.Same(before, engine.Snapshot);
        Assert.True(engine.Execute(new("player"), new StartTaskCommand(Id(3), 1, 1, "fixture-investment", 3, 1)).Submission.Accepted);
        var result = Advance(engine);
        Assert.Equal(new[] { Id(9), Id(3) }, result.Events.OfType<TaskCompletedEvent>().Select(e => e.TaskId));
        Assert.Equal(894u, result.Snapshot.Forces[0].Money);
    }

    [Fact]
    public void TaskCompletionRechecksResourcesAfterDailyPrecommandUpdates()
    {
        var rules = new FixtureRules { Duration = 2 };
        var engine = new SimulationEngine(CreateWorld(), rules);
        engine.Execute(new("player"), new StartTaskCommand(Id(1), 1, 0, "fixture-investment", 2, 1));
        rules.OnStage = (stage, world, _) =>
        {
            if (stage == ResolutionStage.BeforeDailyCommands && world.Clock.Day == 3)
                world.Replace(world.Forces[1] with { Money = 49 });
        };
        var result = Advance(engine);
        Assert.Equal(new GameClock(190, 1, 3), Assert.Single(result.Events.OfType<TaskCancelledEvent>()).Clock);
        Assert.Empty(result.Events.OfType<TaskCompletedEvent>());
        Assert.Equal(46u, result.Snapshot.Forces[0].Money);
        Assert.Equal(300, result.Snapshot.Settlements[0].Commerce);
        Assert.DoesNotContain(result.Snapshot.Simulation.RandomStreams, s => s.Name == "fixture-investment");
    }

    [Fact]
    public void ExecutionGuardBelongsToWorldAcrossEngineInstances()
    {
        var rules = new FixtureRules();
        var world = CreateWorld();
        var first = new SimulationEngine(world, rules);
        var second = new SimulationEngine(world);
        rules.OnStage = (_, _, _) =>
        {
            Assert.Throws<InvalidOperationException>(() => second.CaptureStableState());
            Assert.Throws<InvalidOperationException>(() => second.AdvanceXun());
        };
        Advance(first);
        Assert.Same(first.CaptureStableState(), second.CaptureStableState());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void SchedulingInvariantsRejectMalformedCandidatesAtomically(int variant)
    {
        var world = CreateWorld();
        var engine = new SimulationEngine(world, new FixtureRules());
        engine.Execute(new("player"), new StartTaskCommand(Id(1), 1, 0, "fixture-investment", 2, 1));
        var before = engine.Snapshot;
        var state = before.Simulation;
        var task = state.Tasks[0];
        var invalid = variant switch
        {
            0 => state with { ActiveControl = 3 },
            1 => state with { Tasks = [task with { OfficerId = 999 }] },
            2 => state with { Tasks = [task with { ProgressDays = 1 }] },
            3 => state with { NextSequence = 0 },
            4 => state with { Controls = [new("player", 99, ControllerKind.Player)] },
            _ => state with { RandomStreams = [new("same", 1), new("same", 2)] }
        };
        var mutation = world.BeginMutation();
        mutation.SetSimulation(invalid);
        Assert.Throws<ArgumentException>(() => world.Commit(mutation));
        Assert.Same(before, engine.Snapshot);
    }

}
