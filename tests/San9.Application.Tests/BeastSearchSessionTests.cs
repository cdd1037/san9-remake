using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class BeastSearchSessionTests
{
    private static ScenarioInitialState Definition(bool knows) => new(ScenarioInitialState.CurrentSchemaVersion, "beast", 190, 1, 1, 1,
        [new(1, "force", 1, 100) { LeaderId = 1, BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("actor", new(2, 1, 1, new(80, 50, 50, 50), false) { LearnedTactics = knows ? [101] : [], LifespanTier = 3 })],
        SearchEvents = [new BeastSearchEventDefinition([new(1, new("site.home"), [102])], [101], 1, 2, 2, 7, true)
            { Id = "beast", Consumption = EventConsumption.OnPresentation }],
        Military = new(2, 1, [0, 0], [new(0, 1, null)], [], [new(1, new(2, 2), new(1, 1))])
        { Tactics = [new(101, 7, TacticEffect.CounterConfusion, 0, 0, 0, false, false, []),
            new(102, 7, TacticEffect.CounterConfusion, 0, 0, 0, false, false, [])] }
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }], []) };
    private static ulong Seed() => Enumerable.Range(1, 10000).Select(i => (ulong)i).First(seed =>
        new StableRandom(StableRandom.DeriveSeed(seed, "event-search-eligibility")).NextUInt64() % 100 < 80 &&
        new StableRandom(StableRandom.DeriveSeed(seed, "event-beast-battle")).NextUInt64() % 100 < 16);

    [Theory]
    [InlineData(false, true, EventResult.Escaped)]
    [InlineData(true, true, EventResult.Succeeded)]
    [InlineData(true, false, EventResult.Defeated)]
    public void ChoicesRoundTripSaveAndReplayAllWorldEffects(bool fight, bool knows, EventResult expected)
    {
        var definition = Definition(knows);
        var session = GameSession.Start(definition, Seed());
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        session.AdvanceXun(); restored.AdvanceXun();
        var prompt = session.GetBeastSearchPrompt()!;
        Assert.NotNull(prompt);
        Assert.Equal(prompt, restored.GetBeastSearchPrompt());
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        string before = session.Snapshot.StateHash;
        Assert.Equal(prompt, session.GetBeastSearchPrompt());
        Assert.Equal(before, session.Snapshot.StateHash);
        Guid id = Guid.NewGuid();
        var command = new RespondDailyTaskCommand(id, prompt.ForceId, session.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, fight);
        var request = session.Request(new CommandOperation(command), id: id);
        var first = session.Apply(request);
        var second = restored.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, first.Disposition);
        Assert.Equal(CanonicalJson.Text(first.Events), CanonicalJson.Text(second.Events));
        Assert.Equal(expected, Assert.Single(first.Events.OfType<BeastSearchResolvedEvent>()).Result);
        Assert.Equal("site.home", Assert.Single(session.Snapshot.Simulation.ConsumedEventKeys).Value);
        Assert.Null(session.GetBeastSearchPrompt());
        Assert.DoesNotContain(first.Events, e => e is SearchResolvedEvent);
        var saved = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(session.Capture().StateHash, saved.Capture().StateHash);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
        var person = session.Snapshot.Officers.Single(p => p.Id == 2);
        Assert.Equal(expected == EventResult.Succeeded ? 82 : 80, person.BaseAbilities.War);
        Assert.Equal(expected == EventResult.Succeeded, person.LearnedTactics.Contains(102));
        Assert.Equal(expected == EventResult.Defeated ? 2 : 3, person.LifespanTier);
    }

    [Fact]
    public void ContentKeepsOrderedRewardsAndRejectsMissingPlacesOrTactics()
    {
        var definition = Definition(true);
        var beast = Assert.IsType<BeastSearchEventDefinition>(Assert.Single(definition.SearchEvents));
        foreach (var invalid in new[]
        {
            beast with { Sites = [new(9999, new("bad"), [])] },
            beast with { Sites = [new(1, new("bad"), [9999])] },
            beast with { BattleTacticIds = [9999] },
            beast with { Sites = [new(1, null, [])] }
        }) Assert.Throws<InvalidDataException>(() => (definition with { SearchEvents = [invalid] }).Validate());
        var first = definition with { SearchEvents = [beast with { Sites = [new(1, new("site.home"), [101, 102])] }] };
        var reversed = definition with { SearchEvents = [beast with { Sites = [new(1, new("site.home"), [102, 101])] }] };
        Assert.NotEqual(GameSession.Fingerprint(first), GameSession.Fingerprint(reversed));
        var document = JsonNode.Parse(CanonicalJson.Encode(new RespondDailyTaskCommand(Guid.NewGuid(), 1, 0, Guid.NewGuid(), 1, false)))!;
        document.AsObject().Remove("accept");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<RespondDailyTaskCommand>(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }
}
