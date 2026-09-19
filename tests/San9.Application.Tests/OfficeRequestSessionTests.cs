using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class OfficeRequestSessionTests
{
    [Theory]
    [InlineData("officerId")]
    [InlineData("choice")]
    public void MissingAnswerFieldsCannotBecomeImplicitAcceptance(string property)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(CanonicalJson.Text<GameCommand>(
            new RespondOfficeRequestCommand(Guid.NewGuid(), 1, 0, 9000, EventChoice.Accept)))!.AsObject();
        Assert.True(json.Remove(property));
        Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<GameCommand>(json.ToJsonString(),
                new System.Text.Json.JsonSerializerOptions(CanonicalJson.Options) { AllowOutOfOrderMetadataProperties = true }));
    }

    private static ScenarioInitialState Scenario() => G01ScenarioTests.Fixture() with
    {
        FreeOfficerMigrationEnabled = false,
        Officers = [.. G01ScenarioTests.Fixture().Officers,
            new("free", new OfficerState(9000, null, 1, new(50, 50, 50, 50), false)
                { Status = OfficerStatus.Free, BirthYear = 170 }.WithLoyalty(1, 255))]
    };

    [Theory]
    [InlineData(EventChoice.Accept)]
    [InlineData(EventChoice.Reject)]
    public void PublicChoicesRoundTripAndReplayFromStableBoundary(EventChoice choice)
    {
        var scenario = Scenario();
        var session = GameSession.Start(scenario, 190);
        session.AdvanceXun(); session.AdvanceXun();
        var restored = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        session.BeginRecording();
        Assert.Equal(CanonicalJson.Text(session.AdvanceXun().Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(new OfficeRequestPrompt(9000, 1, 1), session.GetOfficeRequestPrompt());
        string waitingHash = session.Snapshot.StateHash;
        Assert.Throws<SaveException>(() => SaveService.Encode(session));
        Assert.Throws<InvalidOperationException>(() => session.CaptureRecording());
        _ = session.GetOfficeRequestPrompt();
        Assert.Equal(waitingHash, session.Snapshot.StateHash);
        var command = new RespondOfficeRequestCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 9000, choice);
        var request = session.Request(new CommandOperation(command), "player", command.CommandId);
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.Apply(request).Events));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario, GameSession.EngineFactory(scenario));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
        var saved = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        Assert.Empty(saved.InitialEvents);
        Assert.Equal(session.Capture().StateHash, saved.Capture().StateHash);
        Assert.Equal(choice == EventChoice.Accept ? 1 : (int?)null, saved.Snapshot.Officers.Single(p => p.Id == 9000).OwnerId);
    }

    [Fact]
    public void ContentToggleIsIndependentFingerprintedAndValidatedOnRestore()
    {
        var scenario = Scenario() with { OfficeRequestsEnabled = false };
        var session = GameSession.Start(scenario, 190);
        for (int i = 0; i < 4; i++) Assert.Empty(session.AdvanceXun().Events.OfType<OfficeRequestOfferedEvent>());
        Assert.Null(session.GetOfficeRequestPrompt());
        Assert.DoesNotContain(session.Snapshot.Simulation.RandomStreams, r => r.Name.StartsWith("event-office-request"));
        Assert.NotEqual(session.ContentFingerprint, GameSession.Fingerprint(scenario with { OfficeRequestsEnabled = true }));
        var captured = session.Capture();
        Assert.Throws<ArgumentException>(() => GameSession.Restore(scenario, captured with
        { World = captured.World with { Simulation = captured.World.Simulation with { OfficeRequestsEnabled = true } } }));
    }
}
