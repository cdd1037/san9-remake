using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class EmperorTransferSessionTests
{
    private static ScenarioInitialState Content() => new(ScenarioInitialState.CurrentSchemaVersion, "emperor-transfer", 190, 3, 21, 1,
        [new(1, "force.1", 1, 10000, 10000, 100) { BaseTrust = 100, LeaderId = 1 },
         new(2, "force.2", 2, 10000, 10000, 100) { BaseTrust = 100, LeaderId = 2 }],
        [new(1, "city.1", 1, 0, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false },
         new(2, "city.2", 2, 1, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false }],
        new() { [1] = "city.1", [2] = "city.2" }, [])
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false,
        FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false,
        ForceConsolidationEnabled = false,
        Officers = [new("ruler.1", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("ruler.2", new(2, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("emperor", new(99, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Free }),
            new("loyal", new(11, 1, 1, new(50, 50, 50, 50), false) { Righteousness = 11,
                ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 100) })],
        ImperialCourt = new([99], []) { TransferEvent = new(11, 15, 5) }
    };

    private static GameSession Pending(ScenarioInitialState content)
    {
        var snapshot = GameSession.Start(content, 17).Capture();
        // A stable save after an ownership transition, before the next xun cleanup.
        return GameSession.Restore(content, snapshot with { World = snapshot.World with
            { Simulation = snapshot.World.Simulation with { EmperorTransfer = new(2) } } });
    }

    [Theory]
    [InlineData(EmperorTransferChoice.Support)]
    [InlineData(EmperorTransferChoice.Depose)]
    public void ChoiceResumesAfterQuarterUpdateAndRoundTripsThroughSaveAndReplay(EmperorTransferChoice choice)
    {
        var content = Content(); var session = Pending(content);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.Equal(new EmperorTransferPending(2), restored.Snapshot.Simulation.EmperorTransfer);
        session.BeginRecording(); var first = session.AdvanceXun();
        Assert.Equal(CanonicalJson.Text(first.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Single(first.Events.OfType<EmperorTransferOfferedEvent>());
        var stages = first.Events.OfType<StageCompletedEvent>().Select(e => e.Stage).ToArray();
        Assert.Contains(ResolutionStage.QuarterUpdate, stages); Assert.Equal(ResolutionStage.XunCleanup, stages[^1]);
        Assert.DoesNotContain(ResolutionStage.QuarterPolitics, stages);
        Assert.Null(session.Snapshot.Simulation.EmperorTransfer);
        string hash = session.Snapshot.StateHash;
        Assert.Equal(new EmperorTransferPrompt(1, 99, 1), session.GetEmperorTransferPrompt());
        Assert.Equal(hash, session.Snapshot.StateHash);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        var wrong = session.Execute(new("ai:2"), new RespondEmperorTransferCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 99, 1, choice));
        Assert.False(wrong.Submission.Accepted); Assert.Equal(hash, session.Snapshot.StateHash);
        var invalid = session.Execute(new("player"), new RespondEmperorTransferCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 99, 1, (EmperorTransferChoice)99));
        Assert.False(invalid.Submission.Accepted); Assert.Equal(hash, session.Snapshot.StateHash);
        var command = new RespondEmperorTransferCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 99, 1, choice);
        var result = session.Execute(new("player"), command);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.Execute(new("player"), command).Events));
        Assert.Equal(new[] { ResolutionStage.QuarterPolitics }, result.Events.OfType<StageCompletedEvent>().Select(e => e.Stage));
        Assert.Single(result.Events.OfType<EmperorTransferResolvedEvent>());
        Assert.Null(session.GetEmperorTransferPrompt());
        bool depose = choice == EmperorTransferChoice.Depose;
        Assert.Equal(depose, session.View.ImperialCourtEnded);
        Assert.Equal(depose ? 95 : 105, session.Snapshot.Officers.Single(p => p.Id == 11).LoyaltyTo(1));
        Assert.Equal(depose ? 100 : 200, session.View.Forces.Single(f => f.Id == 1).Trust);
        Assert.Equal(OfficerStatus.Free, session.Snapshot.Officers.Single(p => p.Id == 99).Status);
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Equal(session.Capture().StateHash, SaveService.Decode(SaveService.Encode(session), content).Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
        var duplicate = session.Execute(new("player"), command with { CommandId = Guid.NewGuid(), Sequence = session.Snapshot.Simulation.NextSequence });
        Assert.False(duplicate.Submission.Accepted);
        Assert.Empty(session.AdvanceXun().Events.OfType<EmperorTransferResolvedEvent>());
    }

    [Fact]
    public void ContentAndPendingStateAreStrictAndAffectFingerprint()
    {
        var content = Content(); string hash = GameSession.Fingerprint(content);
        Assert.NotEqual(hash, GameSession.Fingerprint(content with { ImperialCourt = content.ImperialCourt with
            { TransferEvent = new(10, 15, 7) } }));
        Assert.Throws<InvalidDataException>(() => GameSession.Start(content with { ImperialCourt = content.ImperialCourt with
            { TransferEvent = new(12, 11, 5) } }, 17));
        var court = JsonNode.Parse(CanonicalJson.Text(content.ImperialCourt))!.AsObject();
        Assert.True(court.Remove("transferEvent"));
        Assert.Throws<JsonException>(() => court.Deserialize<ImperialCourtDefinition>(CanonicalJson.Options));
        var pending = JsonNode.Parse(CanonicalJson.Text(new EmperorTransferPending(2)))!.AsObject();
        Assert.True(pending.Remove("previousForceId"));
        Assert.Throws<JsonException>(() => pending.Deserialize<EmperorTransferPending>(CanonicalJson.Options));
        var snapshot = Pending(content).Capture();
        Assert.Throws<ArgumentException>(() => GameSession.Restore(content with { ImperialCourt = content.ImperialCourt with { TransferEvent = null } }, snapshot));
        Assert.Throws<ArgumentException>(() => GameSession.Restore(content, snapshot with { World = snapshot.World with
            { Simulation = snapshot.World.Simulation with { EmperorTransfer = new(12345) } } }));
        var title = new CountryTitleRule(20, true, true, false, [], []);
        content = content with { Information = content.Information with { CountryTitles = [new(title, "title", -1)] } };
        Assert.NotEqual(GameSession.Fingerprint(content), GameSession.Fingerprint(content with { Information = content.Information with
            { CountryTitles = [new(title with { ForcesEmperorDeposition = true }, "title", -1)] } }));
        var titleJson = JsonNode.Parse(CanonicalJson.Text(title))!.AsObject();
        Assert.True(titleJson.Remove("forcesEmperorDeposition"));
        Assert.Throws<JsonException>(() => titleJson.Deserialize<CountryTitleRule>(CanonicalJson.Options));
    }
}
