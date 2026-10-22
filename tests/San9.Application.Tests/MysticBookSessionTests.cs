using San9.Tests;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class MysticBookSessionTests
{
    private static ScenarioInitialState Definition(bool returning = false) => new(ScenarioInitialState.CurrentSchemaVersion, "mystic", 190, 2, 1, 1,
        [new(1, "force", 1, 100) { LeaderId = 1, BaseTrust = 100, EventHistory = [new("visited")] }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(0, 0, 0, 100), false) { Status = OfficerStatus.Ruler }),
            new("first", new(2, 1, 1, new(0, 0, 0, 100), false)), new("second", new(3, 1, 1, new(0, 0, 0, 100), false)),
            new("diviner", new(4, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special })],
        SearchEvents = [new MysticBookOfferSearchEventDefinition(1, 4, 500, 1, 1500, 500, 100)
            { Id = "offer", Priority = 1, Consumption = EventConsumption.None },
            new MysticBookReturnSearchEventDefinition(1, 4, 500, 1, [26, 30, 29, 22])
            { Id = "return", Consumption = EventConsumption.None }],
        Military = new(2, 1, [0, 0], [new(0, 1, null)], [], [new(1, new(2, 2), new(1, 1))])
        { Tactics = [new(26, 7, TacticEffect.CounterConfusion, 0, 0, 0, false, false, []),
            new(30, 7, TacticEffect.CounterConfusion, 0, 0, 0, false, false, []),
            new(29, 7, TacticEffect.CounterConfusion, 0, 0, 0, false, false, []),
            new(22, 7, TacticEffect.CounterConfusion, 0, 0, 0, false, false, [])] },
        Information = InformationDefinitions.Empty with
        {
            Corps = [new San9.Core.Domain.CorpsState(1, 1, 1, new(50000, 10000)) { IsRoot = true }],
            Treasures = [new(500, "book", 2, 10, 50, 29)],
            TreasureStates = [new(500, returning ? 2 : 4, TreasureDiscovery.Discovered, null)]
        }
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BothChoicesSaveRestoreAndReplayWithTheCorrectOwnershipAndPayment(bool returning, bool accept)
    {
        var d = Definition(returning); var session = GameSession.Start(d, 1);         Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, [2], 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        session.AdvanceXun(); restored.AdvanceXun();
        Guid task; int step;
        if (returning)
        {
            var p = Assert.IsType<MysticBookReturnPrompt>(session.GetMysticBookReturnPrompt());
            Assert.Equal(p, restored.GetMysticBookReturnPrompt()); Assert.Equal(26, p.DeclineRewardTacticId);
            task = p.TaskId; step = p.Step;
        }
        else
        {
            var p = Assert.IsType<MysticBookOfferPrompt>(session.GetMysticBookOfferPrompt());
            Assert.Equal(p, restored.GetMysticBookOfferPrompt()); Assert.Equal((500u, 100u), (p.AcceptCost, p.DeclineCost));
            task = p.TaskId; step = p.Step;
        }
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        Guid id = Guid.NewGuid();
        var answer = new RespondDailyTaskCommand(id, 1, session.Snapshot.Simulation.NextSequence, task, step, accept);
        var request = session.Request(new CommandOperation(answer), id: id);
        var first = session.Apply(request); var second = restored.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, first.Disposition);
        EventAssert.Equal(first.Events, second.Events);
        Assert.DoesNotContain(first.Events, e => e is SearchResolvedEvent);
        Assert.Equal(returning ? (accept ? 4 : 2) : (accept ? 2 : 4), session.Snapshot.Treasures.Single().HolderId);
        Assert.Equal(returning ? 0 : -(accept ? 500L : 100L), first.Events.OfType<ResourceChangedEvent>().Where(e => e.Reason.StartsWith("mystic-book-", StringComparison.Ordinal)).Sum(e => e.Actual));
        Assert.Equal(returning && !accept, session.Snapshot.Officers.Single(o => o.Id == 2).LearnedTactics.Contains(26));
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Null(session.GetMysticBookOfferPrompt()); Assert.Null(session.GetMysticBookReturnPrompt());
        StateAssert.Equal(session.Capture(), restored.Capture());
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), d).Capture());
    }

    [Fact]
    public void ReturningTheBookMakesItAvailableToTheNextSearcherOnTheSameDay()
    {
        var session = GameSession.Start(Definition(true), 1);
        foreach (int actor in new[] { 2, 3 })
            Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, [actor], 1)).Submission.Accepted);
        session.AdvanceXun(); var first = Assert.IsType<MysticBookReturnPrompt>(session.GetMysticBookReturnPrompt());
        Assert.True(session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, first.TaskId, first.Step, true)).Submission.Accepted);
        var second = Assert.IsType<MysticBookOfferPrompt>(session.GetMysticBookOfferPrompt());
        Assert.Equal(3, second.SearchingOfficerId);
        Assert.True(session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, second.TaskId, second.Step, true)).Submission.Accepted);
        Assert.Equal(3, session.Snapshot.Treasures.Single().HolderId);
        Assert.Empty(session.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Equal(SimulationBoundary.Stable, session.Snapshot.Simulation.Boundary);
    }

    [Fact]
    public void ContentValidatesEveryReference()
    {
        var d = Definition(); var offer = (MysticBookOfferSearchEventDefinition)d.SearchEvents[0];
        var bookReturn = (MysticBookReturnSearchEventDefinition)d.SearchEvents[1];
        SearchEventDefinition[] bad = [offer with { PlaceId = 999 }, offer with { MysticOfficerId = 999 }, offer with { TreasureId = 999 },
            offer with { LeadershipChanceDivisor = 0 }, offer with { Consumption = EventConsumption.OnSuccess },
            bookReturn with { RewardTacticIds = [] }, bookReturn with { RewardTacticIds = [999] }, bookReturn with { RewardTacticIds = [26, 26] }];
        foreach (var invalid in bad) Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [invalid] }).Validate());

        var document = JsonNode.Parse(CanonicalJson.Encode(d))!;
        document["searchEvents"]![0]!.AsObject().Remove("declineCost");
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<ScenarioInitialState>(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }
}
