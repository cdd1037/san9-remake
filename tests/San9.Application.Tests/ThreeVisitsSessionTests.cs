using San9.Tests;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ThreeVisitsSessionTests
{
    [Fact]
    public void InitialRecommendationFollowsTheOrderPromptWithoutRepeatingOrChangingTheWorld()
    {
        var definition = Definition();
        var session = GameSession.Start(definition, 17);
        var roles = ProgressPresentationDefinition.IntroductionRoles.Append("order").Append("recommendation");
        var texts = MissionScenario.Texts(session.View) with
        {
            Messages = roles.ToDictionary(role => role, role => new MessageDefinition([new MessageLiteral(role)]))
        };
        var content = CommandPresentation.Empty with
        {
            Progress = new([], [])
            {
                Introduction = ProgressPresentationDefinition.IntroductionRoles.ToDictionary(role => role,
                    role => new DecisionMessageDefinition(role, [])),
                OrderPrompt = new("order", [])
            },
            Events = new(new() { ["three-visits-recommendation"] = new("recommendation", [], "actor") })
            { People = new() { ["recommender"] = 14 } }
        };
        var before = session.Capture();
        Assert.Equal(OfficerStatus.Undiscovered, session.InitialView.World.Officers.Single(p => p.Id == 15).Status);
        var publication = new StrategyEntrancePublication(session);
        var output = publication.Read(1, texts, content, introduceNewGame: true)!;
        var keys = output.Messages.Select(m => m.Message.Key).ToArray();
        Assert.Equal("opening", keys[0]);
        Assert.Equal(new[] { "order", "recommendation" }, keys.TakeLast(2));
        Assert.Equal(14, output.Messages[^1].SpeakerId);
        Assert.Null(publication.Read(1, texts, content, introduceNewGame: true));
        Assert.Equal("order", Assert.Single(publication.Read(2, texts, content, introduceNewGame: true)!.Messages).Message.Key);
        StateAssert.Equal(before, session.Capture());

        var restored = GameSession.Restore(definition, before);
        var restoredOutput = new StrategyEntrancePublication(restored).Read(1, texts, content, introduceNewGame: false)!;
        Assert.Equal("order", Assert.Single(restoredOutput.Messages).Message.Key);

        var rebound = new StrategyEntrancePublication(session, resumedEntrance: 1);
        Assert.Null(rebound.Read(1, texts, content, introduceNewGame: false));
        // A genuine subsequent entrance still prompts, without replaying initial recommendations.
        var nextEntrance = rebound.Read(2, texts, content, introduceNewGame: false)!;
        Assert.Equal("order", Assert.Single(nextEntrance.Messages).Message.Key);
        StateAssert.Equal(before, session.Capture());
    }

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
    public void SearchAdviceMarksBothSpecialVisitsWithoutChangingTheWorld()
    {
        var definition = Definition();
        definition = definition with {
            Forces = definition.Forces.Select(f => f with { AdviserId = 14 }).ToArray(),
            Officers = definition.Officers.Select(p => p.State.Id == 14 ? p with { State = p.State with { Status = OfficerStatus.Strategist } } : p).ToArray()
        };
        var session = GameSession.Start(definition,17);
        for (int visit = 0; visit < 2; visit++)
        {
            var before = session.Snapshot;
            var draft = new San9.Application.Presentation.PersonnelDraft(session,1,"search");
            Assert.True(draft.SetTarget(1));
            Assert.Equal("◎",draft.AdviceSymbol(11));
            Assert.Equal(visit == 0 ? ThreeVisitsStep.FirstVisit : ThreeVisitsStep.Recruitment,draft.SearchAdviceFor(11));
            Assert.Same(before,session.Snapshot);
            Assert.True(draft.SetMembers([11]));
            Assert.True(session.Execute(new("player"),draft.CreateCommand(Guid.NewGuid(),session.Snapshot.Simulation.NextSequence)).Submission.Accepted);
            session.AdvanceXun();
        }
    }

    [Fact]
    public void BothVisitsSaveAndReplayAtEveryStableBoundaryWithoutRepeatedRecruitment()
    {
        var definition = Definition(); var session = GameSession.Start(definition, 17);
        Assert.Equal(ThreeVisitsStep.Recommendation, Assert.Single(session.InitialEvents.OfType<ThreeVisitsEvent>()).Step);
        for (int visit = 0; visit < 2; visit++)
        {
            var command = new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, [11], 1);
            Assert.True(session.Execute(new("player"), command).Submission.Accepted);
            var restored = SaveService.Decode(SaveService.Encode(session), definition);
            var result = session.AdvanceXun();
            EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
            Assert.Equal(visit == 0 ? ThreeVisitsStep.FirstVisit : ThreeVisitsStep.Recruitment,
                Assert.Single(result.Events.OfType<ThreeVisitsEvent>()).Step);
            StateAssert.Equal(session.Capture(), restored.Capture());
            Assert.DoesNotContain(result.Events, e => e is SearchResolvedEvent);
            StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), definition).Capture());
        }
        Assert.Equal(150, session.Snapshot.Officers.Single(p => p.Id == 15).Loyalty);
        Assert.Equal(new GameClock(190, 2, 1), session.Snapshot.Simulation.ThreeVisitsStarted);
        Assert.Equal(110, session.Snapshot.Forces.Single().BaseTrust);
        byte[] bytes = SaveService.Encode(session);
        foreach (var revised in new[] { definition with { ThreeVisits = null },
            definition with { ThreeVisits = definition.ThreeVisits! with { StartedKey = new("visits.revised") } } })
        {
            StateAssert.Equal(session.Capture(), SaveService.Decode(bytes, revised).Capture());
            var loaded = SaveService.Load(bytes, revised);
            Assert.Equal(session.Snapshot.Simulation.ThreeVisitsStarted, loaded.Snapshot.Simulation.ThreeVisitsStarted);
            Assert.Equal(150, loaded.Snapshot.Officers.Single(p => p.Id == 15).Loyalty);
            Assert.Empty(loaded.InitialEvents);
        }
    }

    [Fact]
    public void ExpiryAtExactlyOneYearSurvivesSaveAndFullReplay()
    {
        var definition = Definition(); var session = GameSession.Start(definition, 17);         for (int xun = 0; xun < 35; xun++)
            Assert.Empty(session.AdvanceXun().Events.OfType<ThreeVisitsEvent>());
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var result = session.AdvanceXun();
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        Assert.Equal(ThreeVisitsStep.Expired, Assert.Single(result.Events.OfType<ThreeVisitsEvent>()).Step);
        Assert.Equal(new GameClock(191, 2, 1), session.Snapshot.Clock);
        Assert.Equal(OfficerStatus.Undiscovered, session.Snapshot.Officers.Single(p => p.Id == 15).Status);
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
