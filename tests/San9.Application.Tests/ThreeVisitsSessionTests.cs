using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ThreeVisitsSessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "visits", 190, 2, 1, 1,
        [new(1, "force", 1, 100) { LeaderId = 11, BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 100, 100, 100, 50, []) { Kind = SettlementKind.City, ConsumesFood = false }],
        new() { [1] = "home" }, new() { [1] = 1 })
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false,
        FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false, ForceConsolidationEnabled = false,
        Officers = Enumerable.Range(11, 4).Select(id => new OfficerInitialState($"person.{id}",
            new OfficerState(id, 1, 1, new(50, 50, 50, 50), false)
            { Status = id == 11 ? OfficerStatus.Ruler : OfficerStatus.Serving }.WithLoyalty(1, 100)))
            .Append(new("recruit", new(15, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered })).ToArray(),
        ThreeVisits = new(11, 15, 14, [12, 13], 1, 95, 360, 150, 10, new("visits.start"), new("visits.first"), new("visits.end"))
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }], []) };

    [Fact]
    public void BothVisitsSaveAndReplayAtEveryStableBoundaryWithoutRepeatedRecruitment()
    {
        var definition = Definition(); var session = GameSession.Start(definition, 17);
        Assert.Equal(ThreeVisitsStep.Recommendation, Assert.Single(session.InitialEvents.OfType<ThreeVisitsEvent>()).Step);
        session.BeginRecording();
        for (int visit = 0; visit < 2; visit++)
        {
            var command = new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 11, 1);
            Assert.True(session.Execute(new("player"), command).Submission.Accepted);
            var restored = SaveService.Decode(SaveService.Encode(session), definition);
            var result = session.AdvanceXun();
            Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
            Assert.Equal(visit == 0 ? ThreeVisitsStep.FirstVisit : ThreeVisitsStep.Recruitment,
                Assert.Single(result.Events.OfType<ThreeVisitsEvent>()).Step);
            Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
            Assert.DoesNotContain(result.Events, e => e is SearchResolvedEvent);
            Assert.Equal(session.Capture().StateHash, SaveService.Decode(SaveService.Encode(session), definition).Capture().StateHash);
        }
        Assert.Equal(150, session.Snapshot.Officers.Single(p => p.Id == 15).Loyalty);
        Assert.Equal(new GameClock(190, 2, 1), session.Snapshot.Simulation.ThreeVisitsStarted);
        Assert.Equal(110, session.Snapshot.Forces.Single().BaseTrust);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    [Fact]
    public void ExpiryAtExactlyOneYearSurvivesSaveAndFullReplay()
    {
        var definition = Definition(); var session = GameSession.Start(definition, 17); session.BeginRecording();
        for (int xun = 0; xun < 35; xun++)
            Assert.Empty(session.AdvanceXun().Events.OfType<ThreeVisitsEvent>());
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var result = session.AdvanceXun();
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(ThreeVisitsStep.Expired, Assert.Single(result.Events.OfType<ThreeVisitsEvent>()).Step);
        Assert.Equal(new GameClock(191, 2, 1), session.Snapshot.Clock);
        Assert.Equal(OfficerStatus.Undiscovered, session.Snapshot.Officers.Single(p => p.Id == 15).Status);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    [Fact]
    public void ReferencesTimerAndRequiredFieldsAreValidatedAndFingerprintIncludesRules()
    {
        var definition = Definition(); var visits = definition.ThreeVisits!;
        foreach (var invalid in new[] { visits with { RecruitId = 999 }, visits with { PlaceId = 999 },
            visits with { CompanionIds = [12, 12] }, visits with { DurationDays = 0 }, visits with { CompletedKey = visits.StartedKey } })
            Assert.Throws<InvalidDataException>(() => (definition with { ThreeVisits = invalid }).Validate());
        Assert.Throws<InvalidDataException>(() => (definition with { ThreeVisitsStarted = new(190, 2, 1) }).Validate());
        Assert.Throws<InvalidDataException>(() => (definition with { ThreeVisitsStarted = new(191, 2, 1), ConsumedEventKeys = [visits.StartedKey] }).Validate());
        Assert.NotEqual(GameSession.Fingerprint(definition), GameSession.Fingerprint(definition with { ThreeVisits = visits with { DurationDays = 350 } }));
        Assert.Equal(GameSession.Fingerprint(definition), GameSession.Fingerprint(definition with { ThreeVisits = visits with { CompanionIds = [13, 12] } }));
        foreach (string field in new[] { "threeVisits", "threeVisitsStarted" })
        {
            var document = JsonNode.Parse(CanonicalJson.Encode(definition))!.AsObject(); document.Remove(field);
            Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(document.ToJsonString())));
        }
        var state = GameSession.Start(definition, 17).Snapshot.Simulation;
        var node = JsonNode.Parse(CanonicalJson.Encode(state))!.AsObject(); node.Remove("threeVisitsStarted");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<SimulationState>(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }
}
