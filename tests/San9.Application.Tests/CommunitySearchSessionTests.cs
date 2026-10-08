using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CommunitySearchSessionTests
{
    private static CommunitySearchEventDefinition Event(string kind) => (kind switch
    {
        "relief" => (CommunitySearchEventDefinition)new CivicSearchEventDefinition(CivicSearchKind.DisasterRelief) { CostAbilityCeiling = 120, MoneyCostMultiplier = 4, FoodCostMultiplier = 6, TrustReward = 2, TrustPenalty = 2 },
        "volunteers" => new CivicSearchEventDefinition(CivicSearchKind.Volunteers) { SoldierMultiplier = 20 },
        "bribe" => new CivicSearchEventDefinition(CivicSearchKind.Bribe) { BaseMoneyReward = 50, MoneyAbilityMultiplier = 2, TrustReward = 2, TrustPenalty = 2 },
        "permit" => new CivicSearchEventDefinition(CivicSearchKind.TravelPermit) { BaseMoneyReward = 10, MoneyAbilityMultiplier = 2, TrustReward = 2, TrustPenalty = 2 },
        "festival" => new CivicSearchEventDefinition(CivicSearchKind.Festival) { TrustReward = 2, ExecutionTrustPenalty = 4 },
        "monument" => new CommemorationSearchEventDefinition([new(1, null, CommunityAbility.Intelligence, [], new("monument.1"))], 1500, 2, 2) { Consumption = EventConsumption.OnSuccess },
        "sacrifice" => new CommemorationSearchEventDefinition([new(1, null, CommunityAbility.War, [], new("sacrifice.1"))], 1000, 1, 2) { Consumption = EventConsumption.OnSuccess },
        "training" => new TrainingSiteSearchEventDefinition([new(1, 2)], 500, 0, 600, 4),
        _ => throw new ArgumentException()
    }) with { Id = kind, ChanceAbility = CommunityAbility.Politics, ChanceDivisor = 1 };
    internal static ScenarioInitialState Definition(string kind) => new(ScenarioInitialState.CurrentSchemaVersion, "community", 190, 1, 1, 1,
        [new(1, "force", 1, 100) { LeaderId = 1, BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 1000, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("actor", new(2, 1, 1, new(60, 30, 100, 70), false))],
        SearchEvents = [Event(kind)]
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }], []) };
    public static IEnumerable<object[]> Choices()
    {
        yield return ["relief", CommunitySearchChoice.DonateMoney]; yield return ["relief", CommunitySearchChoice.DonateFood];
        foreach (string kind in new[] { "volunteers", "bribe", "permit", "festival", "monument", "sacrifice", "training" })
            yield return [kind, CommunitySearchChoice.Accept];
        foreach (string kind in new[] { "relief", "volunteers", "bribe", "permit", "festival", "monument", "sacrifice", "training" })
            yield return [kind, CommunitySearchChoice.Decline];
        yield return ["bribe", CommunitySearchChoice.DemandMoney]; yield return ["permit", CommunitySearchChoice.DemandMoney];
        yield return ["festival", CommunitySearchChoice.Execute];
    }
    [Theory]
    [MemberData(nameof(Choices))]
    public void EveryChoiceSupportsPublicQueryAtomicResponseSaveAndReplay(string kind, CommunitySearchChoice choice)
    {
        var d = Definition(kind); var session = GameSession.Start(d, 20); session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        session.AdvanceXun(); restored.AdvanceXun();
        var p = Assert.IsType<CommunitySearchPrompt>(session.GetCommunitySearchPrompt());
        Assert.Equal(CanonicalJson.Text(p), CanonicalJson.Text(restored.GetCommunitySearchPrompt()));
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        string hash = session.Snapshot.StateHash;
        Assert.Equal(CanonicalJson.Text(p), CanonicalJson.Text(session.GetCommunitySearchPrompt())); Assert.Equal(hash, session.Snapshot.StateHash);
        bool accepted = choice is not (CommunitySearchChoice.Decline or CommunitySearchChoice.Execute);
        var answer = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, p.TaskId, p.Step, accepted) { CommunitySearch = choice };
        foreach (var invalid in new[] { answer with { CommunitySearch = null }, answer with { CommunitySearch = (CommunitySearchChoice)99 },
            answer with { Accept = !accepted },
            answer with { SearchConflict = SearchConflictChoice.Pay }, answer with { CountryTitleId = 1 }, answer with { MerchantInvestment = new(MerchantInvestmentChoice.Decline, 0) } })
        {
            Assert.False(session.Execute(new("player"), invalid).Submission.Accepted); Assert.Equal(hash, session.Snapshot.StateHash);
        }
        var request = session.Request(new CommandOperation(answer), id: answer.CommandId);
        var result = session.Apply(request); var other = restored.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(other.Events));
        Assert.Equal(choice, Assert.Single(result.Events.OfType<CommunitySearchResolvedEvent>()).Choice);
        Assert.DoesNotContain(result.Events, e => e is SearchResolvedEvent);
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(SaveService.Decode(SaveService.Encode(session), d)));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), d, GameSession.EngineFactory(d));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void SameDaySearchersSeeOnlyAcceptedSiteConsumption(bool accept)
    {
        var d = Definition("monument");
        d = d with { Officers = [.. d.Officers, new("second", d.Officers[1].State with { Id = 3 })] };
        var session = GameSession.Start(d, 20);
        foreach (int actor in new[] { 2, 3 }) Assert.True(session.Execute(new("player"),
            new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, actor, 1)).Submission.Accepted);
        session.AdvanceXun(); var p = session.GetCommunitySearchPrompt()!;
        Assert.True(session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            p.TaskId, p.Step, accept) { CommunitySearch = accept ? CommunitySearchChoice.Accept : CommunitySearchChoice.Decline }).Submission.Accepted);
        if (!accept)
        {
            p = session.GetCommunitySearchPrompt()!; Assert.Equal(3, p.OfficerId);
            Assert.True(session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
                p.TaskId, p.Step, false) { CommunitySearch = CommunitySearchChoice.Decline }).Submission.Accepted);
        }
        Assert.Null(session.GetCommunitySearchPrompt()); Assert.True(session.Snapshot.Simulation.CanPersist);
    }
    [Fact]
    public void ContentValidatesReferencesAndRequiredFields()
    {
        var d = Definition("monument"); var e = (CommemorationSearchEventDefinition)d.SearchEvents.Single();
        foreach (var bad in new[] { e with { ChanceDivisor = 0 }, e with { Sites = [] }, e with { Sites = [e.Sites[0] with { PlaceId = 999 }] },
            e with { Sites = [e.Sites[0] with { RequiredTreasureId = 999 }] }, e with { Sites = [e.Sites[0] with { RewardTacticIds = [999] }] },
            e with { Months = [13] }, e with { Consumption = EventConsumption.None } })
            Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [bad] }).Validate());
        Assert.NotEqual(GameSession.Fingerprint(d), GameSession.Fingerprint(d with { SearchEvents = [e with { Cost = 1 }] }));
        var json = JsonNode.Parse(CanonicalJson.Encode(d))!; json["searchEvents"]![0]!.AsObject().Remove("chanceDivisor");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    [Theory]
    [InlineData(19999, true)] [InlineData(20000, false)]
    public void VolunteerGarrisonBoundarySurvivesSaveAndReplay(uint soldiers, bool eligible)
    {
        var d = Definition("volunteers");
        d = d with { Settlements = [d.Settlements[0] with { Soldiers = soldiers }],
            SearchEvents = [(CivicSearchEventDefinition)Event("volunteers") with { MaximumForceSoldiersExclusive = 20000 }] };
        var session = GameSession.Start(d, 20);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        session.AdvanceXun(); restored.AdvanceXun();
        Assert.Equal(eligible, session.GetCommunitySearchPrompt() is not null);
        Assert.Equal(CanonicalJson.Text(session.GetCommunitySearchPrompt()), CanonicalJson.Text(restored.GetCommunitySearchPrompt()));
        if (session.GetCommunitySearchPrompt() is { } prompt)
        {
            var command = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
                prompt.TaskId, prompt.Step, true) { CommunitySearch = CommunitySearchChoice.Accept };
            var request = session.Request(new CommandOperation(command), id: command.CommandId);
            Assert.Equal(SessionDisposition.Accepted, session.Apply(request).Disposition);
            Assert.Equal(SessionDisposition.Accepted, restored.Apply(request).Disposition);
        }
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), d, GameSession.EngineFactory(d));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }
}
