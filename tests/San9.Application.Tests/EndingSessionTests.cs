using System.Collections.Immutable;
using System.Text.Json;
using San9.Application.Persistence;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class EndingSessionTests
{
    internal static ScenarioInitialState Fixture() => G01ScenarioTests.Fixture() with
    {
        Terminal = new([1], 350),
        Ending = new([1], [2], [1], [1], [2], [1], []),
        Officers = G01ScenarioTests.Fixture().Officers.Concat(new[] {
            new OfficerInitialState("officer.7", new(7, 1, 1, new(90, 80, 80, 80), false)),
            new OfficerInitialState("officer.8", new(8, 1, 1, new(90, 80, 80, 80), false)) }).ToArray()
    };

    [Fact]
    public void VictoryPromptIncludesForceAndTreasureTacticsAfterSaveRestore()
    {
        var original = Fixture();
        var content = original with
        {
            Forces = original.Forces.Select(f => f with { ProvidedTactics = [0, 1] }).ToArray(),
            Officers = original.Officers.Select(o => o with { State = o.State with { LearnedTactics = [0] } }).ToArray(),
            Information = new([], [], [new(900, "treasure", 0, 0, 0, 2)], original.Information.Corps,
                [new(900, 2, TreasureDiscovery.Discovered, null)])
        };
        var session = GameSession.Start(content, 1);
        string hash = session.Capture().StateHash;
        var prompt = session.GetEndingPrompt()!;
        Assert.Equal(3, prompt.Officers.Single(o => o.Id == 2).LearnedTacticCount);
        Assert.All(prompt.Officers.Where(o => o.Id != 2), o => Assert.Equal(2, o.LearnedTacticCount));
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.Equal(prompt.Officers.ToArray(), restored.GetEndingPrompt()!.Officers.ToArray());
        Assert.All(restored.Snapshot.Officers, o => Assert.Equal([0], o.LearnedTactics.ToArray()));
        Assert.Equal(hash, session.Capture().StateHash);
    }

    [Fact]
    public void VictoryChoicePersistsAndReplaysWithoutAdvancingWorld()
    {
        var content = Fixture();
        var session = GameSession.Start(content, 1);
        Assert.Equal(TerminalRoute.PlayerVictoryEnding, session.GetTerminalResult().Route);
        Assert.Equal(5, session.GetEndingPrompt()!.Officers.Length);
        Assert.Null(session.Ending);
        var snapshot = session.Snapshot;
        session = SaveService.Decode(SaveService.Encode(session), content, GameSession.EngineFactory(content));
        session.BeginRecording();
        var request = session.Request(new EndingOperation(new(EndingPolicy.ContinentalExpansion, new(2, 3, 7, 8))));
        Assert.Equal(SessionDisposition.Accepted, session.Apply(request).Disposition);
        Assert.Equal(EndingOutcome.SouthernDynasty, session.Ending!.Outcome);
        Assert.Null(session.GetEndingPrompt());
        Assert.Equal(snapshot.StateHash, session.Snapshot.StateHash);
        Assert.Equal(snapshot.Clock, session.Snapshot.Clock);
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        Assert.Equal(SessionDisposition.Rejected, session.Apply(session.Request(request.Operation)).Disposition);
        Assert.Equal(SessionRejection.Terminal, session.Apply(session.Request(new AdvanceOperation())).Rejection);
        var recording = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        Assert.Equal(session.Capture().StateHash, recording.Session.Capture().StateHash);
        Assert.Equal(session.Ending, recording.Session.Ending);
        var restored = SaveService.Decode(SaveService.Encode(session), content, GameSession.EngineFactory(content));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Equal(session.Ending, restored.Ending);
    }

    [Fact]
    public void InvalidCourtAndControllerLeaveSelectionAndWatermarkUntouched()
    {
        var session = GameSession.Start(Fixture(), 1);
        string hash = session.Capture().StateHash;
        foreach (var selection in new[] { new EndingSelection(EndingPolicy.ContinentalExpansion, new(2, 2, 7, 8)),
            new(EndingPolicy.ContinentalExpansion, new(1, 3, 7, 8)), new((EndingPolicy)10, new(2, 3, 7, 8)),
            new(EndingPolicy.ContinentalExpansion, new(2, 3, 7, 99)) })
            Assert.Equal(SessionDisposition.Rejected, session.Apply(session.Request(new EndingOperation(selection))).Disposition);
        Assert.Equal(SessionDisposition.Rejected, session.Apply(session.Request(new EndingOperation(new(EndingPolicy.DomesticProsperity, new(2, 3, 7, 8))), "ai:2")).Disposition);
        Assert.Equal(hash, session.Capture().StateHash);
    }

    [Fact]
    public void NonVictoryCannotSelectEndingAndSmallForceHasNoOfficeholderRequirement()
    {
        var content = Fixture() with { Terminal = new([2], 350) };
        var computerVictory = GameSession.Start(content, 1);
        Assert.Equal(TerminalRoute.ReturnToTitle, computerVictory.GetTerminalResult().Route);
        Assert.Null(computerVictory.GetEndingPrompt());
        Assert.Equal(SessionDisposition.Rejected, computerVictory.Apply(computerVictory.Request(new EndingOperation(new(EndingPolicy.DomesticProsperity, null)))).Disposition);
        var small = GameSession.Start(Fixture() with { Officers = G01ScenarioTests.Fixture().Officers }, 1);
        Assert.Equal(SessionDisposition.Accepted, small.Apply(small.Request(new EndingOperation(new(EndingPolicy.DomesticProsperity, null)))).Disposition);
        Assert.Equal(EndingOutcome.FallenState, small.Ending!.Outcome);
    }

    [Fact]
    public void ContentReferencesAreValidatedFingerprintedAndRequiredInSerializedContent()
    {
        var content = Fixture();
        Assert.NotEqual(GameSession.Fingerprint(content), GameSession.Fingerprint(content with { Terminal = new([2], 350) }));
        Assert.NotEqual(GameSession.Fingerprint(content), GameSession.Fingerprint(content with { Ending = content.Ending! with { DomesticGatewayCityIds = [1] } }));
        var reordered = content with { Terminal = new([1, 2], 350) };
        Assert.Equal(GameSession.Fingerprint(reordered), GameSession.Fingerprint(reordered with { Terminal = new([2, 1], 350) }));
        Assert.Throws<InvalidDataException>(() => (content with { Terminal = new([999], 350) }).Validate());
        Assert.Throws<InvalidDataException>(() => (content with { Ending = content.Ending! with { CulturalTreasureIds = [999] } }).Validate());
        var json = JsonSerializer.SerializeToNode(content, ScenarioContentFiles.DataJsonOptions)!;
        json.AsObject().Remove("ending");
        Assert.Throws<JsonException>(() => json.Deserialize<ScenarioInitialState>(ScenarioContentFiles.DataJsonOptions));
    }

    [Fact]
    public void RestoreRejectsForgedEndingAndPrematureYearLimit()
    {
        var content = Fixture();
        var session = GameSession.Start(content, 1);
        var saved = session.Capture();
        Assert.Throws<ArgumentException>(() => GameSession.Restore(content, saved with
        { Ending = new(EndingPolicy.ContinentalExpansion, new(2, 3, 7, 99)) }));
        Assert.Throws<ArgumentException>(() => GameSession.Restore(content, saved with
        { World = saved.World with { Simulation = saved.World.Simulation with { TerminalReason = "time-limit" } } }));
        var activeContent = content with { Terminal = new([1, 2], 350) };
        var active = GameSession.Start(activeContent, 1).Capture();
        Assert.Throws<ArgumentException>(() => GameSession.Restore(activeContent, active with
        { Ending = new(EndingPolicy.ContinentalExpansion, new(2, 3, 7, 8)) }));
    }
}
