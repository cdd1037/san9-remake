using San9.Tests;
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
    internal static ScenarioInitialState Scenario(bool ended = false) => new(ScenarioInitialState.CurrentSchemaVersion, "politics", 190, 3, 1, 1,
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

    [Fact]
    public void AutomaticTitleUsesAuthoredPreferenceIncludingAutomaticOnlyTitle()
    {
        var scenario = Scenario(true);
        scenario = scenario with { Information = scenario.Information with { CountryTitles = [.. scenario.Information.CountryTitles,
            new(new(501, false, true, false, [1], []), "preferred", 40)] } };
        var session = GameSession.Start(scenario, 17, playerForceController: ControllerKind.Automatic);
        session.BeginRecording();
        for (int i = 0; i < 3; i++) session.AdvanceXun();
        Assert.Equal(501, session.Snapshot.Forces.Single(f => f.Id == 1).CountryTitleId);
        Assert.Null(session.GetPendingResponse());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario,
            GameSession.EngineFactory(scenario));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticProclamationDoesNotWaitForAnUnselectableTitle(bool provincePreference)
    {
        var scenario = Scenario(true);
        scenario = scenario with
        {
            ProvinceCountryTitleIds = provincePreference ? scenario.ProvinceCountryTitleIds : ImmutableDictionary<int, int>.Empty,
            Information = scenario.Information with { CountryTitles = scenario.Information.CountryTitles
                .Select(t => t.Rule.Id == 500 ? t with { Rule = t.Rule with { ManualSelectionAllowed = false } } : t).ToArray() }
        };
        var session = GameSession.Start(scenario, 17, playerForceController: ControllerKind.Automatic);
        for (int i = 0; i < 3; i++) session.AdvanceXun();
        var force = session.Snapshot.Forces.Single(f => f.Id == 1);
        Assert.Equal(99, force.RankId);
        Assert.Equal(provincePreference ? 500 : (int?)null, force.CountryTitleId);
        Assert.Null(session.GetPendingResponse());
    }

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
        EventAssert.Equal(result.Events, restoredBefore.AdvanceXun().Events);
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
        var before = session.Snapshot;
        for (int i = 0; i < 5; i++) Assert.Equal(prompt, session.GetCountryTitlePrompt());
        StateAssert.Equal(before, session.Snapshot);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        Assert.Throws<InvalidOperationException>(() => session.CaptureRecording());
        Assert.False(session.Execute(new("player"), new ChooseCountryTitleCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 900)).Submission.Accepted);
        Assert.False(session.Execute(new("ai:2"), new ChooseCountryTitleCommand(Guid.NewGuid(), 2, session.Snapshot.Simulation.NextSequence, 500)).Submission.Accepted);
        StateAssert.Equal(before, session.Snapshot);
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
        var committedHash = session.Snapshot;
        Assert.False(session.Execute(new("player"), command).Submission.Accepted);
        StateAssert.Equal(committedHash, session.Snapshot);
        var restored = SaveService.Decode(SaveService.Encode(session), scenario);
        Assert.Equal(force, restored.View.Forces.Single(f => f.Id == 1));
        StateAssert.Equal(session.Capture(), restored.Capture());
        EventAssert.Equal(session.AdvanceXun().Events, restored.AdvanceXun().Events);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario, GameSession.EngineFactory(scenario));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
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
        StateAssert.Equal(session.Capture(), restored.Capture());
        StateAssert.Equal(session.Capture(), RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario,
            GameSession.EngineFactory(scenario)).Session.Capture());
    }

    [Fact]
    public void RuntimeTitleIsRequiredAndValidated()
    {
        var scenario = Scenario();

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
    [Fact]
    public void AutomaticCountryTitleUsesTheSamePublicChoiceAndReplays()
    {
        var scenario = Scenario() with { PlayerForceId = 2 };
        var session = GameSession.Start(scenario, 17);
        var events = AutomaticResponseReplay.Advance(session, scenario, 3);
        Assert.Single(events.OfType<PeerageChangedEvent>(), e => e.ForceId == 1);
        Assert.Equal(500, session.Snapshot.Forces.Single(f => f.Id == 1).CountryTitleId);
    }
}
