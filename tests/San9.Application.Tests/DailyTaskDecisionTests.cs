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

    private sealed class Rules : SimulationRules
    {
        internal override TaskRule? FindTask(string kind) => kind == "decision-fixture" ? new DecisionRule() : base.FindTask(kind);
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
        {
            if (stage == ResolutionStage.BeforeDailyCommands)
            {
                var force = world.Forces[1];
                world.Replace(force with { BaseTrust = force.BaseTrust - 1 });
                world.NextRandom("daily-fixture");
            }
        }
        private sealed class DecisionRule : TaskRule
        {
            internal override int DurationDays(PersistentTask task) => 2;
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

    [Fact]
    public void TwoSameDayDecisionsContinueInMemoryAndReplayWithoutRepeatingDailyWork()
    {
        var content = Content();
        var session = GameSession.Start(content, 42, Engine);
        session.BeginRecording();
        for (int actor = 1; actor <= 2; actor++)
            Assert.True(session.Execute(new("player"), new StartTaskCommand(Guid.NewGuid(), 1,
                session.Snapshot.Simulation.NextSequence, "decision-fixture", actor, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), content, Engine);
        restored.AdvanceXun();
        var advance = session.AdvanceXun();
        Assert.Equal(3, session.Snapshot.Clock.Day);
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        Assert.Equal(97, session.Snapshot.Forces[0].BaseTrust);
        Assert.DoesNotContain(advance.Events, e => e is XunAdvancedEvent);
        Assert.Equal(3, advance.Events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.BeforeDailyCommands));
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        Assert.Throws<InvalidOperationException>(() => session.EndRecording());
        Assert.True(session.IsRecording);
        for (int answer = 0; answer < 2; answer++)
        {
            var pending = session.Snapshot.Simulation.PendingDailyTask!;
            var command = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
                pending.RemainingTaskIds[0], 0, answer == 0);
            string beforeAnswer = session.Snapshot.StateHash;
            Assert.True(session.Validate(new("player"), command).Accepted);
            Assert.Equal(beforeAnswer, session.Snapshot.StateHash);
            var request = session.Request(new CommandOperation(command), id: command.CommandId);
            var expected = session.Apply(request);
            var actual = restored.Apply(request);
            Assert.Equal(SessionDisposition.Accepted, expected.Disposition);
            Assert.Equal(SessionDisposition.Accepted, actual.Disposition);
            Assert.Equal(CanonicalJson.Text(expected.Events), CanonicalJson.Text(actual.Events));
            Assert.Equal(session.Snapshot.StateHash, restored.Snapshot.StateHash);
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
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, Engine);
        Assert.Equal(session.Snapshot.StateHash, replay.Session.Snapshot.StateHash);
        Assert.Equal(10, replay.Events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.BeforeDailyCommands));
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
        string before = session.Snapshot.StateHash;
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            pending.RemainingTaskIds[0], 0, true);
        Assert.Equal(CommandRejection.NotActiveController, session.Execute(new("other"), response).Submission.Rejection);
        Assert.Equal(before, session.Snapshot.StateHash);
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
            Assert.Equal(before, session.Snapshot.StateHash);
            Assert.Equal(operationSequence, session.NextOperationSequence);
        }
        Assert.Equal(CommandRejection.DecisionPending, session.Execute(new("player"),
            new WaitCommand(Guid.NewGuid(), 1, response.Sequence)).Submission.Rejection);
        Assert.Equal(before, session.Snapshot.StateHash);
        Assert.Throws<InvalidOperationException>(() => session.AdvanceXun());
        Assert.Equal(before, session.Snapshot.StateHash);
    }
}
