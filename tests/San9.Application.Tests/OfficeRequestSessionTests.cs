using System.Collections.Immutable;
using San9.Application.AI;
using San9.Tests;
using San9.Application.Persistence;
using San9.Application.Diagnostics;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class OfficeRequestSessionTests
{
    [Theory]
    [InlineData(0, EventChoice.Accept)]
    [InlineData(50, EventChoice.Reject)]
    public void AutomaticOfficeRequestUsesAffinityProbability(int distance, EventChoice expected)
    {
        var scenario = Scenario();
        var session = GameSession.Start(scenario, 190);
        session.AdvanceXun(); session.AdvanceXun(); session.AdvanceXun();
        var snapshot = session.Snapshot;
        var engine = GameSession.EngineFactory(scenario)(WorldState.Restore(snapshot with
        {
            Officers = snapshot.Officers.Select(p => p.Id == 9000 ? p with { Affinity = distance } :
                p.Id == 1 ? p with { Affinity = 0 } : p).ToImmutableList(),
            Simulation = snapshot.Simulation with { Controls = snapshot.Simulation.Controls.Select(c => c.ForceId == 1
                ? c with { Kind = ControllerKind.Automatic, ControllerId = "ai:1" } : c).ToImmutableArray() }
        }));
        var response = AutomaticResponseRunner.Choose(engine, engine.GetPendingResponse()!);
        Assert.Equal(expected, Assert.IsType<RespondOfficeRequestCommand>(response).Choice);
    }

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
        EventAssert.Equal(session.AdvanceXun().Events, restored.AdvanceXun().Events);
        Assert.Equal(new OfficeRequestPrompt(9000, 1, 1), session.GetOfficeRequestPrompt());
        var waitingHash = session.Snapshot;
        Assert.Throws<SaveException>(() => SaveService.Encode(session));
        Assert.Null(SessionDiagnostics.Compare(session, restored));
        _ = session.GetOfficeRequestPrompt();
        StateAssert.Equal(waitingHash, session.Snapshot);
        var command = new RespondOfficeRequestCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 9000, choice);
        var request = session.Request(new CommandOperation(command), "player", command.CommandId);
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.NotNull(SessionDiagnostics.Compare(session, restored));
        EventAssert.Equal(result.Events, restored.Apply(request).Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        var saved = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        Assert.Empty(saved.InitialEvents);
        StateAssert.Equal(session.Capture(), saved.Capture());
        Assert.Equal(choice == EventChoice.Accept ? 1 : (int?)null, saved.Snapshot.Officers.Single(p => p.Id == 9000).OwnerId);
    }

    [Fact]
    public void OfficeRequestSwitchIsIndependentAndRetainsSavedConfiguration()
    {
        var scenario = Scenario() with { OfficeRequestsEnabled = false };
        var session = GameSession.Start(scenario, 190);
        for (int i = 0; i < 4; i++) Assert.Empty(session.AdvanceXun().Events.OfType<OfficeRequestOfferedEvent>());
        Assert.Null(session.GetOfficeRequestPrompt());
        Assert.DoesNotContain(session.Snapshot.Simulation.RandomStreams, r => r.Name.StartsWith("event-office-request"));

        var captured = session.Capture();
        Assert.False(GameSession.Restore(scenario with { OfficeRequestsEnabled = true }, captured)
            .Snapshot.Simulation.OfficeRequestsEnabled);
    }
    [Fact]
    public void AutomaticOfficeRequestResumesWithNoOptionalPlanningBudget()
    {
        var scenario = Scenario() with { PlayerForceId = 2 };
        var session = GameSession.Start(scenario, 190);
        var events = AutomaticResponseVerification.Advance(session, scenario, 3);
        Assert.Contains(events.OfType<OfficeRequestResolvedEvent>(), e => e.OfficerId == 9000 && e.Choice == EventChoice.Accept);
        Assert.Equal(1, session.Snapshot.Officers.Single(p => p.Id == 9000).OwnerId);
    }
}
