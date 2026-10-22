using San9.Tests;
using System.Text.Json;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class StagedDailyTaskTests
{
    private static ScenarioInitialState Content() => new(ScenarioInitialState.CurrentSchemaVersion, "staged-decisions", 190, 1, 1, 1,
        [new(1, "first", 1, 100)],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], [], [])
    {
        Officers = [new("first", new(1, 1, 1, new(50, 50, 50, 50), false)),
            new("second", new(2, 1, 1, new(50, 50, 50, 50), false))]
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 1000)) { IsRoot = true }], []) };

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
                ResourceAccounts.Change(world, new(ResourceOwnerKind.Corps, 1), ResourceKind.Money, -100,
                    "fixture-settlement", task.OfficerId, events, true);
                return null;
            }
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void RetainsTaskAcrossInMemoryDecisionsAndReplaysWithoutRepeatingDailyWork(bool automaticFinal, bool accept)
    {
        var content = Content();
        SimulationEngine Factory(WorldState world) => new(world, new Rules(automaticFinal));
        var session = GameSession.Start(content, 42, Factory);
        Guid taskId = Guid.NewGuid();
        Assert.True(session.Execute(new("player"), new StartTaskCommand(taskId, 1, 0, "staged-fixture", 1, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), content, Factory);
        restored.AdvanceXun();
        uint beforeMoney = session.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money;
        var advance = session.AdvanceXun();
        List<DomainEvent> events = [.. advance.Events];
        Assert.Equal(3, session.Snapshot.Clock.Day);
        Assert.Equal(1, Assert.Single(session.Snapshot.Simulation.Tasks).ResolutionStep);
        Assert.DoesNotContain(advance.Events, e => e is TaskCompletedEvent);
        var first = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, taskId, 1, true);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        var answered = session.Execute(new("player"), first);
        events.AddRange(answered.Events);
        Assert.True(answered.Submission.Accepted);
        EventAssert.Equal(answered.Events, restored.Execute(new("player"), first).Events);
        StateAssert.Equal(session.Snapshot, restored.Snapshot);
        if (!automaticFinal)
        {
            Assert.Equal(3, session.Snapshot.Clock.Day);
            Assert.Equal(2, Assert.Single(session.Snapshot.Simulation.Tasks).ResolutionStep);
            Assert.DoesNotContain(answered.Events, e => e is TaskCompletedEvent or XunAdvancedEvent or StageCompletedEvent);
            Assert.Equal(beforeMoney, session.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money);
            var beforeInvalid = session.Snapshot;
            var stale = first with { CommandId = Guid.NewGuid(), Sequence = session.Snapshot.Simulation.NextSequence };
            Assert.Equal(CommandRejection.InvalidDecision, session.Execute(new("player"), stale).Submission.Rejection);
            StateAssert.Equal(beforeInvalid, session.Snapshot);
            Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
            var final = stale with { CommandId = Guid.NewGuid(), Step = 2, Accept = accept };
            answered = session.Execute(new("player"), final);
            events.AddRange(answered.Events);
            Assert.True(answered.Submission.Accepted);
            EventAssert.Equal(answered.Events, restored.Execute(new("player"), final).Events);
            StateAssert.Equal(session.Snapshot, restored.Snapshot);
        }
        Assert.Single(answered.Events.OfType<TaskCompletedEvent>(), e => e.TaskId == taskId);
        Assert.Single(answered.Events.OfType<XunAdvancedEvent>());
        Assert.Equal(10, events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.BeforeDailyCommands));
        Assert.Empty(session.Snapshot.Simulation.Tasks);
        // The two ordinary officers also receive their next-xun salary after continuation.
        Assert.Equal(beforeMoney - 2 - (accept ? 100u : 0u), session.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money);
    }

    [Fact]
    public void RespondentChangesBetweenStepsAndInvalidatedMissionCancelsWithoutPayment()
    {
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100), new(2, 100)],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 0, 0, 0)], [new(1, 1, 1, new(50, 50, 50, 50), false)],
            controls: [new("first", 1, ControllerKind.Player), new("second", 2, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(1000, 1000)) { IsRoot = true }]);
        var engine = new SimulationEngine(world, new Rules(switchRespondent: true));
        Guid id = Guid.NewGuid();
        Assert.True(engine.Execute(new("first"), new StartTaskCommand(id, 1, 0, "staged-fixture", 1, 1)).Submission.Accepted);
        engine.EndControl(new("first"));
        engine.EndControl(new("second"));
        engine.AdvanceXun();
        Assert.True(engine.Execute(new("first"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, 1, id, 1, true)).Submission.Accepted);
        Assert.Equal(2, engine.Snapshot.Simulation.PendingDailyTask!.RespondingForceId);
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 2, 2, id, 2, true);
        var before = engine.Snapshot;
        Assert.Equal(CommandRejection.NotActiveController, engine.Execute(new("first"), response).Submission.Rejection);
        StateAssert.Equal(before, engine.Snapshot);
        var snapshot = engine.Snapshot;
        snapshot = snapshot with { Settlements = snapshot.Settlements.Select(s => s with { PublicOrder = 100 }).ToArray() };
        engine = new(WorldState.Restore(snapshot), new Rules(switchRespondent: true));
        var cancelled = engine.Execute(new("second"), response);
        Assert.True(cancelled.Submission.Accepted);
        Assert.Single(cancelled.Events.OfType<TaskCancelledEvent>(), e => e.TaskId == id);
        Assert.DoesNotContain(cancelled.Events, e => e is TaskCompletedEvent done && done.TaskId == id);
        Assert.Equal(998u, engine.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 1).Treasury.Money);
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
    public void SaveOmitsResolutionCursorAndCompiledRuleRejectsUnknownRuntimeSteps()
    {
        var content = Content();
        SimulationEngine Factory(WorldState world) => new(world, new Rules());
        var session = GameSession.Start(content, 42, Factory);
        Assert.True(session.Execute(new("player"), new StartTaskCommand(Guid.NewGuid(), 1, 0, "staged-fixture", 1, 1)).Submission.Accepted);
        var json = System.Text.Json.Nodes.JsonNode.Parse(SaveService.Encode(session))!;
        Assert.False(json["session"]!["world"]!["simulation"]!["tasks"]![0]!.AsObject().ContainsKey("resolutionStep"));
        session.AdvanceXun();
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        var snapshot = session.Snapshot;
        snapshot = snapshot with { Simulation = snapshot.Simulation with
            { Tasks = [snapshot.Simulation.Tasks[0] with { ResolutionStep = 3 }] } };
        Assert.Throws<ArgumentException>(() => Factory(WorldState.Restore(snapshot)));
    }
}
