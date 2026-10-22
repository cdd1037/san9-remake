using San9.Tests;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class DailyTaskDecisionTests
{
    private static ScenarioInitialState Content() => new(ScenarioInitialState.CurrentSchemaVersion, "daily-decisions", 190, 1, 1, 1,
        [new(1, "first", 1, 100) { BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], [], [])
    {
        Officers = Enumerable.Range(1, 3).Select(id => new OfficerInitialState($"officer.{id}",
            new(id, 1, 1, new(50, 50, 50, 50), false))).ToArray()
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 1000)) { IsRoot = true }], []) };

    private sealed class Rules(TaskResolutionPhase phase = TaskResolutionPhase.DailyCommands) : SimulationRules
    {
        internal override TaskRule? FindTask(string kind) => kind == "decision-fixture" ? new DecisionRule(phase) : base.FindTask(kind);
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
        {
            if (stage == ResolutionStage.BeforeDailyCommands)
            {
                var force = world.Forces[1];
                world.Replace(force with { BaseTrust = force.BaseTrust - 1 });
                world.NextRandom("daily-fixture");
            }
        }
        private sealed class DecisionRule(TaskResolutionPhase phase) : TaskRule
        {
            internal override TaskResolutionPhase Phase => phase;
            internal override int DurationDays(PersistentTask task) => phase == TaskResolutionPhase.XunBoundary ? 10 : 2;
            internal override bool Validate(WorldMutation world, PersistentTask task) => true;
            internal override int? DecisionRecipient(WorldMutation world, PersistentTask task) => 1;
            internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events) =>
                throw new InvalidOperationException("Explicit decision required.");
            internal override void CompleteDecision(WorldMutation world, PersistentTask task, bool accept, List<DomainEvent> events)
            {
                var place = world.Settlements[1];
                world.Replace(place with { PublicOrder = place.PublicOrder + (accept ? 10 : 1) });
            }
        }
    }

    private static SimulationEngine Engine(WorldState world) => new(world, new Rules());

    [Theory]
    [InlineData(TaskResolutionPhase.DailyCommands)]
    [InlineData(TaskResolutionPhase.XunBoundary)]
    public void IncrementalAnswersResumeOnlyCurrentDayAndPreserveReplay(TaskResolutionPhase phase)
    {
        var content = Content();
        SimulationEngine Factory(WorldState world) => new(world, new Rules(phase));
        var session = GameSession.Start(content, 42, Factory, enableXunStartCheckpoints: true);
        Assert.NotNull(session.TakeXunStartCheckpoint());
        List<DomainEvent> events = [];
        SessionResult Run(SessionOperation operation, Guid? id = null)
        {
            var pending = session.StartOperation(session.Request(operation, id: id));
            Assert.True(SpinWait.SpinUntil(() => pending.IsCompleted, TimeSpan.FromSeconds(10)));
            var result = pending.Complete();
            Assert.Equal(SessionDisposition.Accepted, result.Disposition);
            events.AddRange(result.Events);
            return result;
        }
        for (int actor = 1; actor <= 2; actor++)
        {
            var command = new StartTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
                "decision-fixture", actor, 1);
            Run(new CommandOperation(command), command.CommandId);
        }
        Run(new PrepareAdvanceOperation());
        do { Run(new AdvanceDayOperation()); }
        while (session.Snapshot.Simulation.Boundary == SimulationBoundary.Advancing);
        int decisionDay = phase == TaskResolutionPhase.DailyCommands ? 3 : 11;
        Assert.Equal(decisionDay, session.Snapshot.Clock.Day);
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        Assert.Throws<InvalidOperationException>(() => session.Capture());
        Assert.Null(session.TakeXunStartCheckpoint());
        var waiting = session.Snapshot;
        Assert.Equal(SessionRejection.NotStable, session.Apply(session.Request(new AdvanceDayOperation())).Rejection);
        Assert.Same(waiting, session.Snapshot);
        for (int answer = 0; answer < 2; answer++)
        {
            var pending = session.Snapshot.Simulation.PendingDailyTask!;
            var response = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
                pending.RemainingTaskIds[0], 0, answer == 0);
            var result = Run(new CommandOperation(response), response.CommandId);
            Assert.DoesNotContain(result.Events, e => e is StageCompletedEvent { Stage: ResolutionStage.BeforeDailyCommands });
            Assert.Equal(decisionDay + (answer == 1 && phase == TaskResolutionPhase.DailyCommands ? 1 : 0), session.Snapshot.Clock.Day);
            if (answer == 0) Assert.Null(session.TakeXunStartCheckpoint());
        }
        if (phase == TaskResolutionPhase.DailyCommands)
        {
            Assert.Equal(SimulationBoundary.Advancing, session.Snapshot.Simulation.Boundary);
            Assert.DoesNotContain(events, e => e is XunAdvancedEvent);
            while (session.Snapshot.Simulation.Boundary == SimulationBoundary.Advancing) Run(new AdvanceDayOperation());
        }
        Assert.Equal(11, session.Snapshot.Clock.Day);
        Assert.Equal(session.Capture(), session.TakeXunStartCheckpoint());
        Assert.Null(session.TakeXunStartCheckpoint());
        Assert.Equal(90, session.Snapshot.Forces[0].BaseTrust);
        Assert.Single(events.OfType<XunAdvancedEvent>());
        Assert.Equal(10, events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.BeforeDailyCommands));
    }

    [Fact]
    public void UnsupportedAutomaticTaskResponseReportsIdentityAndRollsBack()
    {
        var original = Content();
        var content = original with { Information = original.Information with { Corps = original.Information.Corps
            .Select(c => c with { ControlMode = CorpsControlMode.Automatic }).ToArray() } };
        var session = GameSession.Start(content, 42, Engine);
        Assert.True(session.Execute(new("corps:1"), new StartTaskCommand(Guid.NewGuid(), 1, 0,
            "decision-fixture", 2, 1)).Submission.Accepted);
        var before = session.Capture();
        var request = session.Request(new AdvanceOperation());
        var advance = session.Apply(request);
        Assert.Equal(SessionRejection.AutomaticResponseFailed, advance.Rejection);
        var failure = Assert.IsType<San9.Application.AI.AutomaticResponseFailure>(advance.AutomaticResponseFailure);
        Assert.Equal("response-unsupported", failure.Code);
        Assert.Equal("daily-task", failure.Kind);
        Assert.Equal("corps:1", failure.ControllerId);
        StateAssert.Equal(before, session.Capture());
        Assert.Empty(advance.Events);
        Assert.Equal(failure, session.Apply(request).AutomaticResponseFailure);
        var exception = Assert.Throws<InvalidOperationException>(() => session.AdvanceXun());
        Assert.Contains(failure.Code, exception.Message);
        Assert.Contains(failure.ControllerId, exception.Message);
        StateAssert.Equal(before, session.Capture());
    }

    [Fact]
    public void RevokedTaskDelegationReturnsResponseOwnershipToPlayer()
    {
        var original = Content();
        var content = original with { Information = original.Information with { Corps = original.Information.Corps
            .Select(c => c with { ControlMode = CorpsControlMode.Automatic }).ToArray() } };
        var session = GameSession.Start(content, 42, Engine);
        Assert.True(session.Execute(new("corps:1"), new StartTaskCommand(Guid.NewGuid(), 1, 0,
            "decision-fixture", 2, 1)).Submission.Accepted);
        Assert.True(session.Execute(new("player"), new SetCorpsControlCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 1, CorpsControlMode.Direct)).Submission.Accepted);
        var result = session.Apply(session.Request(new AdvanceOperation()));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        var pending = Assert.IsType<ResponseRequest>(session.GetPendingResponse());
        Assert.Equal("player", pending.Owner.ControllerId);
        Assert.Equal(ControllerKind.Player, pending.Owner.Kind);
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
    }

    [Fact]
    public void TwoSameDayDecisionsContinueInMemoryAndReplayWithoutRepeatingDailyWork()
    {
        var content = Content();
        var session = GameSession.Start(content, 42, Engine);
        for (int actor = 1; actor <= 2; actor++)
            Assert.True(session.Execute(new("player"), new StartTaskCommand(Guid.NewGuid(), 1,
                session.Snapshot.Simulation.NextSequence, "decision-fixture", actor, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), content, Engine);
        restored.AdvanceXun();
        var advance = session.AdvanceXun();
        Assert.Equal(3, session.Snapshot.Clock.Day);
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        var beforeSettings = session.Snapshot;
        long beforeSettingsSequence = session.NextOperationSequence;
        var settings = session.Apply(session.Request(new PkSettingsOperation(false, false, true)));
        Assert.Equal(SessionDisposition.Rejected, settings.Disposition);
        Assert.Equal(SessionRejection.NotStable, settings.Rejection);
        StateAssert.Equal(beforeSettings, session.Snapshot);
        Assert.Equal(beforeSettingsSequence, session.NextOperationSequence);
        Assert.Equal(97, session.Snapshot.Forces[0].BaseTrust);
        Assert.DoesNotContain(advance.Events, e => e is XunAdvancedEvent);
        Assert.Equal(3, advance.Events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.BeforeDailyCommands));
        List<DomainEvent> events = [.. advance.Events];
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        for (int answer = 0; answer < 2; answer++)
        {
            var pending = session.Snapshot.Simulation.PendingDailyTask!;
            var command = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
                pending.RemainingTaskIds[0], 0, answer == 0);
            var beforeAnswer = session.Snapshot;
            Assert.True(session.Validate(new("player"), command).Accepted);
            StateAssert.Equal(beforeAnswer, session.Snapshot);
            var request = session.Request(new CommandOperation(command), id: command.CommandId);
            var expected = session.Apply(request);
            events.AddRange(expected.Events);
            var actual = restored.Apply(request);
            Assert.Equal(SessionDisposition.Accepted, expected.Disposition);
            Assert.Equal(SessionDisposition.Accepted, actual.Disposition);
            EventAssert.Equal(expected.Events, actual.Events);
            StateAssert.Equal(session.Snapshot, restored.Snapshot);
            var afterAnswer = session.Snapshot;
            long operationSequence = session.NextOperationSequence;
            var duplicate = session.Apply(request);
            Assert.Equal(SessionDisposition.AlreadyProcessed, duplicate.Disposition);
            Assert.Empty(duplicate.Events);
            Assert.Same(afterAnswer, session.Snapshot);
            Assert.Equal(operationSequence, session.NextOperationSequence);
            Assert.Equal(answer == 0 ? 0 : 1, expected.Events.OfType<XunAdvancedEvent>().Count());
            if (answer == 0)
            {
                Assert.Equal(3, session.Snapshot.Clock.Day);
                Assert.Equal(97, session.Snapshot.Forces[0].BaseTrust);
                Assert.DoesNotContain(expected.Events, e => e is StageCompletedEvent { Stage: ResolutionStage.BeforeDailyCommands });
                Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
            }
        }
        Assert.Equal(11, session.Snapshot.Clock.Day);
        Assert.Equal(90, session.Snapshot.Forces[0].BaseTrust);
        Assert.Equal(11, session.Snapshot.Settlements[0].PublicOrder);
        Assert.Empty(session.Snapshot.Simulation.Tasks);
        Assert.Null(session.Snapshot.Simulation.PendingDailyTask);
        Assert.Equal(10, events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.BeforeDailyCommands));
    }

    [Fact]
    public void InvalidContinuationDateQueueAndRespondentAreRejectedOnRestore()
    {
        var session = GameSession.Start(Content(), 42, Engine);
        for (int actor = 1; actor <= 2; actor++)
            Assert.True(session.Execute(new("player"), new StartTaskCommand(Guid.NewGuid(), 1,
                session.Snapshot.Simulation.NextSequence, "decision-fixture", actor, 1)).Submission.Accepted);
        session.AdvanceXun();
        var saved = new SessionSnapshot(session.Snapshot, [], [], session.NextOperationSequence);
        var pending = saved.World.Simulation.PendingDailyTask!;
        DailyTaskDecision[] invalid = [pending with { XunStarted = new(190, 1, 2) },
            pending with { XunStarted = new(190, 1, 11) }, pending with { RespondingForceId = 99 },
            pending with { RemainingTaskIds = [] }, pending with { RemainingTaskIds = [Guid.NewGuid()] },
            pending with { RemainingTaskIds = [pending.RemainingTaskIds[1], pending.RemainingTaskIds[0]] },
            pending with { RemainingTaskIds = [pending.RemainingTaskIds[0], pending.RemainingTaskIds[0]] }];
        foreach (var choice in invalid)
            Assert.Throws<ArgumentException>(() => WorldState.Restore(saved.World with
                { Simulation = saved.World.Simulation with { PendingDailyTask = choice } }));
        Assert.Throws<ArgumentException>(() => WorldState.Restore(saved.World with
            { Simulation = saved.World.Simulation with { PendingDailyTask = null } }));
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        Assert.Throws<ArgumentException>(() => GameSession.Restore(Content(), saved, Engine));

    }

    [Fact]
    public void InvalidResponsesAndNormalCommandsCannotMutatePendingDecision()
    {
        var session = GameSession.Start(Content(), 42, Engine);
        Assert.True(session.Execute(new("player"), new StartTaskCommand(Guid.NewGuid(), 1, 0,
            "decision-fixture", 1, 1)).Submission.Accepted);
        session.AdvanceXun();
        var pending = session.Snapshot.Simulation.PendingDailyTask!;
        var before = session.Snapshot;
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            pending.RemainingTaskIds[0], 0, true);
        Assert.Equal(CommandRejection.NotActiveController, session.Execute(new("other"), response).Submission.Rejection);
        StateAssert.Equal(before, session.Snapshot);
        long operationSequence = session.NextOperationSequence;
        foreach (var invalid in new[]
        {
            response with { TaskId = Guid.NewGuid() },
            response with { Step = response.Step + 1 },
            response with { CountryTitleId = 1 },
            response with { MerchantInvestment = new(MerchantInvestmentChoice.Decline, 0) }
        })
        {
            var result = session.Execute(new("player"), invalid);
            Assert.Equal(CommandRejection.InvalidDecision, result.Submission.Rejection);
            Assert.Empty(result.Events);
            StateAssert.Equal(before, session.Snapshot);
            Assert.Equal(operationSequence, session.NextOperationSequence);
        }
        Assert.Equal(CommandRejection.DecisionPending, session.Execute(new("player"),
            new WaitCommand(Guid.NewGuid(), 1, response.Sequence)).Submission.Rejection);
        StateAssert.Equal(before, session.Snapshot);
        Assert.Throws<InvalidOperationException>(() => session.AdvanceXun());
        StateAssert.Equal(before, session.Snapshot);
    }
}
