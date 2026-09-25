using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PoliticalEventSessionTests
{
    private static ScenarioInitialState Scenario(bool ended = false) => new(ScenarioInitialState.CurrentSchemaVersion, "politics", 190, 3, 1, 1,
        [new(1, "force.1", 1, 1000) { BaseTrust = ended ? 1000 : 900, LeaderId = 1, RankId = 5, FlagGlyph = 10 },
         new(2, "force.2", 2, 100) { BaseTrust = 100, LeaderId = 2, RankId = 700, CountryTitleId = 900 }],
        [new(1, "city.1", 1, 0, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false },
         new(2, "city.2", 2, 1, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false }],
        new() { [1] = "city.1", [2] = "city.2" }, new() { [1] = 1, [2] = 2 })
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false,
        FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false,
        ForceConsolidationEnabled = false,
        Officers = [new("ruler.1", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("ruler.2", new(2, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("emperor", new(31, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Free })],
        ImperialCourt = new([31], []), ImperialCourtEnded = ended,
        CityProvinceIds = new() { [1] = 10, [2] = 20 },
        ProvinceCountryTitleIds = ImmutableDictionary<int, int>.Empty.Add(10, 500),
        PoliticalEvents = new(1000, new("historical.court-ended")),
        Information = InformationDefinitions.Empty with
        {
            Corps = [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 2, new(10000, 10000)) { IsRoot = true }],
            Ranks = [new(700, "rank.entry", 10000, 0, 0, 0), new(5, "rank.king", 20000, 500, 5, 50), new(99, "rank.emperor", 30000, 1000, 10, 100)],
            CountryTitles = [new(new(500, true, true, false, [], []), "title.a", 37), new(new(900, true, true, false, [], []), "title.b", 88)]
        }
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuarterTailChoiceIsReadOnlyUntilAtomicConfirmationAndRoundTrips(bool ended)
    {
        var scenario = Scenario(ended); var session = GameSession.Start(scenario, 17);
        session.BeginRecording();
        Assert.Equal(10, session.View.Forces.Single(f => f.Id == 1).FlagGlyph);
        Assert.Null(session.GetCountryTitlePrompt());
        session.AdvanceXun(); session.AdvanceXun();
        var restoredBefore = SaveService.Decode(SaveService.Encode(session), scenario);
        var result = session.AdvanceXun();
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restoredBefore.AdvanceXun().Events));
        var prompt = session.GetCountryTitlePrompt()!;
        Assert.Equal(new[] { 500 }, prompt.CandidateIds); Assert.Equal(1, prompt.ForceId);
        Assert.Equal(ended ? PeerageChangeKind.Proclamation : PeerageChangeKind.Abdication, prompt.Kind);
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        Assert.Equal(5, session.View.Forces.Single(f => f.Id == 1).RankId);
        Assert.Null(session.View.Forces.Single(f => f.Id == 1).CountryTitleId);
        Assert.Equal(ended, session.View.ImperialCourtEnded);
        Assert.Empty(result.Events.OfType<PeerageChangedEvent>());
        var order = result.Events.ToList();
        Assert.True(order.FindIndex(e => e is StageCompletedEvent { Stage: ResolutionStage.QuarterUpdate }) <
            order.FindIndex(e => e is CountryTitleOfferedEvent));
        string before = session.Snapshot.StateHash;
        for (int i = 0; i < 5; i++) Assert.Equal(prompt, session.GetCountryTitlePrompt());
        Assert.Equal(before, session.Snapshot.StateHash);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        Assert.Throws<InvalidOperationException>(() => session.CaptureRecording());
        Assert.False(session.Execute(new("player"), new ChooseCountryTitleCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 900)).Submission.Accepted);
        Assert.False(session.Execute(new("ai:2"), new ChooseCountryTitleCommand(Guid.NewGuid(), 2, session.Snapshot.Simulation.NextSequence, 500)).Submission.Accepted);
        Assert.Equal(before, session.Snapshot.StateHash);
        var command = new ChooseCountryTitleCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 500);
        var committed = session.Execute(new("player"), command);
        Assert.True(committed.Submission.Accepted); Assert.Single(committed.Events.OfType<PeerageChangedEvent>());
        var force = session.View.Forces.Single(f => f.Id == 1);
        Assert.Equal(99, force.RankId); Assert.Equal(500, force.CountryTitleId); Assert.Equal(37, force.FlagGlyph);
        Assert.Equal(700, session.View.Forces.Single(f => f.Id == 2).RankId);
        Assert.True(session.View.ImperialCourtEnded); Assert.Null(session.View.EmperorId);
        Assert.DoesNotContain("emperor", session.View.Settlements.Single(s => s.Id == 1).Icons);
        Assert.Equal(!ended, session.Snapshot.Simulation.ConsumedEventKeys.Contains(new("historical.court-ended")));
        Assert.Null(session.GetCountryTitlePrompt());
        string committedHash = session.Snapshot.StateHash;
        Assert.False(session.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(committedHash, session.Snapshot.StateHash);
        var restored = SaveService.Decode(SaveService.Encode(session), scenario);
        Assert.Equal(force, restored.View.Forces.Single(f => f.Id == 1));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Equal(CanonicalJson.Text(session.AdvanceXun().Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario, GameSession.EngineFactory(scenario));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void PromotionOccursAfterQuarterLoyaltyAndPersistsWithoutPromptOrCourtTermination()
    {
        var scenario = Scenario();
        scenario = scenario with { Forces = scenario.Forces.Select(f => f with { RankId = 700 }).ToArray() };
        var session = GameSession.Start(scenario, 17); session.BeginRecording();
        session.AdvanceXun(); session.AdvanceXun(); var result = session.AdvanceXun();
        Assert.Equal(new[] { 1, 2 }, result.Events.OfType<PeerageChangedEvent>().Select(e => e.ForceId));
        Assert.All(session.View.Forces, f => Assert.Equal(5, f.RankId));
        Assert.False(session.View.ImperialCourtEnded); Assert.Null(session.GetCountryTitlePrompt());
        Assert.Equal(910, session.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
        Assert.Equal(110, session.Snapshot.Forces.Single(f => f.Id == 2).Trust);
        var restored = SaveService.Decode(SaveService.Encode(session), scenario);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Equal(session.Capture().StateHash, RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario,
            GameSession.EngineFactory(scenario)).Session.Capture().StateHash);
    }

    [Fact]
    public void PoliticalInputsAffectFingerprintAndRuntimeTitleIsRequiredAndValidated()
    {
        var scenario = Scenario(); string original = GameSession.Fingerprint(scenario);
        Assert.NotEqual(original, GameSession.Fingerprint(scenario with { PoliticalEvents = null }));
        Assert.NotEqual(original, GameSession.Fingerprint(scenario with { ProvinceCountryTitleIds = ImmutableDictionary<int, int>.Empty }));
        Assert.NotEqual(original, GameSession.Fingerprint(scenario with { Information = scenario.Information with
        { CountryTitles = scenario.Information.CountryTitles.Select(t => t with { Rule = t.Rule with { AutomaticSelectionAllowed = false } }).ToArray() } }));
        var session = GameSession.Start(scenario, 17); var snapshot = session.Capture();
        Assert.Throws<ArgumentException>(() => GameSession.Restore(scenario, snapshot with { World = snapshot.World with
        { Forces = snapshot.World.Forces.Select(f => f.Id == 1 ? f with { CountryTitleId = 123456 } : f).ToArray() } }));
        Assert.Throws<ArgumentException>(() => GameSession.Restore(scenario, snapshot with { World = snapshot.World with
        { Forces = snapshot.World.Forces.Select(f => f with { CountryTitleId = 900 }).ToArray() } }));
        var json = JsonNode.Parse(CanonicalJson.Text(snapshot.World.Forces[0]))!.AsObject();
        Assert.True(json.Remove("countryTitleId"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ForceState>(json.ToJsonString(), CanonicalJson.Options));
        var content = JsonNode.Parse(JsonSerializer.Serialize(scenario, ScenarioContentFiles.DataJsonOptions))!.AsObject();
        Assert.True(content.Remove("politicalEvents"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ScenarioInitialState>(content.ToJsonString(), ScenarioContentFiles.DataJsonOptions));
    }
}
