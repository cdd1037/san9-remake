using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class TigerSearchSessionTests
{
    private static ScenarioInitialState Definition => new(ScenarioInitialState.CurrentSchemaVersion, "tiger", 190, 2, 1, 1,
        [new(1, "force", 1, 100) { LeaderId = 1, BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("first", new(2, 1, 1, new(80, 0, 0, 0), false) { LifespanTier = 3 }),
            new("second", new(3, 1, 1, new(80, 0, 0, 0), false) { LifespanTier = 3 })],
        CityProvinceIds = new() { [1] = 800 },
        SearchEvents = [new TigerSearchEventDefinition([800], 1, 260, 5, 20, 1, 1, 2, 7)
            { Id = "tiger", Consumption = EventConsumption.None }]
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }], []) };
    private static ulong Seed(bool wins) => Enumerable.Range(1, 10000).Select(i => (ulong)i).First(seed =>
        new StableRandom(StableRandom.DeriveSeed(seed, "event-search-eligibility")).NextUInt64() % 100 < 80 &&
        (new StableRandom(StableRandom.DeriveSeed(seed, "event-tiger-battle")).NextUInt64() % 100 < 80) == wins);

    [Theory]
    [InlineData(false, true, EventResult.Declined)]
    [InlineData(true, true, EventResult.Succeeded)]
    [InlineData(true, false, EventResult.Defeated)]
    public void ChoicesRoundTripSaveAndReplayWithoutOrdinarySearch(bool accept, bool wins, EventResult expected)
    {
        var d = Definition; var session = GameSession.Start(d, Seed(wins)); session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        session.AdvanceXun(); restored.AdvanceXun();
        var prompt = Assert.IsType<TigerSearchPrompt>(session.GetTigerSearchPrompt());
        Assert.Equal(prompt, restored.GetTigerSearchPrompt()); Assert.Equal(80, prompt.BattleChance);
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        string before = session.Snapshot.StateHash;
        Assert.Equal(prompt, session.GetTigerSearchPrompt()); Assert.Equal(before, session.Snapshot.StateHash);
        Guid id = Guid.NewGuid();
        var request = session.Request(new CommandOperation(new RespondDailyTaskCommand(id, 1, session.Snapshot.Simulation.NextSequence,
            prompt.TaskId, prompt.Step, accept)), id: id);
        var first = session.Apply(request); var second = restored.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, first.Disposition);
        Assert.Equal(CanonicalJson.Text(first.Events), CanonicalJson.Text(second.Events));
        var result = Assert.Single(first.Events.OfType<TigerSearchResolvedEvent>());
        Assert.Equal(expected, result.Result); Assert.Equal(expected == EventResult.Defeated ? 7 : 0, result.Injury);
        Assert.Equal(expected == EventResult.Succeeded ? 81 : 80, result.War);
        Assert.Equal(expected == EventResult.Succeeded ? 101 : 98, result.BaseTrust);
        Assert.Equal(3, result.LifespanTier); Assert.DoesNotContain(first.Events, e => e is SearchResolvedEvent or BeastSearchResolvedEvent);
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys); Assert.Null(session.GetTigerSearchPrompt());
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(SaveService.Decode(SaveService.Encode(session), d)));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), d, GameSession.EngineFactory(d));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    [Fact]
    public void RefusalLeavesEventAvailableForNextSearcherOnSameDay()
    {
        var d = Definition;
        ulong seed = Enumerable.Range(1, 10000).Select(i => (ulong)i).First(s =>
        {
            var random = new StableRandom(StableRandom.DeriveSeed(s, "event-search-eligibility"));
            return random.NextUInt64() % 100 < 80 && random.NextUInt64() % 100 < 80;
        });
        var session = GameSession.Start(d, seed);
        foreach (int actor in new[] { 2, 3 })
            Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, actor, 1)).Submission.Accepted);
        session.AdvanceXun();
        foreach (int actor in new[] { 2, 3 })
        {
            var prompt = Assert.IsType<TigerSearchPrompt>(session.GetTigerSearchPrompt()); Assert.Equal(actor, prompt.OfficerId);
            Assert.True(session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
                prompt.TaskId, prompt.Step, false)).Submission.Accepted);
        }
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys); Assert.Equal(96, session.Snapshot.Forces.Single().BaseTrust);
        Assert.Null(session.GetTigerSearchPrompt());
    }

    [Fact]
    public void ContentValidatesProvinceReferencesAndAllRequiredParameters()
    {
        var d = Definition; var tiger = Assert.IsType<TigerSearchEventDefinition>(d.SearchEvents.Single());
        foreach (var invalid in new[]
        {
            tiger with { ProvinceIds = [999] }, tiger with { ProvinceIds = [] }, tiger with { ProvinceIds = [800, 800] },
            tiger with { WarChanceDivisor = 0 }, tiger with { MaximumBaseTrustExclusive = 0 }, tiger with { DefeatInjury = 15 },
            tiger with { Consumption = EventConsumption.OnPresentation }
        }) Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [invalid] }).Validate());
        Assert.NotEqual(GameSession.Fingerprint(d), GameSession.Fingerprint(d with { SearchEvents = [tiger with { BaseTrustPenalty = 3 }] }));
        var two = d with { CityProvinceIds = new() { [1] = 800, [4] = 900 },
            Settlements = [.. d.Settlements, new(4, "other", 1, 10, 10, 0, 100, 100, 0, [])],
            SearchEvents = [tiger with { ProvinceIds = [800, 900] }] };
        Assert.Equal(GameSession.Fingerprint(two), GameSession.Fingerprint(two with { SearchEvents = [tiger with { ProvinceIds = [900, 800] }] }));
        var json = JsonNode.Parse(CanonicalJson.Encode(d))!;
        json["searchEvents"]![0]!.AsObject().Remove("baseTrustPenalty");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }
}
