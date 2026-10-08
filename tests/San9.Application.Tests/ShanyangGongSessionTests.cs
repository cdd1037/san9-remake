using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ShanyangGongSessionTests
{
    internal static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "shanyang", 230, 2, 1, 1,
        [new(1, "force.1", 1, 700) { LeaderId = 11, RankId = 7, BaseTrust = 700 },
         new(2, "force.2", 2, 100) { LeaderId = 21, RankId = 7, BaseTrust = 100 }],
        Enumerable.Range(1, 3).Select(id => new SettlementInitialState(id, $"city.{id}", id < 3 ? 1 : 2, id, 0, 100, 100, 100, 50, [])
            { Kind = SettlementKind.City, ConsumesFood = false }).ToArray(),
        new() { [1] = "city.1", [2] = "city.2", [3] = "city.3" }, new() { [1] = 1, [2] = 2, [3] = 3 })
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false,
        FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false, ForceConsolidationEnabled = false,
        Officers = [new("ruler", new(11, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, Surname = "刘" }),
            new("searcher", new OfficerState(12, 1, 1, new(50, 50, 50, 50), false) { Righteousness = 11 }.WithLoyalty(1, 100)),
            new("other", new(21, 2, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("emperor", new(99, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special, Surname = "刘" })],
        CityProvinceIds = new() { [1] = 10, [2] = 20, [3] = 30 },
        ImperialCourtEnded = true, ImperialCourt = new([99], []),
        ShanyangGong = new(99, 1, "刘", 700, 2, 20, 10, 11, 20, 40, new("shanyang")),
        Information = InformationDefinitions.Empty with
        {
            Corps = [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }],
            Ranks = [new(7, "rank.entry", 10000, 0, 0, 0), new(9, "rank.king", 20000, 500, 5, 50), new(12, "rank.emperor", 30000, 1000, 10, 100)],
            CountryTitles = [new(new(101, true, true, false, [], []), "title.a", 37)]
        }
    };
    private static RespondDailyTaskCommand Response(GameSession session, bool accept, int? title = null)
    {
        var p = session.GetShanyangGongPrompt()!;
        return new(Guid.NewGuid(), p.ForceId, session.Snapshot.Simulation.NextSequence, p.TaskId, p.Step, accept) { CountryTitleId = title };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PublicSearchChoicesCommitAtomicallyResumeAndReplay(bool protect)
    {
        var definition = Definition(); var session = GameSession.Start(definition, 17); session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 12, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var arrival = session.AdvanceXun(); Assert.Equal(CanonicalJson.Text(arrival.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        var prompt = session.GetShanyangGongPrompt()!;
        Assert.NotNull(prompt); Assert.Equal((99, 1, 1, false), (prompt.EmperorId, prompt.PlaceId, prompt.Step, prompt.ChoosingCountryTitle));
        var before = session.Snapshot.StateHash;
        Assert.Equal(prompt, session.GetShanyangGongPrompt()); Assert.Equal(before, session.Snapshot.StateHash);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        Assert.False(session.Execute(new("player"), Response(session, protect) with { TaskId = Guid.NewGuid() }).Submission.Accepted);
        Assert.False(session.Execute(new("player"), Response(session, protect) with { Step = 0 }).Submission.Accepted);
        Assert.False(session.Execute(new("ai:2"), Response(session, protect)).Submission.Accepted);
        Assert.False(session.Execute(new("player"), Response(session, protect, 101)).Submission.Accepted);
        Assert.Equal(before, session.Snapshot.StateHash);
        var answer = Response(session, protect);
        var result = session.Execute(new("player"), answer); Assert.True(result.Submission.Accepted);
        Assert.True(restored.Execute(new("player"), answer).Submission.Accepted);
        if (protect)
        {
            prompt = session.GetShanyangGongPrompt()!;
            Assert.True(prompt.ChoosingCountryTitle); Assert.Equal(2, prompt.Step); Assert.Equal(new[] { 101 }, prompt.CountryTitleIds);
            Assert.Empty(result.Events.OfType<ShanyangGongEvent>()); Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys);
            Assert.Equal(7, session.Snapshot.Forces.Single(f => f.Id == 1).RankId);
            Assert.Equal(700, session.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
            Assert.Equal(100, session.Snapshot.Officers.Single(p => p.Id == 12).Loyalty);
            Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
            before = session.Snapshot.StateHash;
            Assert.False(session.Execute(new("player"), Response(session, true, 999)).Submission.Accepted);
            Assert.False(session.Execute(new("player"), Response(session, false, 101)).Submission.Accepted);
            Assert.False(session.Execute(new("player"), Response(session, true)).Submission.Accepted);
            Assert.Equal(before, session.Snapshot.StateHash);
            answer = Response(session, true, 101);
            result = session.Execute(new("player"), answer); Assert.True(result.Submission.Accepted);
            Assert.True(restored.Execute(new("player"), answer).Submission.Accepted);
        }
        Assert.Single(result.Events.OfType<ShanyangGongEvent>()); Assert.Empty(result.Events.OfType<SearchResolvedEvent>());
        Assert.Null(session.GetShanyangGongPrompt()); Assert.Equal(SimulationBoundary.Stable, session.Snapshot.Simulation.Boundary);
        Assert.Equal(new GameClock(230, 2, 11), session.Snapshot.Clock);
        Assert.Equal(protect ? 120 : 80, session.Snapshot.Officers.Single(p => p.Id == 12).Loyalty);
        Assert.Equal(protect ? OfficerStatus.Special : OfficerStatus.Dead, session.Snapshot.Officers.Single(p => p.Id == 99).Status);
        Assert.True(session.Snapshot.Simulation.ImperialCourtEnded); Assert.Null(session.View.EmperorId);
        Assert.Contains(new("shanyang"), session.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Equal(session.Snapshot.StateHash, restored.Snapshot.StateHash);
        before = session.Snapshot.StateHash; Assert.False(session.Execute(new("player"), answer).Submission.Accepted); Assert.Equal(before, session.Snapshot.StateHash);
        Assert.Equal(session.Capture().StateHash, SaveService.Decode(SaveService.Encode(session), definition).Capture().StateHash);
        Assert.Null(session.GetShanyangGongPrompt());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    [Fact]
    public void DepositionHistoryAndSurnameSurviveSaveAndBlockTheSearchEvent()
    {
        var d = Definition(); d = d with { Forces = d.Forces.Select(f => f.Id == 1 ? f with { HasDeposedEmperor = true } : f).ToArray() };
        var session = SaveService.Decode(SaveService.Encode(GameSession.Start(d, 17)), d);
        Assert.True(session.Snapshot.Forces.Single(f => f.Id == 1).HasDeposedEmperor);
        Assert.Equal("刘", session.Snapshot.Officers.Single(p => p.Id == 11).Surname);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 12, 1)).Submission.Accepted);
        Assert.Empty(session.AdvanceXun().Events.OfType<ShanyangGongEvent>()); Assert.Null(session.GetShanyangGongPrompt());
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys);
    }

    [Fact]
    public void DefinitionAndSemanticStateParticipateInStrictContractsAndFingerprint()
    {
        var d = Definition(); var e = d.ShanyangGong!;
        foreach (var invalid in new[] { e with { EmperorId = 999 }, e with { PlaceId = 999 }, e with { MinimumTrust = -1 },
            e with { MinimumRighteousness = 16 }, e with { ImperialSurname = "" }, e with { ConsumptionKey = default } })
            Assert.Throws<InvalidDataException>(() => (d with { ShanyangGong = invalid }).Validate());
        Assert.Throws<InvalidDataException>(() => (d with { Information = InformationDefinitions.Empty }).Validate());
        Assert.NotEqual(GameSession.Fingerprint(d), GameSession.Fingerprint(d with { ShanyangGong = e with { MinimumTrust = 701 } }));
        Assert.NotEqual(GameSession.Fingerprint(d), GameSession.Fingerprint(d with { Forces = d.Forces.Select(f => f with { HasDeposedEmperor = true }).ToArray() }));
        Assert.NotEqual(GameSession.Fingerprint(d), GameSession.Fingerprint(d with { Officers = d.Officers.Select(o => o with { State = o.State with { Surname = "曹" } }).ToArray() }));
        foreach (string field in JsonNode.Parse(CanonicalJson.Encode(e))!.AsObject().Select(p => p.Key))
        {
            var node = JsonNode.Parse(CanonicalJson.Encode(e))!.AsObject(); node.Remove(field);
            Assert.Throws<JsonException>(() => StrictJson.Deserialize<ShanyangGongDefinition>(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
    }
}
