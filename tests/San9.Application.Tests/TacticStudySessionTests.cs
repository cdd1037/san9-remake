using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class TacticStudySessionTests
{
    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "study", 190, 1, 1, 1,
        [new(1, "force.1", 1, 10000, 100000, 100) { LeaderId = 1 }, new(2, "force.2", 2, 10000, 100000, 100) { LeaderId = 4 }],
        [new(1, "city.1", 1, 0, 0, 100, 600, 600, 100, []), new(2, "city.2", 1, 2, 0, 100, 600, 600, 100, []),
         new(3, "city.3", 2, 4, 0, 100, 600, 600, 100, [])], [], [])
    {
        NaturalDeathsEnabled = false,
        Officers = [Person(1, 1, 1, 0, OfficerStatus.Ruler), Person(2, 1, 1, 100), Person(3, 1, 2, 100), Person(4, 2, 3, 0, OfficerStatus.Ruler)],
        TacticCourses = [new(1, 9000), new(2, 9001)],
        Military = new(6, 1, [0, 0, 0, 0, 0, 0], [new(0, 1, null)], [],
            [new(1, new(2, 2), new(1, 1)), new(2, new(12, 2), new(1, 1)), new(3, new(22, 2), new(1, 1))])
        {
            Tactics = [new(9000, 7, TacticEffect.CounterConfusion, 0, 0, 200, false, false, []),
                new(9001, 7, TacticEffect.CounterConfusion, 0, 0, 200, false, false, [])]
        }
    };

    private static OfficerInitialState Person(int id, int force, int city, int proficiency, OfficerStatus status = OfficerStatus.Serving) =>
        new($"officer.{id}", new(id, force, city, new(50, 50, 50, 50), false)
        { Status = status, TacticProficiencies = ImmutableArray.CreateRange(new int[9]).SetItem(7, proficiency) });

    private static CommandExecutionResult Choose(GameSession session, int city, int? person, string controller = "player", int force = 1) =>
        session.Execute(new(controller), new ChooseTacticStudentCommand(Guid.NewGuid(), force, session.Snapshot.Simulation.NextSequence, city, person));

    private static void ReachQuarter(GameSession session)
    {
        for (int i = 0; i < 8; i++)
        {
            session.AdvanceXun();
            Assert.Null(session.GetTacticStudyPrompt());
        }
        session.AdvanceXun();
    }

    [Fact]
    public void MultipleOffersSaveAndReplayThenGrantAtNextQuarterWithoutImmediateReenrollment()
    {
        var definition = Scenario();
        var session = GameSession.Start(definition, 190);
        Assert.Null(session.GetTacticStudyPrompt());
        ReachQuarter(session);
        Assert.Equal(new[] { 2 }, session.GetTacticStudyPrompt()!.CandidateIds);
        Assert.Equal(CommandRejection.NotActiveController, Choose(session, 1, 2, "ai:2", 2).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidDecision, Choose(session, 1, 1).Submission.Rejection);
        Assert.True(Choose(session, 1, 2).Submission.Accepted);
        Assert.Equal(2, session.GetTacticStudyPrompt()!.CityId);
        var restored = SaveService.Decode(SaveService.Encode(session), definition, GameSession.EngineFactory(definition));
        session.BeginRecording();
        var command = new ChooseTacticStudentCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 2, 3);
        var result = session.Execute(new("player"), command);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.Execute(new("player"), command).Events));
        Assert.DoesNotContain(result.Events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.QuarterStudyOffers);
        Assert.Single(result.Events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.QuarterUpdate);
        Assert.Equal(2, session.Snapshot.Simulation.Tasks.Count(t => t.Kind == "tactic-study"));
        Assert.Equal("tactic-study", session.View.Officers.Single(o => o.Id == 2).Journey!.Kind);
        Assert.Equal(CommandRejection.PersonUnavailable, session.Execute(new("player"),
            new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 2, 1)).Submission.Rejection);
        for (int i = 0; i < 8; i++)
        {
            Assert.Empty(session.AdvanceXun().Events.OfType<TacticLearnedEvent>());
            restored.AdvanceXun();
            if (i == 2)
            {
                restored = SaveService.Decode(SaveService.Encode(restored), definition, GameSession.EngineFactory(definition));
                Assert.All(restored.Snapshot.Simulation.Tasks, task => Assert.Equal(30, task.ProgressDays));
                Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
            }
        }
        Assert.Empty(session.Snapshot.Officers.Single(o => o.Id == 2).LearnedTactics);
        var completion = session.AdvanceXun();
        Assert.Equal(CanonicalJson.Text(completion.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(2, completion.Events.OfType<TacticLearnedEvent>().Count());
        Assert.Equal(new(190, 7, 1), session.Snapshot.Clock);
        Assert.Null(session.GetTacticStudyPrompt());
        Assert.Empty(session.Snapshot.Simulation.Tasks);
        Assert.Equal(100, session.Snapshot.Officers.Single(o => o.Id == 2).TacticProficiencies[7]);
        Assert.Contains(9000, session.Snapshot.Officers.Single(o => o.Id == 2).LearnedTactics);
        Assert.DoesNotContain(session.Snapshot.Simulation.RandomStreams, s => s.Name.Contains("study") || s.Name.Contains("learn"));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void DeclineAdvancesOffersAndIneligibleOrAlreadyLearnedOfficersAreNotOffered()
    {
        var definition = Scenario();
        definition = definition with { Officers = definition.Officers.Select(p => p.State.Id == 3 ? p with
            { State = p.State with { LearnedTactics = [9001] } } : p).ToArray() };
        var session = GameSession.Start(definition, 2);
        ReachQuarter(session);
        Assert.True(Choose(session, 1, null).Submission.Accepted);
        Assert.Null(session.GetTacticStudyPrompt());
        Assert.Empty(session.Snapshot.Simulation.Tasks);
        Assert.Equal(SimulationBoundary.Stable, session.Snapshot.Simulation.Boundary);
        var low = Scenario() with { Officers = Scenario().Officers.Select(p => p with
            { State = p.State with { TacticProficiencies = p.State.TacticProficiencies.SetItem(7, 99) } }).ToArray() };
        var noCandidates = GameSession.Start(low, 2);
        ReachQuarter(noCandidates);
        Assert.Null(noCandidates.GetTacticStudyPrompt());
    }

    [Theory]
    [InlineData(1, 3, true)]
    [InlineData(11, 2, false)]
    [InlineData(21, 1, false)]
    public void FirstQuarterRequiresThirtyElapsedDays(int startDay, int xun, bool offered)
    {
        var session = GameSession.Start(Scenario() with { Month = 3, Day = startDay }, 4);
        for (int i = 0; i < xun; i++) session.AdvanceXun();
        Assert.Equal(new(190, 4, 1), session.Snapshot.Clock);
        Assert.Equal(offered, session.GetTacticStudyPrompt() is not null);
    }

    [Fact]
    public void CourseReferencesAreValidatedAndParticipateInFingerprint()
    {
        var definition = Scenario();
        Assert.Throws<InvalidDataException>(() => (definition with { TacticCourses = [new(9999, 9000)] }).Validate());
        Assert.Throws<InvalidDataException>(() => (definition with { TacticCourses = [new(1, 9999)] }).Validate());
        Assert.Throws<InvalidDataException>(() => (definition with { TacticCourses = [new(1, 9000), new(1, 9001)] }).Validate());
        Assert.NotEqual(GameSession.Fingerprint(definition), GameSession.Fingerprint(definition with { TacticCourses = [] }));
        var json = JsonNode.Parse(JsonSerializer.Serialize(definition, ScenarioContentFiles.DataJsonOptions))!;
        json.AsObject().Remove("tacticCourses");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ScenarioInitialState>(json.ToJsonString(), ScenarioContentFiles.DataJsonOptions));
    }

    [Fact]
    public void CompletionPrecedesNaturalDeathAtTheSameQuarterBoundary()
    {
        var definition = Scenario();
        definition = definition with { NaturalDeathsEnabled = true, TacticCourses = [new(1, 9000)],
            Officers = definition.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with { BirthYear = 90 } } : p).ToArray() };
        var session = GameSession.Start(definition, 190);
        for (int quarter = 0; quarter < 3; quarter++)
        {
            ReachQuarter(session);
            Assert.True(Choose(session, 1, quarter == 2 ? 2 : null).Submission.Accepted);
        }
        for (int i = 0; i < 8; i++) session.AdvanceXun();
        var result = session.AdvanceXun();
        int learning = result.Events.ToList().FindIndex(e => e is TacticLearnedEvent { OfficerId: 2 });
        int death = result.Events.ToList().FindIndex(e => e is OfficerDiedEvent { OfficerId: 2 });
        Assert.True(learning >= 0 && death > learning);
        Assert.Equal(OfficerStatus.Dead, session.Snapshot.Officers.Single(p => p.Id == 2).Status);
        Assert.Empty(session.Snapshot.Simulation.Tasks);
    }

    [Fact]
    public void RestoreRejectsForgedDecisionAndCoursePayload()
    {
        var definition = Scenario();
        var session = GameSession.Start(definition, 3);
        ReachQuarter(session);
        var waiting = session.Capture();
        Assert.Throws<ArgumentException>(() => GameSession.Restore(definition, waiting with
        { World = waiting.World with { Simulation = waiting.World.Simulation with { PendingTacticStudy = new(3, 2) } } }));
        var json = JsonNode.Parse(JsonSerializer.Serialize(waiting.World, CanonicalJson.Options))!;
        json["simulation"]!.AsObject().Remove("pendingTacticStudy");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorldSnapshot>(json.ToJsonString(), CanonicalJson.Options));
        Assert.True(Choose(session, 1, 2).Submission.Accepted);
        Assert.True(Choose(session, 2, null).Submission.Accepted);
        var studying = session.Capture();
        var task = Assert.Single(studying.World.Simulation.Tasks);
        Assert.Throws<ArgumentException>(() => GameSession.Restore(definition, studying with
        { World = studying.World with { Simulation = studying.World.Simulation with { Tasks = [task with { StudyTacticId = 9001 }] } } }));
        Assert.Throws<ArgumentException>(() => GameSession.Restore(definition, studying with
        { World = studying.World with { Simulation = studying.World.Simulation with { Tasks = [task with { Due = task.Due.NextDay() }] } } }));
    }
}
