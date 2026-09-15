using System.Text;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AllianceFormationSessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "alliance-choice", 190, 1, 1, 5,
        Enumerable.Range(1, 5).Select(id => new ForceInitialState(id, $"force.{id}", id, 10000, 100000, 50) { BaseTrust = 50 }).ToArray(),
        Enumerable.Range(1, 5).Select(id => new SettlementInitialState(id * 100, $"city.{id}", id, id, 0, 0, 100, 100, 0, [])
            { Population = 100000, DraftPopulation = id == 1 ? 400001u : 0, PublicOrder = 500 }).ToArray(), [], [])
    {
        CityProvinceIds = Enumerable.Range(1, 5).ToDictionary(id => id * 100, _ => 1),
        Information = InformationDefinitions.Empty with { Relations = [new(1, 5, 80)] }
    };

    private static GameSession Ready(ScenarioInitialState definition)
    {
        var initial = GameSession.Start(definition, 5).Capture();
        return GameSession.Restore(definition, initial with { World = initial.World with { Clock = new(190, 12, 21) } });
    }

    [Fact]
    public void ChoiceSuspendsQuarterAndSurvivesSaveRestoreAndCompleteRecording()
    {
        var definition = Definition();
        var session = Ready(definition);
        session.BeginRecording();
        uint draftPopulation = session.Snapshot.Settlements.Single(s => s.Id == 100).DraftPopulation;
        session.AdvanceXun();
        Assert.Equal(draftPopulation, session.Snapshot.Settlements.Single(s => s.Id == 100).DraftPopulation);
        Assert.Equal(5, session.GetAllianceParticipationPrompt()!.Proposal.RespondingForceId);
        string pendingHash = session.Capture().StateHash;
        Assert.Equal(SessionRejection.NotStable, session.Apply(session.Request(new AdvanceOperation())).Rejection);
        Assert.Equal(pendingHash, session.Capture().StateHash);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(pendingHash, restored.Capture().StateHash);
        var command = new AllianceParticipationCommand(Guid.NewGuid(), 5, session.Snapshot.Simulation.NextSequence, 1, true);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        Assert.True(restored.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Equal(new[] { 2, 3, 4, 5 }, session.Snapshot.Simulation.Alliance!.MemberForceIds);
        Assert.Equal(75, session.Snapshot.Forces.Single(f => f.Id == 5).BaseTrust);
        Assert.Equal(draftPopulation + 100, session.Snapshot.Settlements.Single(s => s.Id == 100).DraftPopulation);
        Assert.True(session.Snapshot.Relations.Count > definition.Information.Relations.Length);
        var final = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(session.Capture().StateHash, final.Capture().StateHash);
        var replayed = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replayed.Session.Capture().StateHash);
        Assert.Single(replayed.Events.OfType<AllianceProposedEvent>());
        Assert.Single(replayed.Events.OfType<AllianceFormedEvent>());
    }

    [Fact]
    public void PlayerLoadPreservesTheChosenTargetAndRecordingCanStartWhileAwaitingAnAnswer()
    {
        var definition = Definition();
        var session = Ready(definition);
        session.AdvanceXun();
        var loaded = SaveService.Decode(SaveService.Encode(session), definition, GameSession.EngineFactory(definition), playerLoad: true);
        Assert.NotEqual(session.Snapshot.Simulation.RunSeed, loaded.Snapshot.Simulation.RunSeed);
        Assert.Equal(CanonicalJson.Text(session.GetAllianceParticipationPrompt()), CanonicalJson.Text(loaded.GetAllianceParticipationPrompt()));
        loaded.BeginRecording();
        Assert.True(loaded.Execute(new("player"), new AllianceParticipationCommand(Guid.NewGuid(), 5,
            loaded.Snapshot.Simulation.NextSequence, 1, false)).Submission.Accepted);
        Assert.Equal(new[] { 2, 3, 4 }, loaded.Snapshot.Simulation.Alliance!.MemberForceIds);
        var replayed = RecordingService.Replay(RecordingService.Encode(loaded.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(loaded.Capture().StateHash, replayed.Session.Capture().StateHash);
        Assert.Empty(replayed.Events.OfType<AllianceProposedEvent>());
        Assert.Single(replayed.Events.OfType<AllianceFormedEvent>());
    }

    [Fact]
    public void MissingDecisionPayloadAndImplicitJoinValueAreRejected()
    {
        var definition = Definition();
        var session = Ready(definition);
        session.AdvanceXun();
        var document = JsonNode.Parse(SaveService.Encode(session))!;
        document["session"]!["world"]!["simulation"]!.AsObject().Remove("pendingAlliance");
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => SaveService.Decode(
            Encoding.UTF8.GetBytes(document.ToJsonString()), definition)).Failure);
        var command = JsonNode.Parse(CanonicalJson.Text<GameCommand>(new AllianceParticipationCommand(Guid.NewGuid(), 5, 0, 1, false)))!;
        command.AsObject().Remove("join");
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<GameCommand>(command.ToJsonString(),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, AllowOutOfOrderMetadataProperties = true }));
    }
}
