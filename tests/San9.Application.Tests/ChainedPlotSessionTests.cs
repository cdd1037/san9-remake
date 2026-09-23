using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ChainedPlotSessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "plot", 192, 3, 21, 1,
        [new(1, "force", 1, 10000, 10000, 500) { LeaderId = 11, BaseTrust = 500 }],
        [new(1, "home", 1, 0, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false }],
        new() { [1] = "home" }, new() { [1] = 1 })
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false,
        FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false, ForceConsolidationEnabled = false,
        Officers = Enumerable.Range(11, 5).Select(id => new OfficerInitialState($"person.{id}",
            new OfficerState(id, 1, 1, new(50, 50, 50, 50), false)
            { Status = id == 11 ? OfficerStatus.Ruler : OfficerStatus.Serving, BloodlineId = id is 11 or 15 ? 700 : id }.WithLoyalty(1, 100)))
            .Append(new("emperor", new(99, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special })).ToArray(),
        ImperialCourt = new([99], []),
        ChainedPlot = new(11, 12, 13, new(192, 4, 1), 95, 1000, [13, 14], [15], 96, 80, 2, new("plot.start"), new("plot.end"))
    };

    [Theory]
    [InlineData(12)]
    [InlineData(13)]
    public void WithdrawnConspiratorCompletesStartedPlotOnceAcrossSaveAndReplay(int withdrawnId)
    {
        var original = Definition();
        var definition = original with
        {
            Officers = original.Officers.Select(p => p.State.Id == 14
                ? p with { State = p.State with { BaseAbilities = new(100, 100, 100, 100) } } : p).ToArray(),
            SearchEvents = [new ScholarPunishmentSearchEventDefinition([new(1, withdrawnId, [], new("scholar.visited"))], 1)
                { Id = "withdraw-conspirator", ChanceAbility = ConflictChanceAbility.Politics, ChanceDivisor = 1 }]
        };
        var session = GameSession.Start(definition, 17);
        session.BeginRecording();
        Assert.Equal(ChainedPlotStep.Prelude, Assert.Single(session.AdvanceXun().Events.OfType<ChainedPlotEvent>()).Step);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 14, 1)).Submission.Accepted);
        session.AdvanceXun();
        var prompt = Assert.IsType<SearchConflictPrompt>(session.GetSearchConflictPrompt());
        var answer = session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, false) { SearchConflict = SearchConflictChoice.Execute });
        Assert.True(answer.Submission.Accepted);
        Assert.True(session.Snapshot.Officers.Single(p => p.Id == withdrawnId).IsDormant);
        var later = session.AdvanceXun();
        Assert.Equal(ChainedPlotStep.ConspiratorsUnavailable,
            Assert.Single(answer.Events.Concat(later.Events).OfType<ChainedPlotEvent>()).Step);
        Assert.Contains(definition.ChainedPlot!.CompletedKey, session.Snapshot.Simulation.ConsumedEventKeys);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Empty(restored.InitialEvents.OfType<ChainedPlotEvent>());
        Assert.Empty(session.AdvanceXun().Events.OfType<ChainedPlotEvent>());
        Assert.Empty(restored.AdvanceXun().Events.OfType<ChainedPlotEvent>());
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }
    [Fact]
    public void PreludeAndCoupSaveAndReplayAtStableBoundariesWithoutRepeatingDeaths()
    {
        var definition = Definition(); var session = GameSession.Start(definition, 17);
        Assert.Empty(session.InitialEvents.OfType<ChainedPlotEvent>()); session.BeginRecording();
        foreach (var step in new[] { ChainedPlotStep.Prelude, ChainedPlotStep.Coup })
        {
            var restored = SaveService.Decode(SaveService.Encode(session), definition);
            var result = session.AdvanceXun();
            Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
            Assert.Equal(step, Assert.Single(result.Events.OfType<ChainedPlotEvent>()).Step);
            Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
            var stable = SaveService.Decode(SaveService.Encode(session), definition);
            Assert.Equal(session.Capture().StateHash, stable.Capture().StateHash);
            Assert.Empty(stable.InitialEvents);
        }
        Assert.Equal(12, session.Snapshot.Forces.Single().LeaderId);
        Assert.Equal(new[] { 11, 15 }, session.Snapshot.Officers.Where(p => p.Status == OfficerStatus.Dead).Select(p => p.Id));
        Assert.Empty(session.AdvanceXun().Events.OfType<ChainedPlotEvent>());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    [Fact]
    public void FailedPlotCompletesOnceAndSurvivesSaveAndReplay()
    {
        var definition = Definition() with { ConsumedEventKeys = [new("plot.start")], ImperialCourtEnded = true };
        var session = GameSession.Start(definition, 17);
        Assert.Equal(ChainedPlotStep.CourtUnavailable, Assert.Single(session.InitialEvents.OfType<ChainedPlotEvent>()).Step);
        Assert.Equal(11, session.Snapshot.Forces.Single().LeaderId);
        session.BeginRecording();
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Empty(restored.InitialEvents);
        Assert.Empty(session.AdvanceXun().Events.OfType<ChainedPlotEvent>());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    [Fact]
    public void ContentValidatesReferencesAndParametersAndCanonicalizesListsInFingerprint()
    {
        var definition = Definition(); var plot = definition.ChainedPlot!;
        foreach (var invalid in new[] { plot with { RulerId = 999 }, plot with { SuccessorId = 999 },
            plot with { InstigatorId = 11 }, plot with { SupporterIds = [999] }, plot with { OpponentIds = [15, 15] },
            plot with { LoyaltyThreshold = 256 }, plot with { TrustThreshold = 1001 }, plot with { SupporterMinimumLoyalty = -1 },
            plot with { OpponentMaximumLoyalty = 256 }, plot with { AffinityDivisor = 0 }, plot with { CompletedKey = plot.StartedKey } })
            Assert.Throws<InvalidDataException>(() => (definition with { ChainedPlot = invalid }).Validate());
        Assert.NotEqual(GameSession.Fingerprint(definition), GameSession.Fingerprint(definition with { ChainedPlot = plot with { TrustThreshold = 999 } }));
        Assert.Equal(GameSession.Fingerprint(definition), GameSession.Fingerprint(definition with { ChainedPlot = plot with { SupporterIds = [14, 13] } }));
        var document = JsonNode.Parse(CanonicalJson.Encode(definition))!.AsObject(); document.Remove("chainedPlot");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(document.ToJsonString())));
        foreach (string field in new[] { "successorId", "earliestDate", "supporterIds", "completedKey" })
        {
            var node = JsonNode.Parse(CanonicalJson.Encode(plot))!.AsObject(); node.Remove(field);
            Assert.Throws<JsonException>(() => StrictJson.Deserialize<ChainedPlotDefinition>(Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
    }
}
