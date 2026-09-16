using System.Text.Json;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class StagedDailyTaskTests
{
    private static ScenarioInitialState Content() => new(ScenarioInitialState.CurrentSchemaVersion, "staged-decisions", 190, 1, 1, 1,
        [new(1, "first", 1, 1000, 1000, 100)],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], [], [])
    {
        Officers = [new("first", new(1, 1, 1, new(50, 50, 50, 50), false)),
            new("second", new(2, 1, 1, new(50, 50, 50, 50), false))]
    };

    // A scheduling fixture: automatic preparation, first decision, final decision/payment.
    // Actual diplomacy offer eligibility and settlement belong to production-command tests.
    private sealed class Rules(bool automaticFinal = false, bool switchRespondent = false) : SimulationRules
    {
        internal override TaskRule? FindTask(string kind) => kind == "staged-fixture"
            ? new Rule(automaticFinal, switchRespondent) : base.FindTask(kind);

        private sealed class Rule(bool automaticFinal, bool switchRespondent) : TaskRule
        {
            internal override bool SupportsResolutionStep(int step) => step is >= 0 and <= 2;
            internal override int DurationDays(PersistentTask task) => 2;
            internal override bool Validate(WorldMutation world, PersistentTask task) => world.Settlements[1].PublicOrder < 100;
            internal override int? DecisionRecipient(WorldMutation world, PersistentTask task) => task.ResolutionStep switch
            {
                0 => null,
                1 => 1,
                _ => automaticFinal ? null : switchRespondent ? 2 : 1
            };
            internal override PersistentTask? Resolve(WorldMutation world, PersistentTask task, bool? accept, List<DomainEvent> events)
            {
                world.NextRandom("staged-fixture");
                if (accept == false) return null;
                if (task.ResolutionStep < 2) return task;
                ResourceAccounts.Change(world, new(ResourceOwnerKind.Force, 1), ResourceKind.Money, -100,
                    "fixture-settlement", task.OfficerId, events, true);
                return null;
            }
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void RetainsTaskAcrossDecisionsAndSavesEachStepWithoutRepeatingDailyWork(bool automaticFinal, bool accept)
    {
        var content = Content();
        SimulationEngine Factory(WorldState world) => new(world, new Rules(automaticFinal));
        var session = GameSession.Start(content, 42, Factory);
        session.BeginRecording();
        Guid taskId = Guid.NewGuid();
        Assert.True(session.Execute(new("player"), new StartTaskCommand(taskId, 1, 0, "staged-fixture", 1, 1)).Submission.Accepted);
        uint beforeMoney = session.Snapshot.Forces[0].Money;
        var advance = session.AdvanceXun();
        Assert.Equal(3, session.Snapshot.Clock.Day);
        Assert.Equal(1, Assert.Single(session.Snapshot.Simulation.Tasks).ResolutionStep);
        Assert.DoesNotContain(advance.Events, e => e is TaskCompletedEvent);
        var first = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, taskId, 1, true);
        var restored = SaveService.Decode(SaveService.Encode(session), content, Factory);
        var answered = session.Execute(new("player"), first);
        Assert.True(answered.Submission.Accepted);
        Assert.Equal(CanonicalJson.Text(answered.Events), CanonicalJson.Text(restored.Execute(new("player"), first).Events));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        if (!automaticFinal)
        {
            Assert.Equal(3, session.Snapshot.Clock.Day);
            Assert.Equal(2, Assert.Single(session.Snapshot.Simulation.Tasks).ResolutionStep);
            Assert.DoesNotContain(answered.Events, e => e is TaskCompletedEvent or XunAdvancedEvent or StageCompletedEvent);
            Assert.Equal(beforeMoney, session.Snapshot.Forces[0].Money);
            string beforeInvalid = session.Capture().StateHash;
            var stale = first with { CommandId = Guid.NewGuid(), Sequence = session.Snapshot.Simulation.NextSequence };
            Assert.Equal(CommandRejection.InvalidDecision, session.Execute(new("player"), stale).Submission.Rejection);
            Assert.Equal(beforeInvalid, session.Capture().StateHash);
            restored = SaveService.Decode(SaveService.Encode(session), content, Factory);
            var final = stale with { CommandId = Guid.NewGuid(), Step = 2, Accept = accept };
            answered = session.Execute(new("player"), final);
            Assert.True(answered.Submission.Accepted);
            Assert.Equal(CanonicalJson.Text(answered.Events), CanonicalJson.Text(restored.Execute(new("player"), final).Events));
            Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        }
        Assert.Single(answered.Events.OfType<TaskCompletedEvent>(), e => e.TaskId == taskId);
        Assert.Single(answered.Events.OfType<XunAdvancedEvent>());
        Assert.Empty(session.Snapshot.Simulation.Tasks);
        // The two ordinary officers also receive their next-xun salary after continuation.
        Assert.Equal(beforeMoney - 2 - (accept ? 100u : 0u), session.Snapshot.Forces[0].Money);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, Factory);
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
        Assert.Equal(10, replay.Events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.BeforeDailyCommands));
        Assert.Single(replay.Events.OfType<TaskCompletedEvent>(), e => e.TaskId == taskId);
    }

    [Fact]
    public void RespondentChangesBetweenStepsAndInvalidatedMissionCancelsWithoutPayment()
    {
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 1000, 1000, 100), new(2, 1000, 1000, 100)],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 0, 0, 0)], [new(1, 1, 1, new(50, 50, 50, 50), false)],
            controls: [new("first", 1, ControllerKind.Player), new("second", 2, ControllerKind.Player)]);
        var engine = new SimulationEngine(world, new Rules(switchRespondent: true));
        Guid id = Guid.NewGuid();
        Assert.True(engine.Execute(new("first"), new StartTaskCommand(id, 1, 0, "staged-fixture", 1, 1)).Submission.Accepted);
        engine.EndControl(new("first"));
        engine.EndControl(new("second"));
        engine.AdvanceXun();
        Assert.True(engine.Execute(new("first"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, 1, id, 1, true)).Submission.Accepted);
        Assert.Equal(2, engine.Snapshot.Simulation.PendingDailyTask!.RespondingForceId);
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 2, 2, id, 2, true);
        string before = engine.Snapshot.StateHash;
        Assert.Equal(CommandRejection.NotActiveController, engine.Execute(new("first"), response).Submission.Rejection);
        Assert.Equal(before, engine.Snapshot.StateHash);
        var snapshot = JsonSerializer.Deserialize<WorldSnapshot>(CanonicalJson.Text(engine.Snapshot), CanonicalJson.Options)!;
        snapshot = snapshot with { Settlements = snapshot.Settlements.Select(s => s with { PublicOrder = 100 }).ToArray() };
        engine = new(WorldState.Restore(snapshot), new Rules(switchRespondent: true));
        var cancelled = engine.Execute(new("second"), response);
        Assert.True(cancelled.Submission.Accepted);
        Assert.Single(cancelled.Events.OfType<TaskCancelledEvent>(), e => e.TaskId == id);
        Assert.DoesNotContain(cancelled.Events, e => e is TaskCompletedEvent done && done.TaskId == id);
        Assert.Equal(998u, engine.Snapshot.Forces.Single(f => f.Id == 1).Money);
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
    }

    [Fact]
    public void MultipleStagedMissionsKeepTheirSameDayQueueUntilEachFinishes()
    {
        SimulationEngine Factory(WorldState world) => new(world, new Rules());
        var session = GameSession.Start(Content(), 42, Factory);
        List<Guid> ids = [Guid.NewGuid(), Guid.NewGuid()];
        for (int i = 0; i < 2; i++)
            Assert.True(session.Execute(new("player"), new StartTaskCommand(ids[i], 1, i,
                "staged-fixture", i + 1, 1)).Submission.Accepted);
        session.AdvanceXun();
        List<DomainEvent> events = [];
        for (int answer = 0; answer < 4; answer++)
        {
            Assert.Equal(3, session.Snapshot.Clock.Day);
            Assert.Equal(ids[answer / 2], session.Snapshot.Simulation.PendingDailyTask!.RemainingTaskIds[0]);
            var result = session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
                session.Snapshot.Simulation.NextSequence, ids[answer / 2], answer % 2 + 1, true));
            Assert.True(result.Submission.Accepted);
            events.AddRange(result.Events);
            if (answer < 3) Assert.DoesNotContain(result.Events, e => e is StageCompletedEvent or XunAdvancedEvent);
        }
        Assert.Equal(ids, events.OfType<TaskCompletedEvent>().Select(e => e.TaskId));
        Assert.Single(events.OfType<XunAdvancedEvent>());
        Assert.Equal(11, session.Snapshot.Clock.Day);
        Assert.Empty(session.Snapshot.Simulation.Tasks);
    }

    [Fact]
    public void SaveRequiresResolutionStepAndCompiledRuleRejectsUnknownSteps()
    {
        var content = Content();
        SimulationEngine Factory(WorldState world) => new(world, new Rules());
        var session = GameSession.Start(content, 42, Factory);
        Assert.True(session.Execute(new("player"), new StartTaskCommand(Guid.NewGuid(), 1, 0, "staged-fixture", 1, 1)).Submission.Accepted);
        session.AdvanceXun();
        var json = System.Text.Json.Nodes.JsonNode.Parse(SaveService.Encode(session))!;
        Assert.True(json["session"]!["world"]!["simulation"]!["tasks"]![0]!.AsObject().Remove("resolutionStep"));
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => SaveService.Decode(
            System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), content, Factory)).Failure);
        var snapshot = session.Snapshot;
        snapshot = snapshot with { Simulation = snapshot.Simulation with
            { Tasks = [snapshot.Simulation.Tasks[0] with { ResolutionStep = 3 }] } };
        Assert.Throws<ArgumentException>(() => Factory(WorldState.Restore(snapshot)));
    }
}
