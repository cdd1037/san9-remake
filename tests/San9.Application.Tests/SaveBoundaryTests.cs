using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class SaveBoundaryTests
{
    [Fact]
    public void StableSaveOmitsInteractionStateAndRejectsForgedDecisionBoundary()
    {
        var definition = G01ScenarioTests.Fixture();
        var session = GameSession.Start(definition, 42);
        var bytes = SaveService.Encode(session);
        var simulation = JsonNode.Parse(bytes)!["session"]!["world"]!["simulation"]!.AsObject();
        foreach (string field in new[] { "pendingAlliance", "pendingDailyTask", "pendingTacticStudy", "pendingExecutionSuccession" })
            Assert.False(simulation.ContainsKey(field));
        Assert.Equal(session.Capture().StateHash, SaveService.Decode(bytes, definition).Capture().StateHash);

        var snapshot = session.Capture();
        var forged = snapshot with { World = snapshot.World with
            { Simulation = snapshot.World.Simulation with { Boundary = SimulationBoundary.AwaitingDecision } } };
        Assert.Equal(SaveFailure.NotStable,
            Assert.Throws<SaveException>(() => SaveService.EncodeSnapshot(forged, session.ContentFingerprint)).Failure);
        Assert.Throws<ArgumentException>(() => GameSession.Restore(definition, forged));
    }

    [Theory]
    [InlineData("pendingAlliance")]
    [InlineData("pendingDailyTask")]
    [InlineData("pendingTacticStudy")]
    [InlineData("pendingExecutionSuccession")]
    public void RemovedInteractionFieldsAreRejectedRatherThanSilentlyIgnored(string field)
    {
        var definition = G01ScenarioTests.Fixture();
        var json = JsonNode.Parse(SaveService.Encode(GameSession.Start(definition, 42)))!;
        json["session"]!["world"]!["simulation"]![field] = null;
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() =>
            SaveService.Decode(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), definition)).Failure);
    }

    [Fact]
    public void RuntimeHashIncludesUnserializedDecisionProgress()
    {
        var snapshot = GameSession.Start(G01ScenarioTests.Fixture(), 42).Snapshot;
        var first = snapshot with { Simulation = snapshot.Simulation with
            { Boundary = SimulationBoundary.AwaitingDecision, PendingTacticStudy = new(1, 1) } };
        var second = first with { Simulation = first.Simulation with { PendingTacticStudy = new(2, 1) } };
        Assert.Equal(CanonicalJson.Text(first), CanonicalJson.Text(second));
        Assert.NotEqual(first.StateHash, second.StateHash);
    }
}
