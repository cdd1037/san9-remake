using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class DailyTaskDecisionTests
{
    private static ScenarioInitialState Content() => new(ScenarioInitialState.CurrentSchemaVersion, "daily-decisions", 190, 1, 1, 1,
        [new(1, "first", 1, 1000, 1000, 100) { BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], [], [])
    {
        Officers = Enumerable.Range(1, 3).Select(id => new OfficerInitialState($"officer.{id}",
            new(id, 1, 1, new(50, 50, 50, 50), false))).ToArray()
    };

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
    public void TwoSameDayDecisionsSaveReplayAndContinueWithoutRepeatingDailyWork()
    {
        var content = Content();
        var session = GameSession.Start(content, 42, Engine);
        session.BeginRecording();
        for (int actor = 1; actor <= 2; actor++)
            Assert.True(session.Execute(new("player"), new StartTaskCommand(Guid.NewGuid(), 1,
                session.Snapshot.Simulation.NextSequence, "decision-fixture", actor, 1)).Submission.Accepted);
        var advance = session.AdvanceXun();
        Assert.Equal(3, session.Snapshot.Clock.Day);
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        Assert.Equal(97, session.Snapshot.Forces[0].BaseTrust);
        Assert.DoesNotContain(advance.Events, e => e is XunAdvancedEvent);
        Assert.Equal(3, advance.Events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.BeforeDailyCommands));
        var restored = SaveService.Decode(SaveService.Encode(session), content, Engine);
        for (int answer = 0; answer < 2; answer++)
        {
            var pending = session.Snapshot.Simulation.PendingDailyTask!;
            var command = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
                pending.RemainingTaskIds[0], answer == 0);
            string beforeAnswer = session.Capture().StateHash;
            Assert.True(session.Validate(new("player"), command).Accepted);
            Assert.Equal(beforeAnswer, session.Capture().StateHash);
            var expected = session.Execute(new("player"), command);
            var actual = restored.Execute(new("player"), command);
            Assert.True(expected.Submission.Accepted);
            Assert.Equal(CanonicalJson.Text(expected.Events), CanonicalJson.Text(actual.Events));
            Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
            Assert.Equal(answer == 0 ? 0 : 1, expected.Events.OfType<XunAdvancedEvent>().Count());
            if (answer == 0)
            {
                Assert.Equal(3, session.Snapshot.Clock.Day);
                Assert.Equal(97, session.Snapshot.Forces[0].BaseTrust);
                Assert.DoesNotContain(expected.Events, e => e is StageCompletedEvent { Stage: ResolutionStage.BeforeDailyCommands });
                restored = SaveService.Decode(SaveService.Encode(session), content, Engine);
            }
        }
        Assert.Equal(11, session.Snapshot.Clock.Day);
        Assert.Equal(90, session.Snapshot.Forces[0].BaseTrust);
        Assert.Equal(11, session.Snapshot.Settlements[0].PublicOrder);
        Assert.Empty(session.Snapshot.Simulation.Tasks);
        Assert.Null(session.Snapshot.Simulation.PendingDailyTask);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, Engine);
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
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
        var saved = session.Capture();
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
        var json = System.Text.Json.Nodes.JsonNode.Parse(SaveService.Encode(session))!;
        Assert.True(json["session"]!["world"]!["simulation"]!.AsObject().Remove("pendingDailyTask"));
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => SaveService.Decode(
            System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), Content(), Engine)).Failure);
    }

    [Fact]
    public void WrongControllerWrongTaskAndNormalCommandsCannotMutatePendingDecision()
    {
        var session = GameSession.Start(Content(), 42, Engine);
        Assert.True(session.Execute(new("player"), new StartTaskCommand(Guid.NewGuid(), 1, 0,
            "decision-fixture", 1, 1)).Submission.Accepted);
        session.AdvanceXun();
        var pending = session.Snapshot.Simulation.PendingDailyTask!;
        string before = session.Capture().StateHash;
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            pending.RemainingTaskIds[0], true);
        Assert.Equal(CommandRejection.NotActiveController, session.Execute(new("other"), response).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidDecision, session.Execute(new("player"), response with { TaskId = Guid.NewGuid() }).Submission.Rejection);
        Assert.Equal(CommandRejection.DecisionPending, session.Execute(new("player"),
            new WaitCommand(Guid.NewGuid(), 1, response.Sequence)).Submission.Rejection);
        Assert.Equal(before, session.Capture().StateHash);
        Assert.Throws<InvalidOperationException>(() => session.AdvanceXun());
        Assert.Equal(before, session.Capture().StateHash);
    }
}
