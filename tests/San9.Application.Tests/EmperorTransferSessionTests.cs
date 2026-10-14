using San9.Tests;
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
        [new(1, "force.1", 1, 100) { BaseTrust = 100, LeaderId = 1 },
         new(2, "force.2", 2, 100) { BaseTrust = 100, LeaderId = 2 }],
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
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 2, new(10000, 10000)) { IsRoot = true }], []) };

    [Fact]
    public void UnitAppearanceTracksCourtOwnershipAndRankWhilePreservingOfficerFlags()
    {
        var content = Content();
        content = content with { Information = content.Information with
        {
            Ranks = [new(72, "rank.low", 10000, 0, 0, 1), new(91, "rank.imperial", 60000, 0, 0, 8)]
        } };
        content.Officers[0] = content.Officers[0] with { FlagGlyph = 27 };
        var session = GameSession.Start(content, 17);
        Assert.True(session.View.Forces.Single(f => f.Id == 1).UsesImperialAppearance);
        Assert.False(session.View.Forces.Single(f => f.Id == 2).UsesImperialAppearance);
        Assert.Equal(27, session.View.Officers.Single(o => o.Id == 1).FlagGlyph);
        var world = session.Snapshot;
        var captured = world with { Settlements = world.Settlements.Select(s => s.Id == 1 ? s with { OwnerId = 2 } : s).ToImmutableArray() };
        var afterCapture = new San9.Application.Presentation.StrategySnapshot(content, captured);
        Assert.False(afterCapture.Forces.Single(f => f.Id == 1).UsesImperialAppearance);
        Assert.True(afterCapture.Forces.Single(f => f.Id == 2).UsesImperialAppearance);
        var ended = captured with { Simulation = captured.Simulation with { ImperialCourtEnded = true },
            Forces = captured.Forces.Select(f => f.Id == 1 ? f with { RankId = 91 } : f).ToImmutableArray() };
        var afterCourt = new San9.Application.Presentation.StrategySnapshot(content, ended);
        Assert.True(afterCourt.Forces.Single(f => f.Id == 1).UsesImperialAppearance);
        Assert.False(afterCourt.Forces.Single(f => f.Id == 2).UsesImperialAppearance);
    }

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
        EventAssert.Equal(first.Events, restored.AdvanceXun().Events);
        Assert.Single(first.Events.OfType<EmperorTransferOfferedEvent>());
        var stages = first.Events.OfType<StageCompletedEvent>().Select(e => e.Stage).ToArray();
        Assert.Contains(ResolutionStage.QuarterUpdate, stages); Assert.Equal(ResolutionStage.XunCleanup, stages[^1]);
        Assert.DoesNotContain(ResolutionStage.QuarterPolitics, stages);
        Assert.Null(session.Snapshot.Simulation.EmperorTransfer);
        var hash = session.Snapshot;
        Assert.Equal(new EmperorTransferPrompt(1, 99, 1), session.GetEmperorTransferPrompt());
        StateAssert.Equal(hash, session.Snapshot);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        var wrong = session.Execute(new("ai:2"), new RespondEmperorTransferCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 99, 1, choice));
        Assert.False(wrong.Submission.Accepted); StateAssert.Equal(hash, session.Snapshot);
        var invalid = session.Execute(new("player"), new RespondEmperorTransferCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 99, 1, (EmperorTransferChoice)99));
        Assert.False(invalid.Submission.Accepted); StateAssert.Equal(hash, session.Snapshot);
        var command = new RespondEmperorTransferCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 99, 1, choice);
        var result = session.Execute(new("player"), command);
        Assert.True(result.Submission.Accepted);
        EventAssert.Equal(result.Events, restored.Execute(new("player"), command).Events);
        Assert.Equal(new[] { ResolutionStage.QuarterPolitics }, result.Events.OfType<StageCompletedEvent>().Select(e => e.Stage));
        Assert.Single(result.Events.OfType<EmperorTransferResolvedEvent>());
        Assert.Null(session.GetEmperorTransferPrompt());
        bool depose = choice == EmperorTransferChoice.Depose;
        Assert.Equal(depose, session.View.ImperialCourtEnded);
        Assert.Equal(depose ? 95 : 105, session.Snapshot.Officers.Single(p => p.Id == 11).LoyaltyTo(1));
        Assert.Equal(depose ? 100 : 200, session.View.Forces.Single(f => f.Id == 1).Trust);
        Assert.Equal(OfficerStatus.Free, session.Snapshot.Officers.Single(p => p.Id == 99).Status);
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys);
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), content).Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
        var duplicate = session.Execute(new("player"), command with { CommandId = Guid.NewGuid(), Sequence = session.Snapshot.Simulation.NextSequence });
        Assert.False(duplicate.Submission.Accepted);
        Assert.Empty(session.AdvanceXun().Events.OfType<EmperorTransferResolvedEvent>());
    }

    [Fact]
    public void ContentAndPendingStateAreStrict()
    {
        var content = Content();

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

        var titleJson = JsonNode.Parse(CanonicalJson.Text(title))!.AsObject();
        Assert.True(titleJson.Remove("forcesEmperorDeposition"));
        Assert.Throws<JsonException>(() => titleJson.Deserialize<CountryTitleRule>(CanonicalJson.Options));
    }
    [Fact]
    public void AutomaticCourtTransferSupportsTheEmperorThroughThePublicResponse()
    {
        var scenario = Content() with { PlayerForceId = 2 };
        var initial = Pending(scenario).Capture();
        var session = GameSession.Restore(scenario, initial with { Ai = initial.Ai with
            { Configuration = San9.Application.AI.AiPolicyConfiguration.Standard with { WorkBudget = 1 } } });
        var events = AutomaticResponseReplay.Advance(session, scenario, 1);
        Assert.Equal(EmperorTransferChoice.Support, Assert.Single(events.OfType<EmperorTransferResolvedEvent>()).Choice);
    }
}
