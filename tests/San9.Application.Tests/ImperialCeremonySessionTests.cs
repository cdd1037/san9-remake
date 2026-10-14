using San9.Tests;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ImperialCeremonySessionTests
{
    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "ceremony", 190, 2, 1, 1,
        [new(1, "force", 1, 100) { LeaderId = 1, RankId = 999, BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "home" }, new() { [1] = 1 })
    {
        Officers = [new("ruler", new(1, 1, 1, new(90, 95, 99, 100), false) { Status = OfficerStatus.Ruler }),
            new("officer", new OfficerState(2, 1, 1, new(50, 50, 50, 50), false).WithLoyalty(1, 100))],
        SearchEvents = [new ImperialCeremonySearchEventDefinition(1, 999, 5, 50, 10, new("ceremony"))
            { Id = "ceremony", Consumption = EventConsumption.OnSuccess }],
        Information = InformationDefinitions.Empty with { Corps = [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }], Ranks = [new(999, "rank.emperor", 10000, 0, 0, 100)] }
    };

    [Fact]
    public void SearchCompletesWithoutDecisionAndPersistsRewardsInSaveAndRecording()
    {
        var d = Definition(); var session = GameSession.Start(d, 1); session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 1, 1)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        var result = session.AdvanceXun(); var again = restored.AdvanceXun();
        EventAssert.Equal(result.Events, again.Events);
        Assert.Single(result.Events.OfType<ImperialCeremonyResolvedEvent>());
        Assert.DoesNotContain(result.Events, e => e is SearchResolvedEvent);
        Assert.Null(session.Snapshot.Simulation.PendingDailyTask);
        Assert.Equal(new OfficerAbilities(95, 100, 100, 100), session.Snapshot.Officers.Single(p => p.Id == 1).BaseAbilities);
        Assert.Equal(110, session.Snapshot.Officers.Single(p => p.Id == 2).LoyaltyTo(1));
        Assert.Equal(150, Assert.Single(session.Snapshot.Forces).BaseTrust);
        var saved = SaveService.Decode(SaveService.Encode(session), d);
        StateAssert.Equal(session.Capture(), saved.Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), d, GameSession.EngineFactory(d));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 1, 1)).Submission.Accepted);
        Assert.DoesNotContain(session.AdvanceXun().Events, e => e is ImperialCeremonyResolvedEvent);
        Assert.Single(session.Snapshot.Simulation.ConsumedEventKeys);
    }

    [Fact]
    public void AuthoredRankReferenceAndRewardsAreValidated()
    {
        var d = Definition(); var ceremony = (ImperialCeremonySearchEventDefinition)d.SearchEvents[0];
        foreach (var bad in new[] { ceremony with { RequiredRankId = 0 }, ceremony with { PlaceId = 999 },
            ceremony with { AbilityReward = -1 }, ceremony with { LoyaltyReward = -1 }, ceremony with { ConsumptionKey = default } })
            Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [bad] }).Validate());

    }
}
