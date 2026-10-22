using San9.Tests;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class TreasureMonumentSessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "tower", 190, 2, 1, 1,
        [new(1, "force", 1, 100) { LeaderId = 1, RankId = 999, BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("first", new(2, 1, 1, new(50, 50, 50, 50), false)), new("second", new(3, 1, 1, new(50, 50, 50, 50), false))],
        SearchEvents = [new TreasureMonumentSearchEventDefinition(1, 500, 999, 25000, 10000, 5, new("tower"))
            { Id = "tower", Consumption = EventConsumption.OnSuccess }],
        Information = InformationDefinitions.Empty with
        {
            Corps = [new San9.Core.Domain.CorpsState(1, 1, 1, new(50000, 10000)) { IsRoot = true }],
            Ranks = [new(999, "rank", 10000, 0, 0, 100)],
            Treasures = [new(500, "treasure", 0, 0, 0, null)],
            TreasureStates = [new(500, 1, TreasureDiscovery.Discovered, null)]
        }
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChoiceResumesDailyQueueAndStableSaveAgree(bool accept)
    {
        var d = Definition(); var session = GameSession.Start(d, 1);         Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, [2], 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        session.AdvanceXun(); restored.AdvanceXun();
        var p = Assert.IsType<TreasureMonumentPrompt>(session.GetTreasureMonumentPrompt());
        Assert.Equal(p, restored.GetTreasureMonumentPrompt()); Assert.Equal((500, 10000u), (p.TreasureId, p.Cost));
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        var before = session.Snapshot;
        Assert.Equal(p, session.GetTreasureMonumentPrompt()); StateAssert.Equal(before, session.Snapshot);
        Guid id = Guid.NewGuid();
        var answer = new RespondDailyTaskCommand(id, 1, session.Snapshot.Simulation.NextSequence, p.TaskId, p.Step, accept);
        var request = session.Request(new CommandOperation(answer), id: id);
        var first = session.Apply(request); var second = restored.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, first.Disposition);
        EventAssert.Equal(first.Events, second.Events);
        Assert.Equal(accept, Assert.Single(first.Events.OfType<TreasureMonumentResolvedEvent>()).Accepted);
        Assert.DoesNotContain(first.Events, e => e is SearchResolvedEvent);
        Assert.Equal(accept, session.Snapshot.Simulation.ConsumedEventKeys.Contains(new("tower")));
        Assert.Equal(accept ? 105 : 100, Assert.Single(session.Snapshot.Forces).BaseTrust);
        Assert.Equal(accept ? -10000L : 0L, first.Events.OfType<ResourceChangedEvent>().Where(e => e.Reason == "treasure-monument").Sum(e => e.Actual));
        Assert.Equal(1, Assert.Single(session.Snapshot.Treasures).HolderId);
        Assert.Null(session.GetTreasureMonumentPrompt());
        StateAssert.Equal(session.Capture(), restored.Capture());
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), d).Capture());
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, [2], 1)).Submission.Accepted);
        session.AdvanceXun(); Assert.Equal(!accept, session.GetTreasureMonumentPrompt() is not null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameDaySearchesObserveTheFirstAnswer(bool acceptFirst)
    {
        var session = GameSession.Start(Definition(), 1);
        foreach (int person in new[] { 2, 3 })
            Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, [person], 1)).Submission.Accepted);
        session.AdvanceXun(); var p = session.GetTreasureMonumentPrompt()!;
        Assert.True(session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, p.TaskId, p.Step, acceptFirst)).Submission.Accepted);
        var second = session.GetTreasureMonumentPrompt();
        Assert.Equal(!acceptFirst, second is not null);
        if (second is not null)
        {
            Assert.NotEqual(p.SearchingOfficerId, second.SearchingOfficerId);
            Assert.True(session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
                session.Snapshot.Simulation.NextSequence, second.TaskId, second.Step, true)).Submission.Accepted);
        }
        Assert.Equal(105, Assert.Single(session.Snapshot.Forces).BaseTrust);
        Assert.Single(session.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Equal(SimulationBoundary.Stable, session.Snapshot.Simulation.Boundary);
    }

    [Fact]
    public void ContentReferencesConsumptionPolicyAndCostAreValidated()
    {
        var d = Definition(); var tower = (TreasureMonumentSearchEventDefinition)d.SearchEvents[0];
        foreach (var bad in new[] { tower with { MinimumRankId = 0 }, tower with { PlaceId = 999 }, tower with { TreasureId = 999 },
            tower with { Cost = 0 }, tower with { BaseTrustReward = -1 }, tower with { ConsumptionKey = default },
            tower with { Consumption = EventConsumption.OnPresentation } })
            Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [bad] }).Validate());

    }
}
