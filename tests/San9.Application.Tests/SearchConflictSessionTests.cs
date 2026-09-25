using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class SearchConflictSessionTests
{
    private static ConflictSearchEventDefinition Event(string kind) => (kind switch
    {
        "demand" => (ConflictSearchEventDefinition)new MoneyDemandSearchEventDefinition([3, 4], 100, 2, 100),
        "scholar" => new ScholarPunishmentSearchEventDefinition([new(1, 3, [], new("scholar.visited"))], 1) { TrustReward = 3 },
        "evil" => new EvildoerSearchEventDefinition(50),
        "spirit" => new LandSpiritSearchEventDefinition(50, 200),
        "bandit" => new BanditSuppressionSearchEventDefinition(50, 6, 120, 1),
        "rescue" => new TravelerRescueSearchEventDefinition(2, 50),
        "mediation" => new BanditMediationSearchEventDefinition([new(1, 4)], [4], 50, 60000, 15, 25, 1000),
        _ => throw new ArgumentException()
    }) with { Id = kind, ChanceAbility = ConflictChanceAbility.Politics, ChanceDivisor = 1 };
    private static ScenarioInitialState Definition(string kind) => new(ScenarioInitialState.CurrentSchemaVersion, "conflicts", 190, 4, 1, 1,
        [new(1, "force", 1, 100) { LeaderId = 1, BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("actor", new(2, 1, 1, new(60, 30, 100, 70), false) { LifespanTier = 5 }),
            new("scholar", new(3, null, null, default, false) { Status = OfficerStatus.Special }),
            new("other", new(4, null, null, default, false) { Status = OfficerStatus.Special })],
        SearchEvents = [Event(kind)]
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }], []) };
    public static IEnumerable<object[]> Choices()
    {
        yield return ["demand", SearchConflictChoice.Pay]; yield return ["demand", SearchConflictChoice.Execute];
        yield return ["scholar", SearchConflictChoice.Allow]; yield return ["scholar", SearchConflictChoice.Execute];
        foreach (string kind in new[] { "evil", "spirit", "bandit", "rescue", "mediation" })
        { yield return [kind, SearchConflictChoice.Fight]; yield return [kind, SearchConflictChoice.Decline]; }
        yield return ["evil", SearchConflictChoice.Persuade]; yield return ["spirit", SearchConflictChoice.Persuade];
        yield return ["bandit", SearchConflictChoice.Appease]; yield return ["mediation", SearchConflictChoice.MediatePeace];
    }
    [Theory]
    [MemberData(nameof(Choices))]
    public void EveryChoiceSupportsPublicQueryAtomicResponseSaveAndReplay(string kind, SearchConflictChoice choice)
    {
        var d = Definition(kind); var session = GameSession.Start(d, 20); session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        session.AdvanceXun(); restored.AdvanceXun();
        var p = Assert.IsType<SearchConflictPrompt>(session.GetSearchConflictPrompt());
        Assert.Equal(CanonicalJson.Text(p), CanonicalJson.Text(restored.GetSearchConflictPrompt()));
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        string hash = session.Snapshot.StateHash;
        Assert.Equal(CanonicalJson.Text(p), CanonicalJson.Text(session.GetSearchConflictPrompt())); Assert.Equal(hash, session.Snapshot.StateHash);
        bool accepted = choice is not (SearchConflictChoice.Decline or SearchConflictChoice.Execute);
        var answer = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, p.TaskId, p.Step, accepted) { SearchConflict = choice };
        foreach (var invalid in new[] { answer with { SearchConflict = null }, answer with { SearchConflict = (SearchConflictChoice)99 },
            answer with { Accept = !accepted },
            answer with { CountryTitleId = 1 }, answer with { MerchantInvestment = new(MerchantInvestmentChoice.Decline, 0) } })
        {
            Assert.False(session.Execute(new("player"), invalid).Submission.Accepted); Assert.Equal(hash, session.Snapshot.StateHash);
        }
        var request = session.Request(new CommandOperation(answer), id: answer.CommandId);
        var result = session.Apply(request); var other = restored.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(other.Events));
        Assert.Equal(choice, Assert.Single(result.Events.OfType<SearchConflictResolvedEvent>()).Choice);
        Assert.DoesNotContain(result.Events, e => e is SearchResolvedEvent);
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(SaveService.Decode(SaveService.Encode(session), d)));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), d, GameSession.EngineFactory(d));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
        if (kind == "scholar" && choice == SearchConflictChoice.Execute)
        { Assert.True(session.Snapshot.Officers.Single(o => o.Id == 3).IsDormant); Assert.Equal(1, session.Snapshot.Officers.Single(o => o.Id == 2).LifespanTier); }
    }

    [Theory]
    [InlineData("bandit", SearchConflictChoice.Decline, true)]
    [InlineData("scholar", SearchConflictChoice.Allow, false)]
    public void SameDaySearchersResumeAndRespectPerForceConsumption(string kind, SearchConflictChoice choice, bool repeats)
    {
        var d = Definition(kind);
        d = d with { Officers = [.. d.Officers, new("second", d.Officers.Single(o => o.State.Id == 2).State with { Id = 5 })] };
        var session = GameSession.Start(d, 20);
        foreach (int actor in new[] { 2, 5 }) Assert.True(session.Execute(new("player"),
            new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, actor, 1)).Submission.Accepted);
        session.AdvanceXun();
        var prompt = session.GetSearchConflictPrompt()!;
        Assert.Equal(2, prompt.OfficerId);
        Assert.True(session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            prompt.TaskId, prompt.Step, choice == SearchConflictChoice.Allow) { SearchConflict = choice }).Submission.Accepted);
        if (repeats)
        {
            prompt = session.GetSearchConflictPrompt()!; Assert.Equal(5, prompt.OfficerId);
            Assert.True(session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
                prompt.TaskId, prompt.Step, false) { SearchConflict = choice }).Submission.Accepted);
        }
        Assert.Null(session.GetSearchConflictPrompt()); Assert.True(session.Snapshot.Simulation.CanPersist);
    }

    [Fact]
    public void AuthoredScholarRulerWithdrawsThroughSuccessionAndRemainsRestorable()
    {
        var d = Definition("scholar");
        d = d with { Forces = [d.Forces.Single() with { LeaderId = 3 }],
            Information = d.Information with { Corps = d.Information.Corps.Select(c => c with { LeaderId = 3 }).ToArray() },
            Officers = d.Officers.Select(o => o.State.Id switch
            {
                1 => o with { State = o.State with { Status = OfficerStatus.Serving } },
                3 => o with { State = o.State with { Status = OfficerStatus.Ruler, OwnerId = 1, SettlementId = 1 } },
                _ => o
            }).ToArray() };
        var session = GameSession.Start(d, 20);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        session.AdvanceXun(); var p = session.GetSearchConflictPrompt()!;
        var result = session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            p.TaskId, p.Step, false) { SearchConflict = SearchConflictChoice.Execute });
        Assert.True(result.Submission.Accepted); Assert.NotEqual(3, session.Snapshot.Forces.Single().LeaderId);
        Assert.Contains(result.Events, e => e is ForceLeaderChangedEvent);
        Assert.True(session.Snapshot.Officers.Single(o => o.Id == 3).IsDormant);
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(SaveService.Decode(SaveService.Encode(session), d)));
    }

    [Fact]
    public void ContentRejectsUnknownReferencesAndInvalidRulesAndCanonicalizesOnlyUnorderedCollections()
    {
        var d = Definition("demand"); var e = (MoneyDemandSearchEventDefinition)d.SearchEvents.Single();
        foreach (var bad in new[] { e with { ChanceDivisor = 0 }, e with { DemandingOfficerIds = [999] },
            e with { DemandingOfficerIds = [] }, e with { Months = [13] }, e with { MaximumInjuryExclusive = 16 },
            e with { Consumption = EventConsumption.OnSuccess }, e with { PoliticsCostDivisor = 0 } })
            Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [bad] }).Validate());
        Assert.Equal(GameSession.Fingerprint(d), GameSession.Fingerprint(d with { SearchEvents = [e with { DemandingOfficerIds = [4, 3] }] }));
        Assert.NotEqual(GameSession.Fingerprint(d), GameSession.Fingerprint(d with { SearchEvents = [e with { CostUnit = 50 }] }));
        var json = JsonNode.Parse(CanonicalJson.Encode(d))!;
        json["searchEvents"]![0]!.AsObject().Remove("chanceDivisor");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    [Theory]
    [InlineData(SearchConflictChoice.Fight)] [InlineData(SearchConflictChoice.MediatePeace)]
    public void FavoredLeaderFailureHasNormalPenaltiesAfterSaveAndReplay(SearchConflictChoice choice)
    {
        var d = Definition("mediation");
        var e = (BanditMediationSearchEventDefinition)d.SearchEvents.Single();
        d = d with { SearchEvents = [e with { Sites = [new(1, 1)], MinimumSuccessChance = 0 }],
            Officers = d.Officers.Select(o => o.State.Id == 2
                ? o with { State = o.State with { BaseAbilities = o.State.BaseAbilities with { War = 0 } } } : o).ToArray() };
        var session = GameSession.Start(d, 20); session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        session.AdvanceXun(); restored.AdvanceXun();
        var prompt = Assert.IsType<SearchConflictPrompt>(session.GetSearchConflictPrompt());
        var command = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            prompt.TaskId, prompt.Step, true) { SearchConflict = choice };
        var request = session.Request(new CommandOperation(command), id: command.CommandId);
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        var resolved = Assert.Single(result.Events.OfType<SearchConflictResolvedEvent>());
        Assert.True(resolved.FavoredLeader); Assert.Equal(EventResult.Defeated, resolved.Result);
        Assert.Equal(98, resolved.BaseTrust); Assert.Equal(7, resolved.Injury);
        Assert.Equal(0, resolved.MoneyReward); Assert.Equal(0u, resolved.Soldiers);
        Assert.DoesNotContain(result.Events.OfType<ResourceChangedEvent>(), e => e.Reason == "search-conflict");
        Assert.Equal(SessionDisposition.Accepted, restored.Apply(request).Disposition);
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), d, GameSession.EngineFactory(d));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    [Fact]
    public void MediationRequiresPositiveSerializedForceLimit()
    {
        var d = Definition("mediation"); var e = (BanditMediationSearchEventDefinition)d.SearchEvents.Single();
        Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [e with { MaximumForceSoldiersExclusive = 0 }] }).Validate());
        Assert.NotEqual(GameSession.Fingerprint(d), GameSession.Fingerprint(d with { SearchEvents = [e with { MaximumForceSoldiersExclusive = 50000 }] }));
        var json = JsonNode.Parse(CanonicalJson.Encode(d))!;
        json["searchEvents"]![0]!.AsObject().Remove("maximumForceSoldiersExclusive");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }
}
